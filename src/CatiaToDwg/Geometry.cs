namespace CatiaToDwg;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3 Rounded(int digits = 6) => new(Math.Round(X, digits), Math.Round(Y, digits), Math.Round(Z, digits));
    public Vec3 Normalized()
    {
        double len = Length;
        return len < 1e-12 ? this : this * (1.0 / len);
    }
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public static IEnumerable<Vec3> Tessellate(Circle3 circle, int steps = 64)
    {
        var axis = circle.Axis.Normalized();
        var tmp = Math.Abs(axis.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var u = Cross(axis, tmp).Normalized();
        var v = Cross(axis, u).Normalized();
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            yield return circle.Center + u * (circle.Radius * Math.Cos(t)) + v * (circle.Radius * Math.Sin(t));
        }
    }
}

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec2 Rounded(int digits = 4) => new(Math.Round(X, digits), Math.Round(Y, digits));
}

public readonly record struct Edge3(Vec3 A, Vec3 B);

public readonly record struct Circle3(Vec3 Center, Vec3 Axis, double Radius);

public readonly record struct Ellipse3(Vec3 Center, Vec3 Axis, Vec3 UDir, double RadiusU, double RadiusV)
{
    public static IEnumerable<Vec3> Tessellate(Ellipse3 e, int steps = 96)
    {
        var axis = e.Axis.Normalized();
        var u = e.UDir.Normalized();
        var v = Vec3.Cross(axis, u).Normalized();
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            yield return e.Center + u * (e.RadiusU * Math.Cos(t)) + v * (e.RadiusV * Math.Sin(t));
        }
    }
}

public readonly record struct BBox3(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;

    public static BBox3 FromPoints(IEnumerable<Vec3> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
            return new BBox3(0, 0, 0, 0, 0, 0);
        return new BBox3(
            list.Min(p => p.X), list.Min(p => p.Y), list.Min(p => p.Z),
            list.Max(p => p.X), list.Max(p => p.Y), list.Max(p => p.Z));
    }
}

public readonly record struct BBox2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public static BBox2 FromPoints(IEnumerable<Vec2> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
            return new BBox2(0, 0, 0, 0);
        return new BBox2(
            list.Min(p => p.X), list.Min(p => p.Y),
            list.Max(p => p.X), list.Max(p => p.Y));
    }
}

public sealed class WireModel
{
    public string Name { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string CatiaVersion { get; init; } = "";
    public List<Vec3> Vertices { get; } = [];
    public List<Edge3> Edges { get; } = [];
    public List<Circle3> Circles { get; } = [];
    public List<Ellipse3> Ellipses { get; } = [];
    public List<Vec3> MeshVertices { get; } = [];
    public List<int> MeshIndices { get; } = [];

    public BBox3 Bounds
    {
        get
        {
            var pts = Vertices
                .Concat(Circles.SelectMany(c => Vec3.Tessellate(c, 12)))
                .Concat(Ellipses.SelectMany(e => Ellipse3.Tessellate(e, 12)))
                .Concat(MeshVertices);
            return BBox3.FromPoints(pts);
        }
    }
}

public sealed class SheetLine
{
    public Vec2 A { get; init; }
    public Vec2 B { get; init; }
    public string Layer { get; init; } = "GEOMETRY";
    public short Color { get; init; } = 7;
}

public sealed class SheetText
{
    public Vec2 Position { get; init; }
    public string Value { get; init; } = "";
    public double Height { get; init; } = 2.5;
    public string Layer { get; init; } = "TEXT";
    public short Color { get; init; } = 7;
}

public sealed class SheetCircle
{
    public Vec2 Center { get; init; }
    public double Radius { get; init; }
    public string Layer { get; init; } = "GEOMETRY";
    public short Color { get; init; } = 7;
}

public sealed class SheetEllipse
{
    public Vec2 Center { get; init; }
    public double RadiusX { get; init; }
    public double RadiusY { get; init; }
    public string Layer { get; init; } = "GEOMETRY";
    public short Color { get; init; } = 7;
}

public sealed class DrawingSheet
{
    public double Width { get; init; } = 420;
    public double Height { get; init; } = 297;
    public string Title { get; init; } = "";
    public string ScaleLabel { get; set; } = "1:1";
    public List<SheetLine> Lines { get; } = [];
    public List<SheetText> Texts { get; } = [];
    public List<SheetCircle> Circles { get; } = [];
    public List<SheetEllipse> Ellipses { get; } = [];
}
