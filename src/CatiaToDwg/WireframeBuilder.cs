namespace CatiaToDwg;

public static class WireframeBuilder
{
    private const double Tol = 0.05;
    private const double ChamferMax = 80.0;

    public static List<Edge3> BuildEdges(IReadOnlyList<Vec3> vertices)
    {
        var edges = new List<Edge3>();
        int n = vertices.Count;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                var a = vertices[i];
                var b = vertices[j];
                if (!IsCandidate(a, b, out _))
                    continue;
                if (HasPointBetween(a, b, vertices, i, j))
                    continue;
                edges.Add(new Edge3(a, b));
            }
        }
        return edges;
    }

    public static List<Edge3> WithoutCircleChords(IEnumerable<Edge3> edges, IReadOnlyList<Circle3> circles)
    {
        if (circles.Count == 0)
            return edges.ToList();

        var kept = new List<Edge3>();
        foreach (var edge in edges)
        {
            if (IsCircleChord(edge, circles))
                continue;
            kept.Add(edge);
        }
        return kept;
    }

    private static bool IsCircleChord(Edge3 edge, IReadOnlyList<Circle3> circles)
    {
        double len = (edge.A - edge.B).Length;
        if (len < 2)
            return false;

        var onA = new List<int>();
        var onB = new List<int>();
        for (int i = 0; i < circles.Count; i++)
        {
            if (PointOnCircle(edge.A, circles[i])) onA.Add(i);
            if (PointOnCircle(edge.B, circles[i])) onB.Add(i);
        }
        if (onA.Count == 0 || onB.Count == 0)
            return false;

        foreach (int i in onA)
        {
            if (onB.Contains(i))
                return true;
        }

        foreach (int i in onA)
        {
            foreach (int j in onB)
            {
                if (i == j) continue;
                var c1 = circles[i];
                var c2 = circles[j];
                double dc = (c1.Center - c2.Center).Length;
                if (dc < c1.Radius + c2.Radius - 1 && len < Math.Max(c1.Radius, c2.Radius) * 2.5)
                    return true;
            }
        }
        return false;
    }

    private static bool PointOnCircle(Vec3 point, Circle3 circle)
    {
        var axis = circle.Axis.Normalized();
        var d = point - circle.Center;
        double axial = Vec3.Dot(d, axis);
        if (Math.Abs(axial) > 5)
            return false;
        var radial = d - axis * axial;
        return Math.Abs(radial.Length - circle.Radius) <= 1.5;
    }

    private static bool IsCandidate(Vec3 a, Vec3 b, out int changingAxes)
    {
        double dx = Math.Abs(a.X - b.X);
        double dy = Math.Abs(a.Y - b.Y);
        double dz = Math.Abs(a.Z - b.Z);
        changingAxes = (dx > Tol ? 1 : 0) + (dy > Tol ? 1 : 0) + (dz > Tol ? 1 : 0);

        // Rectangle-based 2D: axis-aligned edges only.
        // Do not emit circular/arc chords. Keep only short 45° corner clips
        // that are true linear chamfers (equal deltas), never a circle.
        if (changingAxes == 1)
            return true;
        if (changingAxes != 2)
            return false;

        var deltas = new List<double>(2);
        if (dx > Tol) deltas.Add(dx);
        if (dy > Tol) deltas.Add(dy);
        if (dz > Tol) deltas.Add(dz);
        if (Math.Abs(deltas[0] - deltas[1]) > Tol)
            return false;
        double len = (a - b).Length;
        return len <= ChamferMax && len >= 1.0;
    }

    private static bool HasPointBetween(Vec3 a, Vec3 b, IReadOnlyList<Vec3> pts, int i, int j)
    {
        var ab = b - a;
        double lab = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
        if (lab < 1e-12)
            return false;

        for (int k = 0; k < pts.Count; k++)
        {
            if (k == i || k == j)
                continue;
            var c = pts[k];
            var ac = c - a;
            double t = (ac.X * ab.X + ac.Y * ab.Y + ac.Z * ab.Z) / lab;
            if (t <= 1e-6 || t >= 1 - 1e-6)
                continue;
            var p = new Vec3(a.X + t * ab.X, a.Y + t * ab.Y, a.Z + t * ab.Z);
            var d = c - p;
            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z < Tol * Tol)
                return true;
        }
        return false;
    }
}
