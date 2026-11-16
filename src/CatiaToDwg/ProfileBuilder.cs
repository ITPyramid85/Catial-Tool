namespace CatiaToDwg;

/// <summary>
/// Rebuilds a thin CATIA plate as the real face profile: outer outline with
/// chamfers and edge notches, plus interior circular/oval holes — not a
/// 3D box grid of construction vertices.
/// </summary>
public static class ProfileBuilder
{
    private const double Snap = 0.05;
    private const double ChamferMax = 80.0;

    public static bool TryApply(WireModel model)
    {
        if (model.Vertices.Count < 6)
            return false;
        if (!TryThinAxis(model.Vertices, out int thin, out double z0, out double z1))
            return false;

        var pts = Unique(model.Vertices.Select(p => ToUv(p, thin)));
        if (pts.Count < 4)
            return false;

        var outer = BuildOuterLoop(pts);
        if (outer.Count < 4)
            return false;

        var holes = FindInteriorHoles(pts, outer);
        var edges = ExtrudeEdges(outer, holes, thin, z0, z1);
        if (edges.Count == 0)
            return false;

        model.Edges.Clear();
        model.Edges.AddRange(edges);

        model.Ellipses.Clear();
        foreach (var hole in holes)
        {
            var e = ToEllipse(hole, thin, (z0 + z1) * 0.5);
            if (e != null)
                model.Ellipses.Add(e.Value);
        }

        model.Circles.RemoveAll(c => IsFilletOrDuplicate(c, holes, thin, z0, z1));

        BuildMesh(model, outer, holes, thin, z0, z1);
        return true;
    }

    private static bool TryThinAxis(IReadOnlyList<Vec3> vertices, out int axis, out double a, out double b)
    {
        axis = -1;
        a = b = 0;
        var xs = UniqueValues(vertices.Select(p => p.X));
        var ys = UniqueValues(vertices.Select(p => p.Y));
        var zs = UniqueValues(vertices.Select(p => p.Z));
        var spans = new[] { xs[^1] - xs[0], ys[^1] - ys[0], zs[^1] - zs[0] };
        var counts = new[] { xs.Count, ys.Count, zs.Count };
        int best = 0;
        for (int i = 1; i < 3; i++)
        {
            if (counts[i] < counts[best] || (counts[i] == counts[best] && spans[i] < spans[best]))
                best = i;
        }

        bool twoLevel = counts[best] == 2;
        double mid = spans.OrderBy(s => s).ElementAt(1);
        bool thinSlab = mid > 1 && spans[best] <= Math.Max(mid * 0.2, 12);
        if (!twoLevel && !thinSlab)
            return false;

        axis = best;
        var vals = best == 0 ? xs : best == 1 ? ys : zs;
        a = vals[0];
        b = vals[^1];
        if (Math.Abs(b - a) < 1e-6)
            return false;
        return true;
    }

    private static Vec2 ToUv(Vec3 p, int thin) => thin switch
    {
        0 => new Vec2(p.Y, p.Z),
        1 => new Vec2(p.X, p.Z),
        _ => new Vec2(p.X, p.Y)
    };

    private static Vec3 FromUv(Vec2 uv, int thin, double w) => thin switch
    {
        0 => new Vec3(w, uv.X, uv.Y),
        1 => new Vec3(uv.X, w, uv.Y),
        _ => new Vec3(uv.X, uv.Y, w)
    };

    private static List<Vec2> Unique(IEnumerable<Vec2> pts)
    {
        var seen = new HashSet<(long, long)>();
        var list = new List<Vec2>();
        foreach (var p in pts)
        {
            var q = new Vec2(SnapTo(p.X), SnapTo(p.Y));
            if (seen.Add((BitConverter.DoubleToInt64Bits(q.X), BitConverter.DoubleToInt64Bits(q.Y))))
                list.Add(q);
        }
        return list;
    }

    private static List<double> UniqueValues(IEnumerable<double> values)
    {
        var list = values.Select(SnapTo).Distinct().OrderBy(v => v).ToList();
        var compact = new List<double>();
        foreach (var v in list)
        {
            if (compact.Count == 0 || v - compact[^1] > Snap)
                compact.Add(v);
            else
                compact[^1] = (compact[^1] + v) * 0.5;
        }
        return compact;
    }

