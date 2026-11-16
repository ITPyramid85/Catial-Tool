using System.Globalization;

namespace CatiaToDwg;

public static class DrawingBuilder
{
    public const double SheetWidth = 420;
    public const double SheetHeight = 297;
    private const double Margin = 12;
    private const double TitleHeight = 36;
    private static readonly double[] StandardScales = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];

    public static DrawingSheet Build(WireModel model)
    {
        var sheet = new DrawingSheet
        {
            Width = SheetWidth,
            Height = SheetHeight,
            Title = model.Name
        };

        AddFrame(sheet);
        var views = CreateViews();
        var projected = views.ToDictionary(v => v.Name, v => Project(model, v.Project));
        double scaleDenom = ChooseScale(projected.Values.Select(p => p.FitBounds).ToList());
        double scale = 1.0 / scaleDenom;
        sheet.ScaleLabel = scaleDenom <= 1.0001 ? "1:1" : $"1:{scaleDenom.ToString("0", CultureInfo.InvariantCulture)}";

        var viewArea = new BBox2(Margin, Margin + TitleHeight, SheetWidth - Margin, SheetHeight - Margin);
        PlaceViews(sheet, views, projected, viewArea, scale);
        AddTitleBlock(sheet, model);
        return sheet;
    }

    private static ViewSpec[] CreateViews() =>
    [
        new("俯视图", "沿 +Y 观察（长度 × 厚度）", p => new Vec2(p.X, -p.Z), 0, 1),
        new("轴测图", "三维投影", Iso, 1, 1),
        new("主视图", "沿 +Z 观察（与 CATIA 三维正面一致）", p => new Vec2(p.X, p.Y), 0, 0),
        new("右视图", "沿 +X 观察（宽度 × 厚度）", p => new Vec2(-p.Z, p.Y), 1, 0)
    ];

    private static Vec2 Iso(Vec3 p)
    {
        const double c = 0.8660254037844386;
        const double s = 0.5;
        return new Vec2((p.X - p.Z) * c, p.Y + (p.X + p.Z) * s);
    }

    private static ProjectedView Project(WireModel model, Func<Vec3, Vec2> project)
    {
        var lines = model.Edges
            .Select(e => (A: project(e.A), B: project(e.B)))
            .Where(e => Dist2(e.A, e.B) > 1e-8)
            .ToList();
        var circles = new List<(Vec2 Center, double Radius)>();
        var extra = new List<Vec2>();

        foreach (var circle in model.Circles)
            AddClosedCurve(project, Tessellate(circle, 128), circle.Radius, lines, circles, extra);

        foreach (var ellipse in model.Ellipses)
        {
            var ring = Ellipse3.Tessellate(ellipse, 128).Select(project).ToList();
            extra.AddRange(ring);
            var bb = BBox2.FromPoints(ring);
            double rx = bb.SizeX / 2.0;
            double ry = bb.SizeY / 2.0;
            if (Math.Abs(rx - ry) <= 0.12 * Math.Max(Math.Max(rx, ry), 1))
                circles.Add((bb.Center, Math.Max(rx, ry)));
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if (Dist2(a, b) > 1e-8)
                    lines.Add((a, b));
            }
        }

        var edgePts = lines.SelectMany(e => new[] { e.A, e.B }).ToList();
        var pts = edgePts.Concat(extra).ToList();
        if (pts.Count == 0)
            pts.AddRange(model.Vertices.Select(project));
        if (edgePts.Count == 0)
            edgePts = pts;
        return new ProjectedView(lines, circles, BBox2.FromPoints(pts), BBox2.FromPoints(edgePts));
    }

    private static void AddClosedCurve(
        Func<Vec3, Vec2> project,
        List<Vec3> world,
        double radius,
        List<(Vec2 A, Vec2 B)> lines,
        List<(Vec2 Center, double Radius)> circles,
        List<Vec2> extra)
    {
        var ring = world.Select(project).ToList();
        extra.AddRange(ring);
        var bb = BBox2.FromPoints(ring);
        double rx = bb.SizeX / 2.0;
        double ry = bb.SizeY / 2.0;
        bool faceOn = Math.Abs(rx - ry) <= 0.12 * Math.Max(radius, 1)
                      && Math.Abs(rx - radius) <= 0.12 * radius;
        if (faceOn)
            circles.Add((bb.Center, radius));
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            if (Dist2(a, b) > 1e-8)
                lines.Add((a, b));
        }
    }

    private static List<Vec3> Tessellate(Circle3 circle, int steps)
    {
        var axis = circle.Axis.Normalized();
        var tmp = Math.Abs(axis.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var u = Vec3.Cross(axis, tmp).Normalized();
        var v = Vec3.Cross(axis, u).Normalized();
        var pts = new List<Vec3>(steps);
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            pts.Add(circle.Center + u * (circle.Radius * Math.Cos(t)) + v * (circle.Radius * Math.Sin(t)));
        }
        return pts;
    }

    private static double ChooseScale(List<BBox2> boxes)
    {
        double maxW = boxes.Max(b => Math.Max(b.SizeX, 1));
        double maxH = boxes.Max(b => Math.Max(b.SizeY, 1));
        double cellW = (SheetWidth - 2 * Margin - 10) / 2.0 - 16;
        double cellH = (SheetHeight - 2 * Margin - TitleHeight - 10) / 2.0 - 18;
        double needed = Math.Max(maxW / Math.Max(cellW, 1), maxH / Math.Max(cellH, 1));
        foreach (double s in StandardScales)
        {
            if (s + 1e-9 >= needed)
                return s;
        }
        return StandardScales[^1];
    }

    private static void PlaceViews(
        DrawingSheet sheet,
        ViewSpec[] views,
        Dictionary<string, ProjectedView> projected,
        BBox2 area,
        double scale)
    {
        double gap = 8;
        double cellW = (area.SizeX - gap) / 2.0;
        double cellH = (area.SizeY - gap) / 2.0;

        foreach (var view in views)
        {
            double x0 = area.MinX + view.Col * (cellW + gap);
            double y0 = area.MinY + view.Row * (cellH + gap);
            var proj = projected[view.Name];
            var b = proj.FitBounds;
            double w = Math.Max(b.SizeX * scale, 0.1);
            double h = Math.Max(b.SizeY * scale, 0.1);
            double ox = x0 + (cellW - w) / 2.0 - b.MinX * scale;
            double oy = y0 + 14 + (cellH - 18 - h) / 2.0 - b.MinY * scale;

            foreach (var (a, bpt) in proj.Lines)
            {
                sheet.Lines.Add(new SheetLine
                {
                    A = new Vec2(a.X * scale + ox, a.Y * scale + oy),
                    B = new Vec2(bpt.X * scale + ox, bpt.Y * scale + oy),
                    Layer = "GEOMETRY",
                    Color = 7
                });
            }

            foreach (var (center, radius) in proj.Circles)
            {
                sheet.Circles.Add(new SheetCircle
                {
                    Center = new Vec2(center.X * scale + ox, center.Y * scale + oy),
                    Radius = radius * scale,
                    Layer = "GEOMETRY",
                    Color = 7
                });
            }

            sheet.Texts.Add(new SheetText
            {
                Position = new Vec2(x0 + 3, y0 + 3),
                Value = $"{view.Name}  {view.Hint}",
                Height = 2.4,
                Layer = "TEXT",
                Color = 4
            });

            if (view.Name == "主视图")
                AddOverallDims(sheet, proj.FitBounds, scale, ox, oy);
        }
    }

    private static void AddOverallDims(DrawingSheet sheet, BBox2 b, double scale, double ox, double oy)
    {
        if (b.SizeX < 1e-6 && b.SizeY < 1e-6)
            return;

        double x1 = b.MinX * scale + ox;
        double x2 = b.MaxX * scale + ox;
        double y1 = b.MinY * scale + oy;
        double y2 = b.MaxY * scale + oy;
        double dimOff = 6;

        sheet.Lines.Add(DimLine(x1, y1 - dimOff, x2, y1 - dimOff));
        sheet.Lines.Add(DimLine(x1, y1 - dimOff - 1.5, x1, y1 - dimOff + 1.5));
        sheet.Lines.Add(DimLine(x2, y1 - dimOff - 1.5, x2, y1 - dimOff + 1.5));
        sheet.Texts.Add(new SheetText
        {
            Position = new Vec2((x1 + x2) / 2 - Math.Min(18, Mm(b.SizeX).Length * 0.9), y1 - dimOff + 1.2),
            Value = Mm(b.SizeX),
            Height = 2.2,
            Layer = "DIM",
            Color = 3
        });

        sheet.Lines.Add(DimLine(x2 + dimOff, y1, x2 + dimOff, y2));
        sheet.Lines.Add(DimLine(x2 + dimOff - 1.5, y1, x2 + dimOff + 1.5, y1));
        sheet.Lines.Add(DimLine(x2 + dimOff - 1.5, y2, x2 + dimOff + 1.5, y2));
        sheet.Texts.Add(new SheetText
        {
            Position = new Vec2(x2 + dimOff + 1.6, (y1 + y2) / 2 - 1),
            Value = Mm(b.SizeY),
            Height = 2.2,
            Layer = "DIM",
            Color = 3
        });
    }

    private static SheetLine DimLine(double x1, double y1, double x2, double y2) => new()
    {
        A = new Vec2(x1, y1),
        B = new Vec2(x2, y2),
        Layer = "DIM",
        Color = 3
    };

    private static void AddFrame(DrawingSheet sheet)
    {
        Rect(sheet, 5, 5, SheetWidth - 5, SheetHeight - 5, "FRAME", 8);
        Rect(sheet, Margin, Margin, SheetWidth - Margin, SheetHeight - Margin, "FRAME", 8);
        sheet.Lines.Add(new SheetLine
        {
            A = new Vec2(Margin, Margin + TitleHeight),
            B = new Vec2(SheetWidth - Margin, Margin + TitleHeight),
            Layer = "FRAME",
            Color = 8
        });
    }

    private static void AddTitleBlock(DrawingSheet sheet, WireModel model)
    {
        double y0 = Margin;
        double x0 = Margin;
        double x1 = SheetWidth - Margin;
        double mid = x0 + 250;

        sheet.Lines.Add(new SheetLine
        {
            A = new Vec2(mid, y0),
            B = new Vec2(mid, y0 + TitleHeight),
            Layer = "FRAME",
            Color = 8
        });

        var bbox = model.Bounds;
        string overall = $"{Mm(bbox.SizeX)} x {Mm(bbox.SizeY)} x {Mm(bbox.SizeZ)} mm  (X Y Z)";
        string source = Path.GetFileName(model.SourcePath);
        string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        sheet.Texts.Add(new SheetText { Position = new Vec2(x0 + 4, y0 + 26), Value = "CATIA 三维  →  二维 DWG", Height = 3.2, Layer = "TEXT", Color = 4 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(x0 + 4, y0 + 18), Value = $"图名  {model.Name}", Height = 3.6, Layer = "TEXT", Color = 7 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(x0 + 4, y0 + 11), Value = $"来源  {source}    {model.CatiaVersion}", Height = 2.3, Layer = "TEXT", Color = 7 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(x0 + 4, y0 + 4), Value = $"尺寸  {overall}    顶点 {model.Vertices.Count}  边 {model.Edges.Count}  圆 {model.Circles.Count}  椭圆孔 {model.Ellipses.Count}", Height = 2.3, Layer = "TEXT", Color = 7 });

        sheet.Texts.Add(new SheetText { Position = new Vec2(mid + 6, y0 + 26), Value = "图幅  A3  横向", Height = 2.3, Layer = "TEXT", Color = 7 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(mid + 6, y0 + 18), Value = $"比例  {sheet.ScaleLabel}", Height = 3.2, Layer = "TEXT", Color = 7 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(mid + 6, y0 + 11), Value = "单位  毫米", Height = 2.3, Layer = "TEXT", Color = 7 });
        sheet.Texts.Add(new SheetText { Position = new Vec2(mid + 6, y0 + 4), Value = $"日期  {when}", Height = 2.3, Layer = "TEXT", Color = 7 });

        sheet.Texts.Add(new SheetText
        {
            Position = new Vec2(x1 - 140, y0 + TitleHeight + 4),
            Value = "正交视图（主视图 = CATIA 三维正面，含缺口与圆孔）",
            Height = 2.0,
            Layer = "TEXT",
            Color = 8
        });
    }

    private static void Rect(DrawingSheet sheet, double x0, double y0, double x1, double y1, string layer, short color)
    {
        sheet.Lines.Add(new SheetLine { A = new Vec2(x0, y0), B = new Vec2(x1, y0), Layer = layer, Color = color });
        sheet.Lines.Add(new SheetLine { A = new Vec2(x1, y0), B = new Vec2(x1, y1), Layer = layer, Color = color });
        sheet.Lines.Add(new SheetLine { A = new Vec2(x1, y1), B = new Vec2(x0, y1), Layer = layer, Color = color });
        sheet.Lines.Add(new SheetLine { A = new Vec2(x0, y1), B = new Vec2(x0, y0), Layer = layer, Color = color });
    }

    private static string Mm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static double Dist2(Vec2 a, Vec2 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private sealed record ViewSpec(string Name, string Hint, Func<Vec3, Vec2> Project, int Col, int Row);
    private sealed record ProjectedView(
        List<(Vec2 A, Vec2 B)> Lines,
        List<(Vec2 Center, double Radius)> Circles,
        BBox2 Bounds,
        BBox2 FitBounds);
}
