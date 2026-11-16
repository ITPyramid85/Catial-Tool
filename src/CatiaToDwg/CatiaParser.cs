using System.Text;
using System.Text.RegularExpressions;

namespace CatiaToDwg;

public static class CatiaParser
{
    private static readonly byte[] VertexMarker = [0x05, 0x08, 0x01];
    private static readonly Regex PartNumberRx = new(@"[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+", RegexOptions.Compiled);

    public static WireModel LoadPart(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 7) != "V5_CFV2")
            throw new InvalidDataException($"{Path.GetFileName(path)} 不是 CATIA V5 文件。");

        var vertices = ExtractVertices(data);
        if (vertices.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} 中未找到可用的三维顶点。");

        var model = new WireModel
        {
            Name = ExtractPartNumber(data) ?? Path.GetFileNameWithoutExtension(path),
            SourcePath = path,
            CatiaVersion = ExtractVersion(data)
        };
        model.Vertices.AddRange(vertices);
        var circles = ExtractCircles(data);
        model.Circles.AddRange(circles);
        model.Edges.AddRange(WireframeBuilder.WithoutCircleChords(
            WireframeBuilder.BuildEdges(vertices), circles));
        ProfileBuilder.TryApply(model);
        return model;
    }

    public static List<string> LoadProductChildNames(string path)
    {
        var data = File.ReadAllBytes(path);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ExtractAsciiTokens(data))
        {
            var name = token.TrimStart('_');
            if (PartNumberRx.IsMatch(name))
                names.Add(name);
        }

        var self = ExtractPartNumber(data);
        if (self != null)
            names.Remove(self);
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static WireModel Combine(string name, string sourcePath, IEnumerable<WireModel> parts)
    {
        var combined = new WireModel
        {
            Name = name,
            SourcePath = sourcePath,
            CatiaVersion = parts.Select(p => p.CatiaVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? ""
        };
        foreach (var part in parts)
        {
            combined.Vertices.AddRange(part.Vertices);
            combined.Edges.AddRange(part.Edges);
            combined.Circles.AddRange(part.Circles);
        }
        return combined;
    }

    private static List<Vec3> ExtractVertices(byte[] data)
    {
        var points = new List<Vec3>();
        var seen = new HashSet<(long, long, long)>();
        int offset = 0;
        while (true)
        {
            int hit = IndexOf(data, VertexMarker, offset);
            if (hit < 0 || hit + 15 > data.Length)
                break;

            double x = BitConverter.ToSingle(data, hit + 3);
            double y = BitConverter.ToSingle(data, hit + 7);
            double z = BitConverter.ToSingle(data, hit + 11);
            offset = hit + 1;

            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                continue;
            if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) > 1_000_000)
                continue;

            var p = new Vec3(x, y, z).Rounded(6);
            var key = (BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y), BitConverter.DoubleToInt64Bits(p.Z));
            if (seen.Add(key))
                points.Add(p);
        }
        return points;
    }

    private static List<Circle3> ExtractCircles(byte[] data)
    {
        var circles = new List<Circle3>();
        var seen = new HashSet<(long, long, long, long)>();
        byte[] marker = [0x00, 0x33, 0x33];
        int offset = 0;
        while (true)
        {
            int hit = IndexOf(data, marker, offset);
            if (hit < 0 || hit + 67 > data.Length)
                break;
            offset = hit + 1;

            double cx = BitConverter.ToSingle(data, hit + 27 + 24);
            double cy = BitConverter.ToSingle(data, hit + 27 + 28);
            double cz = BitConverter.ToSingle(data, hit + 27 + 32);
            double radius = BitConverter.ToSingle(data, hit + 27 + 36);
            double hx = BitConverter.ToSingle(data, hit + 27 + 12);
            double hy = BitConverter.ToSingle(data, hit + 27 + 16);
            double hz = BitConverter.ToSingle(data, hit + 27 + 20);

            if (!IsFinite(cx) || !IsFinite(cy) || !IsFinite(cz) || !IsFinite(radius))
                continue;
            if (radius < 2 || radius > 5000)
                continue;
            if (Math.Max(Math.Abs(cx), Math.Max(Math.Abs(cy), Math.Abs(cz))) > 1_000_000)
                continue;

            var axis = CylinderAxis(hx, hy, hz);
            var key = (
                BitConverter.DoubleToInt64Bits(Math.Round(cx, 3)),
                BitConverter.DoubleToInt64Bits(Math.Round(cy, 3)),
                BitConverter.DoubleToInt64Bits(Math.Round(cz, 3)),
                BitConverter.DoubleToInt64Bits(Math.Round(radius, 3)));
            if (!seen.Add(key))
                continue;

            circles.Add(new Circle3(new Vec3(cx, cy, cz).Rounded(6), axis, radius));
        }
        return circles;
    }

    private static Vec3 CylinderAxis(double hx, double hy, double hz)
    {
        double ax = Math.Abs(hx), ay = Math.Abs(hy), az = Math.Abs(hz);
        if (ax <= ay && ax <= az) return new Vec3(1, 0, 0);
        if (ay <= ax && ay <= az) return new Vec3(0, 1, 0);
        return new Vec3(0, 0, 1);
    }

    private static string? ExtractPartNumber(byte[] data)
    {
        string? after = ReadAsciiAfter(data, "ASMPRODUCT");
        if (!string.IsNullOrWhiteSpace(after) && PartNumberRx.IsMatch(after))
            return PartNumberRx.Match(after).Value;
        return null;
    }

    private static string ExtractVersion(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data);
        var match = Regex.Match(text, @"V5R\d+SP\d+HF\d+");
        if (match.Success)
            return match.Value;
        match = Regex.Match(text, @"CATIAV5R\d+");
        return match.Success ? match.Value : "CATIA V5";
    }

    private static IEnumerable<string> ExtractAsciiTokens(byte[] data)
    {
        var current = new StringBuilder();
        foreach (byte b in data)
        {
            if (b >= 32 && b < 127)
            {
                current.Append((char)b);
            }
            else if (current.Length >= 4)
            {
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Clear();
            }
        }
        if (current.Length >= 4)
            yield return current.ToString();
    }

    private static string? ReadAsciiAfter(byte[] data, string key)
    {
        var needle = Encoding.ASCII.GetBytes(key);
        int i = IndexOf(data, needle, 0);
        if (i < 0)
            return null;
        int p = i + needle.Length;
        while (p < data.Length && data[p] < 33)
            p++;
        int start = p;
        while (p < data.Length && data[p] >= 33 && data[p] < 127)
            p++;
        return start < p ? Encoding.ASCII.GetString(data, start, p - start) : null;
    }

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        int max = data.Length - needle.Length;
        for (int i = start; i <= max; i++)
        {
            int n = 0;
            while (n < needle.Length && data[i + n] == needle[n])
                n++;
            if (n == needle.Length)
                return i;
        }
        return -1;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