    private static double SnapTo(double v) => Math.Round(Math.Round(v / Snap) * Snap, 4);

    private readonly record struct Notch(double Start, double End, double Depth);

    private static List<Vec2> BuildOuterLoop(IReadOnlyList<Vec2> pts)
    {
        double u0 = pts.Min(p => p.X), u1 = pts.Max(p => p.X);
        double v0 = pts.Min(p => p.Y), v1 = pts.Max(p => p.Y);

        var bl = ChamferAt(pts, u0, v0, +1, +1);
        var br = ChamferAt(pts, u1, v0, -1, +1);
        var tr = ChamferAt(pts, u1, v1, -1, -1);
        var tl = ChamferAt(pts, u0, v1, +1, -1);

        var bottom = NotchesOnEdge(pts, horizontal: true, at: v0, u0, u1);
        var top = NotchesOnEdge(pts, horizontal: true, at: v1, u0, u1);
        var left = NotchesOnEdge(pts, horizontal: false, at: u0, v0, v1);
        var right = NotchesOnEdge(pts, horizontal: false, at: u1, v0, v1);

        var loop = new List<Vec2>();
        loop.Add(bl.AlongU);
        EmitEdgeNotches(loop, bottom, v0, v0 + 1, horizontal: true, reverse: false);
        loop.Add(br.AlongU);
        loop.Add(br.AlongV);
        EmitEdgeNotches(loop, right, u1, u1 - 1, horizontal: false, reverse: false);
        loop.Add(tr.AlongV);
        loop.Add(tr.AlongU);
        EmitEdgeNotches(loop, top, v1, v1 - 1, horizontal: true, reverse: true);
        loop.Add(tl.AlongU);
        loop.Add(tl.AlongV);
        EmitEdgeNotches(loop, left, u0, u0 + 1, horizontal: false, reverse: true);
        loop.Add(bl.AlongV);
        return DedupeLoop(loop);
    }

    private readonly record struct Corner(Vec2 AlongU, Vec2 AlongV);

    private static Corner ChamferAt(IReadOnlyList<Vec2> pts, double cu, double cv, int du, int dv)
    {
        Vec2? onUEdge = null;
        Vec2? onVEdge = null;
        foreach (var p in pts)
        {
            double au = (p.X - cu) * du;
            double av = (p.Y - cv) * dv;
            if (Math.Abs(p.Y - cv) <= Snap * 2 && au >= 1 && au <= ChamferMax)
            {
                if (onUEdge == null || au < (onUEdge.Value.X - cu) * du)
                    onUEdge = p;
            }
            if (Math.Abs(p.X - cu) <= Snap * 2 && av >= 1 && av <= ChamferMax)
            {
                if (onVEdge == null || av < (onVEdge.Value.Y - cv) * dv)
                    onVEdge = p;
            }
        }

        if (onUEdge != null && onVEdge != null)
        {
            double su = (onUEdge.Value.X - cu) * du;
            double sv = (onVEdge.Value.Y - cv) * dv;
            if (Math.Abs(su - sv) <= Snap * 4)
                return new Corner(onUEdge.Value, onVEdge.Value);
        }

        var sharp = new Vec2(cu, cv);
        return new Corner(onUEdge ?? sharp, onVEdge ?? sharp);
    }

    private static List<Notch> NotchesOnEdge(
        IReadOnlyList<Vec2> pts,
        bool horizontal,
        double at,
        double span0,
        double span1)
    {
        var onEdge = pts
            .Where(p => Math.Abs((horizontal ? p.Y : p.X) - at) <= Snap * 2)
            .Select(p => horizontal ? p.X : p.Y)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
        if (onEdge.Count < 4)
            return [];

        var notches = new List<Notch>();
        for (int i = 1; i + 1 < onEdge.Count; i += 2)
        {
            double a = onEdge[i];
            double b = onEdge[i + 1];
            if (b - a < 1)
                continue;
            // Ignore the huge "gap" that is just the part interior between ends.
            if (b - a > (span1 - span0) * 0.45)
                continue;

            double depth = NotchDepth(pts, horizontal, at, a, b);
            if (depth < 1)
                continue;
            notches.Add(new Notch(a, b, depth));
        }
        return notches;
    }

