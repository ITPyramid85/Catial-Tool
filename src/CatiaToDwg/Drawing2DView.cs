namespace CatiaToDwg;

public sealed class Drawing2DView : Control
{
    private DrawingSheet? _sheet;
    private float _zoom = 1f;
    private PointF _pan;
    private Point _lastMouse;
    private bool _panning;

    public Drawing2DView()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(32, 36, 42);
        Cursor = Cursors.SizeAll;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    public void SetSheet(DrawingSheet? sheet)
    {
        _sheet = sheet;
        _zoom = 1f;
        _pan = PointF.Empty;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _panning = true;
            _lastMouse = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _panning = false;
        Capture = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_panning)
            return;
        _pan.X += e.X - _lastMouse.X;
        _pan.Y += e.Y - _lastMouse.Y;
        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom *= e.Delta > 0 ? 1.12f : 0.89f;
        _zoom = Math.Clamp(_zoom, 0.2f, 12f);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var border = new Pen(Color.FromArgb(170, 150, 80), 1.5f);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        using var titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        using var hintFont = new Font("Microsoft YaHei UI", 8.5f);
        using var titleBrush = new SolidBrush(Color.FromArgb(220, 210, 160));
        using var hintBrush = new SolidBrush(Color.FromArgb(160, 160, 150));
        g.DrawString("二维图纸查看器", titleFont, titleBrush, 10, 8);

        if (_sheet == null)
        {
            g.DrawString("转换完成后将在此显示二维图纸。", hintFont, hintBrush, 10, 32);
            g.DrawString("拖动：平移    滚轮：缩放", hintFont, hintBrush, 10, 50);
            return;
        }

        g.DrawString($"{_sheet.Title}    {_sheet.ScaleLabel}    线{_sheet.Lines.Count} 圆{_sheet.Circles.Count}    拖动：平移  滚轮：缩放",
            hintFont, hintBrush, 10, 30);

        float margin = 48;
        float availW = Math.Max(Width - 20, 10);
        float availH = Math.Max(Height - margin - 12, 10);
        float fit = Math.Min(availW / (float)_sheet.Width, availH / (float)_sheet.Height);
        float s = fit * _zoom;
        float ox = 10 + _pan.X + (availW - (float)_sheet.Width * s) / 2f;
        float oy = margin + _pan.Y + (availH - (float)_sheet.Height * s) / 2f;

        g.TranslateTransform(ox, oy);
        g.ScaleTransform(s, -s);
        g.TranslateTransform(0, (float)-_sheet.Height);

        using var paper = new SolidBrush(Color.FromArgb(248, 248, 244));
        g.FillRectangle(paper, 0, 0, (float)_sheet.Width, (float)_sheet.Height);

        using var geo = new Pen(Color.FromArgb(20, 20, 20), 0.35f);
        using var frame = new Pen(Color.FromArgb(90, 90, 90), 0.4f);
        using var dim = new Pen(Color.FromArgb(0, 120, 60), 0.3f);
        using var circlePen = new Pen(Color.FromArgb(0, 70, 160), 0.45f);

        foreach (var line in _sheet.Lines)
        {
            var pen = line.Layer switch
            {
                "FRAME" => frame,
                "DIM" => dim,
                _ => geo
            };
            g.DrawLine(pen, (float)line.A.X, (float)line.A.Y, (float)line.B.X, (float)line.B.Y);
        }

        foreach (var circle in _sheet.Circles)
        {
            float r = (float)circle.Radius;
            g.DrawEllipse(circlePen, (float)circle.Center.X - r, (float)circle.Center.Y - r, r * 2, r * 2);
        }

        using var textBrush = new SolidBrush(Color.FromArgb(20, 40, 90));
        using var dimBrush = new SolidBrush(Color.FromArgb(0, 100, 50));
        foreach (var text in _sheet.Texts)
        {
            float h = Math.Max((float)text.Height, 1.6f);
            using var font = new Font("Microsoft YaHei UI", Math.Max(h * 0.85f, 1.2f), FontStyle.Regular, GraphicsUnit.World);
            var brush = text.Layer == "DIM" ? dimBrush : textBrush;
            var state = g.Save();
            g.TranslateTransform((float)text.Position.X, (float)text.Position.Y);
            g.ScaleTransform(1, -1);
            g.DrawString(text.Value, font, brush, 0, -h);
            g.Restore(state);
        }
    }
}
