using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CatiaToDwg;

public static class PdfExport
{
    private static readonly object FontLock = new();
    private static bool _fontsReady;

    public static void Write(DrawingSheet sheet, string path)
    {
        EnsureFonts();

        using var document = new PdfDocument();
        document.Info.Title = sheet.Title;
        document.Info.Creator = "CATIA 转 2D DWG 转换器";

        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(sheet.Width);
        page.Height = XUnit.FromMillimeter(sheet.Height);

        using var gfx = XGraphics.FromPdfPage(page, XGraphicsUnit.Millimeter);
        gfx.SmoothingMode = XSmoothingMode.AntiAlias;

        gfx.DrawRectangle(XBrushes.White, 0, 0, sheet.Width, sheet.Height);

        var geo = new XPen(XColor.FromArgb(20, 20, 20), 0.18);
        var frame = new XPen(XColor.FromArgb(90, 90, 90), 0.25);
        var dim = new XPen(XColor.FromArgb(0, 120, 60), 0.18);
        var circlePen = new XPen(XColor.FromArgb(0, 70, 160), 0.22);
        geo.LineCap = XLineCap.Round;
        frame.LineCap = XLineCap.Round;
        dim.LineCap = XLineCap.Round;
        circlePen.LineCap = XLineCap.Round;

        foreach (var line in sheet.Lines)
        {
            var pen = line.Layer switch
            {
                "FRAME" => frame,
                "DIM" => dim,
                _ => geo
            };
            gfx.DrawLine(pen, line.A.X, FlipY(sheet, line.A.Y), line.B.X, FlipY(sheet, line.B.Y));
        }

        foreach (var circle in sheet.Circles)
        {
            double r = circle.Radius;
            gfx.DrawEllipse(circlePen, circle.Center.X - r, FlipY(sheet, circle.Center.Y) - r, r * 2, r * 2);
        }

        var textBrush = new XSolidBrush(XColor.FromArgb(20, 40, 90));
        var dimBrush = new XSolidBrush(XColor.FromArgb(0, 100, 50));
        foreach (var text in sheet.Texts)
        {
            double heightMm = Math.Max(text.Height, 1.6);
            double heightPt = heightMm * 72.0 / 25.4;
            var font = new XFont("Microsoft YaHei", heightPt, XFontStyleEx.Regular);
            var brush = text.Layer == "DIM" ? dimBrush : textBrush;
            gfx.DrawString(
                text.Value,
                font,
                brush,
                new XPoint(text.Position.X, FlipY(sheet, text.Position.Y)),
                XStringFormats.BaseLineLeft);
        }

        document.Save(path);
    }

    private static double FlipY(DrawingSheet sheet, double y) => sheet.Height - y;

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsReady)
                return;
            GlobalFontSettings.FontResolver = new WindowsCjkFontResolver();
            _fontsReady = true;
        }
    }

    private sealed class WindowsCjkFontResolver : IFontResolver
    {
        private const string FaceName = "CjkFace";
        private readonly byte[] _data;

        public WindowsCjkFontResolver()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates =
            [
                "msyh.ttf",
                "simhei.ttf",
                "msyh.ttc",
                "simsun.ttc",
                "arialuni.ttf",
                "arial.ttf"
            ];

            foreach (var file in candidates)
            {
                string path = Path.Combine(fonts, file);
                if (!File.Exists(path))
                    continue;
                _data = File.ReadAllBytes(path);
                return;
            }

            throw new FileNotFoundException("未找到可用于 PDF 的中文字体（微软雅黑 / 黑体 / 宋体）。");
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new FontResolverInfo(FaceName, isBold, isItalic);

        public byte[] GetFont(string faceName) => _data;
    }
}