    private static double NotchDepth(
        IReadOnlyList<Vec2> pts,
        bool horizontal,
        double at,
        double start,
        double end)
    {
        double best = 0;
        foreach (var p in pts)
        {
            double along = horizontal ? p.X : p.Y;
            if (along < start - Snap || along > end + Snap)
                continue;
            double across = horizontal ? p.Y : p.X;
            double d = Math.Abs(across - at);
            if (d > Snap && (best == 0 || d < best))
                best = d;
        }
        return best;
    }

    private static void EmitEdgeNotches(
        List<Vec2> loop,
        IReadOnlyList<Notch> notches,
        double edgeAt,
        double inwardHint,
        bool horizontal,
        bool reverse)
    {
        var ordered = reverse
            ? notches.Reverse().Select(n => new Notch(n.End, n.Start, n.Depth)).ToList()
            : notches.ToList();

        foreach (var n in ordered)
        {
            double first = n.Start;
            double second = n.End;
            double cut = inwardHint >= edgeAt ? edgeAt + Math.Abs(n.Depth) : edgeAt - Math.Abs(n.Depth);
            if (horizontal)
            {
                loop.Add(new Vec2(first, edgeAt));
                loop.Add(new Vec2(first, cut));
                loop.Add(new Vec2(second, cut));
                loop.Add(new Vec2(second, edgeAt));
            }
            else
            {
                loop.Add(new Vec2(edgeAt, first));
                loop.Add(new Vec2(cut, first));
                loop.Add(new Vec2(cut, second));
                loop.Add(new Vec2(edgeAt, second));
            }
        }
    }

