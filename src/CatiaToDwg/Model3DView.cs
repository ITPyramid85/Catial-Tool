using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using WinFormsColor = System.Drawing.Color;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace CatiaToDwg;

public sealed class Model3DView : UserControl
{
    private readonly ElementHost _host;
    private readonly HelixViewport3D _viewport;

    public Model3DView()
    {
        DoubleBuffered = true;
        BackColor = WinFormsColor.FromArgb(0x6E, 0x6E, 0x6E);
        TabStop = true;

        _viewport = CreateViewport();
        _host = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = _viewport
        };
        Controls.Add(_host);
        Rebuild(null);
    }

    public void SetModel(WireModel? model)
    {
        Rebuild(model);
    }

    private static HelixViewport3D CreateViewport()
    {
        var viewport = new HelixViewport3D
        {
            Background = new LinearGradientBrush(
                WpfColor.FromRgb(0x2A, 0x18, 0x48),
                WpfColor.FromRgb(0x18, 0x28, 0x58),
                90),
            ShowCoordinateSystem = true,
            ShowViewCube = true,
            IsHeadLightEnabled = true,
            ZoomExtentsWhenLoaded = true,
            CameraMode = CameraMode.Inspect,
            CameraRotationMode = CameraRotationMode.Trackball,
            ModelUpDirection = new Vector3D(0, 1, 0),
            Orthographic = false,
            Title = "三维模型查看器",
            SubTitle = "左键旋转  中键/右键平移  滚轮缩放  单击视立方体切换视角",
            TextBrush = System.Windows.Media.Brushes.White,
            ViewCubeFrontText = "前",
            ViewCubeBackText = "后",
            ViewCubeLeftText = "左",
            ViewCubeRightText = "右",
            ViewCubeTopText = "上",
            ViewCubeBottomText = "下",
            ViewCubeHorizontalPosition = WpfHorizontalAlignment.Right,
            ViewCubeVerticalPosition = WpfVerticalAlignment.Top,
            CoordinateSystemHorizontalPosition = WpfHorizontalAlignment.Left,
            RotateGesture = new MouseGesture(MouseAction.LeftClick),
            PanGesture = new MouseGesture(MouseAction.MiddleClick),
            PanGesture2 = new MouseGesture(MouseAction.RightClick)
        };
        return viewport;
    }

    private void Rebuild(WireModel? model)
    {
        _viewport.Children.Clear();
        _viewport.Children.Add(new DefaultLights());
        _viewport.Children.Add(new SunLight());

        if (model == null || (model.Edges.Count == 0 && model.Circles.Count == 0 && model.Vertices.Count == 0))
        {
            _viewport.Title = "三维模型查看器";
            _viewport.SubTitle = "打开 CATPart / CATProduct 后将在此以三维方式显示（与 AutoCAD / CATIA 相同的旋转、平移、缩放）";
            _viewport.Children.Add(CreateGrid(new Vec3(0, 0, 0), 200));
            FitView(new Vec3(0, 0, 0), 200);
            return;
        }

        var bbox = model.Bounds;
        if (model.Circles.Count > 0)
        {
            var pts = model.Vertices
                .Concat(model.Circles.SelectMany(c => Vec3.Tessellate(c, 16)))
                .ToList();
            bbox = BBox3.FromPoints(pts);
        }

        var center = new Vec3(
            (bbox.MinX + bbox.MaxX) / 2,
            (bbox.MinY + bbox.MaxY) / 2,
            (bbox.MinZ + bbox.MaxZ) / 2);
        double span = Math.Max(bbox.SizeX, Math.Max(bbox.SizeY, bbox.SizeZ));
        if (span < 1)
            span = 1;

        _viewport.Title = $"三维模型  {model.Name}";
        _viewport.SubTitle = $"顶点 {model.Vertices.Count}  边 {model.Edges.Count}  圆 {model.Circles.Count}    左键旋转  中键/右键平移  滚轮缩放";

        _viewport.Children.Add(CreateGrid(center with { Y = bbox.MinY }, span));

        var axes = new CoordinateSystemVisual3D
        {
            ArrowLengths = span * 0.12
        };
        axes.Transform = new TranslateTransform3D(bbox.MinX, bbox.MinY, bbox.MinZ);
        _viewport.Children.Add(axes);

        if (model.MeshVertices.Count >= 3 && model.MeshIndices.Count >= 3)
            _viewport.Children.Add(CreateSolid(model));

        if (model.Edges.Count > 0)
        {
            var edgePoints = new Point3DCollection(model.Edges.Count * 2);
            foreach (var edge in model.Edges)
            {
                edgePoints.Add(ToPoint(edge.A));
                edgePoints.Add(ToPoint(edge.B));
            }
            _viewport.Children.Add(new LinesVisual3D
            {
                Points = edgePoints,
                Color = WpfColor.FromRgb(40, 28, 55),
                Thickness = 1.15
            });
        }

        if (model.Circles.Count > 0 || model.Ellipses.Count > 0)
        {
            var circlePoints = new Point3DCollection();
            foreach (var circle in model.Circles)
                AddLoop(circlePoints, Vec3.Tessellate(circle, 96));
            foreach (var ellipse in model.Ellipses)
                AddLoop(circlePoints, Ellipse3.Tessellate(ellipse, 96));
            _viewport.Children.Add(new LinesVisual3D
            {
                Points = circlePoints,
                Color = WpfColor.FromRgb(50, 40, 80),
                Thickness = 1.6
            });
        }
        else if (model.Edges.Count == 0)
        {
            var dots = new Point3DCollection(model.Vertices.Select(ToPoint));
            _viewport.Children.Add(new PointsVisual3D
            {
                Points = dots,
                Color = WpfColor.FromRgb(255, 180, 80),
                Size = 3
            });
        }

        FitView(center, span);
    }

    private static ModelVisual3D CreateSolid(WireModel model)
    {
        var mesh = new MeshGeometry3D();
        foreach (var p in model.MeshVertices)
            mesh.Positions.Add(ToPoint(p));
        foreach (int i in model.MeshIndices)
            mesh.TriangleIndices.Add(i);

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(WpfColor.FromRgb(0xC8, 0xB4, 0xD8))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(WpfColor.FromArgb(160, 255, 255, 255)), 40));
        var back = new DiffuseMaterial(new SolidColorBrush(WpfColor.FromRgb(0xA8, 0x90, 0xC0)));
        return new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material) { BackMaterial = back }
        };
    }

    private static void AddLoop(Point3DCollection dest, IEnumerable<Vec3> loop)
    {
        var pts = loop.ToList();
        for (int i = 0; i < pts.Count; i++)
        {
            dest.Add(ToPoint(pts[i]));
            dest.Add(ToPoint(pts[(i + 1) % pts.Count]));
        }
    }

    private void FitView(Vec3 center, double span)
    {
        _viewport.Dispatcher.InvokeAsync(() =>
        {
            if (_viewport.ActualWidth <= 1 || _viewport.ActualHeight <= 1)
                return;
            // Face-on, slightly isometric — same attitude as a CATIA shaded plate.
            double d = span * 1.6;
            if (_viewport.Camera is ProjectionCamera cam)
            {
                cam.Position = new Point3D(center.X + d * 0.18, center.Y - d * 0.55, center.Z + d * 0.85);
                cam.LookDirection = new Vector3D(-0.18, 0.55, -0.85);
                cam.UpDirection = new Vector3D(0, 1, 0);
            }
            _viewport.ZoomExtents(0);
        }, DispatcherPriority.Loaded);
    }

    private static GridLinesVisual3D CreateGrid(Vec3 center, double span)
    {
        double size = Math.Max(span * 1.8, 50);
        double major = NiceStep(span / 8);
        return new GridLinesVisual3D
        {
            Center = new Point3D(center.X, center.Y, center.Z),
            Normal = new Vector3D(0, 1, 0),
            LengthDirection = new Vector3D(1, 0, 0),
            Width = size,
            Length = size,
            MajorDistance = major,
            MinorDistance = major / 5,
            Thickness = Math.Max(span * 0.0015, 0.4),
            Fill = new SolidColorBrush(WpfColor.FromArgb(180, 90, 90, 90))
        };
    }

    private static double NiceStep(double value)
    {
        if (value <= 0)
            return 10;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double n = value / exp;
        if (n < 2) return 2 * exp;
        if (n < 5) return 5 * exp;
        return 10 * exp;
    }

    private static Point3D ToPoint(Vec3 p) => new(p.X, p.Y, p.Z);
}
