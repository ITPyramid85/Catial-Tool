namespace CatiaToDwg;

/// <summary>
/// Rotates a part so the largest face lies in XY with the longest side
/// horizontal — the same way CATIA shows a plate.
/// </summary>
public static class ModelAligner
{
    public static void AlignFaceUp(WireModel model)
    {
        var b = model.Bounds;
        var axes = new[] { (0, b.SizeX), (1, b.SizeY), (2, b.SizeZ) };
        var ordered = axes.OrderByDescending(a => a.Item2).ToArray();
        int sx = ordered[0].Item1;
        int sy = ordered[1].Item1;
        int sz = ordered[2].Item1;
        if (sx == 0 && sy == 1 && sz == 2)
            return;

        Vec3 Map(Vec3 p) => new(Comp(p, sx), Comp(p, sy), Comp(p, sz));

        for (int i = 0; i < model.Vertices.Count; i++)
            model.Vertices[i] = Map(model.Vertices[i]);
        for (int i = 0; i < model.Edges.Count; i++)
            model.Edges[i] = new Edge3(Map(model.Edges[i].A), Map(model.Edges[i].B));
        for (int i = 0; i < model.Circles.Count; i++)
        {
            var c = model.Circles[i];
            model.Circles[i] = c with { Center = Map(c.Center), Axis = Map(c.Axis) };
        }
        for (int i = 0; i < model.Ellipses.Count; i++)
        {
            var e = model.Ellipses[i];
            model.Ellipses[i] = e with { Center = Map(e.Center), Axis = Map(e.Axis), UDir = Map(e.UDir) };
        }
        for (int i = 0; i < model.MeshVertices.Count; i++)
            model.MeshVertices[i] = Map(model.MeshVertices[i]);
    }

    private static double Comp(Vec3 p, int axis) => axis switch
    {
        0 => p.X,
        1 => p.Y,
        _ => p.Z
    };
}