    private static List<Vec2> DedupeLoop(List<Vec2> loop)
    {
        var clean = new List<Vec2>();
        foreach (var p in loop)
        {
            var q = new Vec2(SnapTo(p.X), SnapTo(p.Y));
            if (clean.Count == 0 || (clean[^1] - q).Length > Snap)
                clean.Add(q);
        }
        if (clean.Count > 1 && (clean[0] - clean[^1]).Length <= Snap)
            clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    private sealed record Hole(Vec2 Center, double Rx, double Ry, List<Vec2> Box);

    private static List<Hole> FindInteriorHoles(IReadOnlyList<Vec2> pts, IReadOnlyList<Vec2> outer)
    {
        var holes = new List<Hole>();
        double u0 = outer.Min(p => p.X), u1 = outer.Max(p => p.X);
        double v0 = outer.Min(p => p.Y), v1 = outer.Max(p => p.Y);
        double marginU = (u1 - u0) * 0.04;
        double marginV = (v1 - v0) * 0.04;

        var interior = pts.Where(p =>
            p.X > u0 + marginU && p.X < u1 - marginU &&
            p.Y > v0 + marginV && p.Y < v1 - marginV).ToList();

        var used = new HashSet<(long, long)>();
        foreach (var a in interior)
        {
            var keyA = Key(a);
            if (!used.Add(keyA))
                continue;

            var xs = interior.Where(p => Math.Abs(p.Y - a.Y) <= Snap * 2).Select(p => p.X).Distinct().OrderBy(v => v).ToList();
            var ys = interior.Where(p => Math.Abs(p.X - a.X) <= Snap * 2).Select(p => p.Y).Distinct().OrderBy(v => v).ToList();
            if (xs.Count < 2 || ys.Count < 2)
                continue;

            // Isolated 4-corner rectangle whose four corners all exist.
            foreach (var x2 in xs.Where(x => x > a.X + 1))
            {
                foreach (var y2 in ys.Where(y => y > a.Y + 1))
                {
                    var b = new Vec2(x2, a.Y);
                    var c = new Vec2(x2, y2);
                    var d = new Vec2(a.X, y2);
                    if (!Contains(interior, b) || !Contains(interior, c) || !Contains(interior, d))
                        continue;

                    double w = x2 - a.X;
                    double h = y2 - a.Y;
                    // Ignore giant rectangles that are plate bands, not holes.
                    if (w > (u1 - u0) * 0.55 || h > (v1 - v0) * 0.55)
                        continue;
                    if (w < 8 || h < 8)
                        continue;

                    used.Add(Key(b)); used.Add(Key(c)); used.Add(Key(d));
                    var box = new List<Vec2> { a, b, c, d };
                    holes.Add(new Hole(new Vec2((a.X + x2) / 2, (a.Y + y2) / 2), w / 2, h / 2, box));
                }
            }
        }

        double minClearU = Math.Max(40, (u1 - u0) * 0.04);
        double minClearV = Math.Max(40, (v1 - v0) * 0.04);
        return holes
            .Where(h =>
            {
                double clearU = Math.Min(h.Center.X - u0, u1 - h.Center.X) - h.Rx;
                double clearV = Math.Min(h.Center.Y - v0, v1 - h.Center.Y) - h.Ry;
                double aspect = Math.Max(h.Rx, h.Ry) / Math.Max(Math.Min(h.Rx, h.Ry), 1);
                return clearU >= minClearU && clearV >= minClearV && aspect <= 3.6;
            })
            .GroupBy(h => (Math.Round(h.Center.X, 1), Math.Round(h.Center.Y, 1)))
            .Select(g => g.OrderBy(h => h.Rx * h.Ry).First())
            .ToList();
    }

    private static bool Contains(List<Vec2> pts, Vec2 p) =>
        pts.Any(q => Math.Abs(q.X - p.X) <= Snap * 2 && Math.Abs(q.Y - p.Y) <= Snap * 2);

    private static (long, long) Key(Vec2 p) =>
        (BitConverter.DoubleToInt64Bits(SnapTo(p.X)), BitConverter.DoubleToInt64Bits(SnapTo(p.Y)));

    private static Ellipse3? ToEllipse(Hole hole, int thin, double w)
    {
        var center = FromUv(hole.Center, thin, w);
        var axis = thin switch
        {
            0 => new Vec3(1, 0, 0),
            1 => new Vec3(0, 1, 0),
            _ => new Vec3(0, 0, 1)
        };
        var uDir = thin switch
        {
            0 => new Vec3(0, 1, 0),
            1 => new Vec3(1, 0, 0),
            _ => new Vec3(1, 0, 0)
        };
        return new Ellipse3(center, axis, uDir, hole.Rx, hole.Ry);
    }

    private static bool IsFilletOrDuplicate(Circle3 circle, List<Hole> holes, int thin, double z0, double z1)
    {
        var uv = ToUv(circle.Center, thin);
        foreach (var hole in holes)
        {
            if ((hole.Center - uv).Length <= Math.Max(hole.Rx, hole.Ry) * 0.6)
                return true;
        }
        // Small radii near the part are notch fillets, not through-holes.
        if (circle.Radius < 40)
            return true;
        double w = thin switch { 0 => circle.Center.X, 1 => circle.Center.Y, _ => circle.Center.Z };
        if (w < Math.Min(z0, z1) - 2 || w > Math.Max(z0, z1) + 2)
            return true;
        return false;
    }

    private static List<Edge3> ExtrudeEdges(List<Vec2> outer, List<Hole> holes, int thin, double z0, double z1)
    {
        var edges = new List<Edge3>();
        void Ring(IReadOnlyList<Vec2> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                var a0 = FromUv(a, thin, z0);
                var b0 = FromUv(b, thin, z0);
                var a1 = FromUv(a, thin, z1);
                var b1 = FromUv(b, thin, z1);
                edges.Add(new Edge3(a0, b0));
                edges.Add(new Edge3(a1, b1));
                edges.Add(new Edge3(a0, a1));
            }
        }

        Ring(outer);
        foreach (var hole in holes)
        {
            var ring = Ellipse3.Tessellate(ToEllipse(hole, thin, z0)!.Value, 72)
                .Select(p => ToUv(p, thin))
                .ToList();
            Ring(ring);
        }
        return edges;
    }

    private static void BuildMesh(WireModel model, List<Vec2> outer, List<Hole> holes, int thin, double z0, double z1)
    {
        model.MeshVertices.Clear();
        model.MeshIndices.Clear();

        var holeLoops = holes
            .Select(h => Ellipse3.Tessellate(ToEllipse(h, thin, z0)!.Value, 48).Select(p => ToUv(p, thin)).ToList())
            .ToList();

        var tris = Triangulate(outer, holeLoops);
        foreach (var (a, b, c) in tris)
        {
            AddTri(model, FromUv(a, thin, z1), FromUv(b, thin, z1), FromUv(c, thin, z1), false);
            AddTri(model, FromUv(a, thin, z0), FromUv(c, thin, z0), FromUv(b, thin, z0), false);
        }

        void Wall(IReadOnlyList<Vec2> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                var q = loop[(i + 1) % loop.Count];
                var p0 = FromUv(p, thin, z0);
                var q0 = FromUv(q, thin, z0);
                var p1 = FromUv(p, thin, z1);
                var q1 = FromUv(q, thin, z1);
                AddTri(model, p0, q0, q1, false);
                AddTri(model, p0, q1, p1, false);
            }
        }

