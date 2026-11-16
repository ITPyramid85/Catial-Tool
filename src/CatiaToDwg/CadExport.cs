using System.Globalization;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;

namespace CatiaToDwg;

public static class CadExport
{
    public static void WriteDxf(DrawingSheet sheet, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        writer.WriteLine("0"); writer.WriteLine("SECTION");
        writer.WriteLine("2"); writer.WriteLine("HEADER");
        writer.WriteLine("9"); writer.WriteLine("$ACADVER");
        writer.WriteLine("1"); writer.WriteLine("AC1009");
        writer.WriteLine("9"); writer.WriteLine("$INSUNITS");
        writer.WriteLine("70"); writer.WriteLine("4");
        writer.WriteLine("9"); writer.WriteLine("$EXTMIN");
        writer.WriteLine("10"); writer.WriteLine("0");
        writer.WriteLine("20"); writer.WriteLine("0");
        writer.WriteLine("30"); writer.WriteLine("0");
        writer.WriteLine("9"); writer.WriteLine("$EXTMAX");
        writer.WriteLine("10"); writer.WriteLine(F(sheet.Width));
        writer.WriteLine("20"); writer.WriteLine(F(sheet.Height));
        writer.WriteLine("30"); writer.WriteLine("0");
        writer.WriteLine("0"); writer.WriteLine("ENDSEC");

        writer.WriteLine("0"); writer.WriteLine("SECTION");
        writer.WriteLine("2"); writer.WriteLine("TABLES");
        writer.WriteLine("0"); writer.WriteLine("TABLE");
        writer.WriteLine("2"); writer.WriteLine("LAYER");
        writer.WriteLine("70"); writer.WriteLine("5");
        WriteLayer(writer, "0", 7);
        WriteLayer(writer, "GEOMETRY", 7);
        WriteLayer(writer, "FRAME", 8);
        WriteLayer(writer, "TEXT", 4);
        WriteLayer(writer, "DIM", 3);
        writer.WriteLine("0"); writer.WriteLine("ENDTAB");
        writer.WriteLine("0"); writer.WriteLine("ENDSEC");

        writer.WriteLine("0"); writer.WriteLine("SECTION");
        writer.WriteLine("2"); writer.WriteLine("ENTITIES");
        foreach (var line in sheet.Lines)
        {
            writer.WriteLine("0"); writer.WriteLine("LINE");
            writer.WriteLine("8"); writer.WriteLine(line.Layer);
            writer.WriteLine("62"); writer.WriteLine(line.Color.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("10"); writer.WriteLine(F(line.A.X));
            writer.WriteLine("20"); writer.WriteLine(F(line.A.Y));
            writer.WriteLine("30"); writer.WriteLine("0");
            writer.WriteLine("11"); writer.WriteLine(F(line.B.X));
            writer.WriteLine("21"); writer.WriteLine(F(line.B.Y));
            writer.WriteLine("31"); writer.WriteLine("0");
        }
        foreach (var text in sheet.Texts)
        {
            writer.WriteLine("0"); writer.WriteLine("TEXT");
            writer.WriteLine("8"); writer.WriteLine(text.Layer);
            writer.WriteLine("62"); writer.WriteLine(text.Color.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("10"); writer.WriteLine(F(text.Position.X));
            writer.WriteLine("20"); writer.WriteLine(F(text.Position.Y));
            writer.WriteLine("30"); writer.WriteLine("0");
            writer.WriteLine("40"); writer.WriteLine(F(text.Height));
            writer.WriteLine("1"); writer.WriteLine(Sanitize(text.Value));
        }
        foreach (var circle in sheet.Circles)
        {
            writer.WriteLine("0"); writer.WriteLine("CIRCLE");
            writer.WriteLine("8"); writer.WriteLine(circle.Layer);
            writer.WriteLine("62"); writer.WriteLine(circle.Color.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("10"); writer.WriteLine(F(circle.Center.X));
            writer.WriteLine("20"); writer.WriteLine(F(circle.Center.Y));
            writer.WriteLine("30"); writer.WriteLine("0");
            writer.WriteLine("40"); writer.WriteLine(F(circle.Radius));
            writer.WriteLine("210"); writer.WriteLine("0");
            writer.WriteLine("220"); writer.WriteLine("0");
            writer.WriteLine("230"); writer.WriteLine("1");
        }
        writer.WriteLine("0"); writer.WriteLine("ENDSEC");
        writer.WriteLine("0"); writer.WriteLine("EOF");
    }

    public static void WriteDwg(DrawingSheet sheet, string path)
    {
        var doc = new CadDocument();
        doc.Header.Version = ACadVersion.AC1015;

        EnsureLayer(doc, "GEOMETRY", 7);
        EnsureLayer(doc, "FRAME", 8);
        EnsureLayer(doc, "TEXT", 4);
        EnsureLayer(doc, "DIM", 3);

        foreach (var line in sheet.Lines)
        {
            var entity = new Line
            {
                StartPoint = new XYZ(line.A.X, line.A.Y, 0),
                EndPoint = new XYZ(line.B.X, line.B.Y, 0),
                Layer = doc.Layers[line.Layer],
                Color = new ACadSharp.Color(line.Color)
            };
            doc.Entities.Add(entity);
        }

        foreach (var text in sheet.Texts)
        {
            var entity = new TextEntity
            {
                InsertPoint = new XYZ(text.Position.X, text.Position.Y, 0),
                Height = Math.Max(text.Height, 0.1),
                Value = Sanitize(text.Value),
                Layer = doc.Layers[text.Layer],
                Color = new ACadSharp.Color(text.Color)
            };
            doc.Entities.Add(entity);
        }

        foreach (var circle in sheet.Circles)
        {
            var entity = new Circle
            {
                Center = new XYZ(circle.Center.X, circle.Center.Y, 0),
                Radius = Math.Max(circle.Radius, 0.01),
                Layer = doc.Layers[circle.Layer],
                Color = new ACadSharp.Color(circle.Color)
            };
            doc.Entities.Add(entity);
        }

        using var writer = new DwgWriter(path, doc);
        writer.Write();
    }

    private static void EnsureLayer(CadDocument doc, string name, short color)
    {
        if (doc.Layers.Contains(name))
            return;
        doc.Layers.Add(new Layer(name) { Color = new ACadSharp.Color(color) });
    }

    private static void WriteLayer(StreamWriter writer, string name, short color)
    {
        writer.WriteLine("0"); writer.WriteLine("LAYER");
        writer.WriteLine("2"); writer.WriteLine(name);
        writer.WriteLine("70"); writer.WriteLine("0");
        writer.WriteLine("62"); writer.WriteLine(color.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("6"); writer.WriteLine("CONTINUOUS");
    }

    private static string Sanitize(string value) =>
        value.Replace("\r", " ").Replace("\n", " ");
}