        Wall(outer);
        foreach (var hole in holeLoops)
            Wall(hole);
    }

    private static void AddTri(WireModel model, Vec3 a, Vec3 b, Vec3 c, bool unused)
    {
        int i = model.MeshVertices.Count;
        model.MeshVertices.Add(a);
        model.MeshVertices.Add(b);
        model.MeshVertices.Add(c);
        model.MeshIndices.Add(i);
        model.MeshIndices.Add(i + 1);
        model.MeshIndices.Add(i + 2);
    }

    private static List<(Vec2 A, Vec2 B, Vec2 C)> Triangulate(List<Vec2> outer, List<List<Vec2>> holes)
    {
        var poly = new List<Vec2>(EnsureCcw(outer));
        foreach (var hole in holes)
        {
            var h = EnsureCcw(hole);
            h.Reverse(); // CW hole
            Bridge(poly, h);
        }
        return EarClip(poly);
    }

    private static List<Vec2> EnsureCcw(IReadOnlyList<Vec2> loop)
    {
        var list = loop.ToList();
        if (SignedArea(list) < 0)
            list.Reverse();
        return list;
    }

    private static double SignedArea(IReadOnlyList<Vec2> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a * 0.5;
    }

    private static void Bridge(List<Vec2> outer, List<Vec2> hole)
    {
        if (hole.Count == 0)
            return;
        int oi = 0, hi = 0;
        double best = double.MaxValue;
        for (int i = 0; i < outer.Count; i++)
        {
            for (int j = 0; j < hole.Count; j++)
            {
                double d = (outer[i] - hole[j]).Length;
                if (d < best)
                {
                    best = d;
                    oi = i;
                    hi = j;
                }
            }
        }

        var injected = new List<Vec2> { outer[oi] };
        for (int k = 0; k <= hole.Count; k++)
            injected.Add(hole[(hi + k) % hole.Count]);
        injected.Add(outer[oi]);
        outer.InsertRange(oi + 1, injected);
    }

    private static List<(Vec2 A, Vec2 B, Vec2 C)> EarClip(List<Vec2> poly)
    {
        var tris = new List<(Vec2, Vec2, Vec2)>();
        var verts = Enumerable.Range(0, poly.Count).ToList();
        int guard = 0;
        while (verts.Count >= 3 && guard++ < 10000)
        {
            bool cut = false;
            for (int i = 0; i < verts.Count; i++)
            {
                int i0 = verts[(i + verts.Count - 1) % verts.Count];
                int i1 = verts[i];
                int i2 = verts[(i + 1) % verts.Count];
                var a = poly[i0];
                var b = poly[i1];
                var c = poly[i2];
                if (!IsEar(a, b, c, poly, verts))
                    continue;
                tris.Add((a, b, c));
                verts.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut)
                break;
        }
        return tris;
    }

    private static bool IsEar(Vec2 a, Vec2 b, Vec2 c, List<Vec2> poly, List<int> verts)
    {
        double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (cross <= 1e-8)
            return false;
        foreach (int i in verts)
        {
            var p = poly[i];
            if (Same(p, a) || Same(p, b) || Same(p, c))
                continue;
            if (PointInTri(p, a, b, c))
                return false;
        }
        return true;
    }

    private static bool Same(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) < 1e-8 && Math.Abs(a.Y - b.Y) < 1e-8;

    private static bool PointInTri(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
    {
        double s1 = Sign(p, a, b);
        double s2 = Sign(p, b, c);
        double s3 = Sign(p, c, a);
        bool neg = s1 < -1e-10 || s2 < -1e-10 || s3 < -1e-10;
        bool pos = s1 > 1e-10 || s2 > 1e-10 || s3 > 1e-10;
        return !(neg && pos);
    }

    private static double Sign(Vec2 p, Vec2 a, Vec2 b) =>
        (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
}
