namespace CatiaToDwg;

public sealed class ConvertResult
{
    public string Source { get; init; } = "";
    public string DwgPath { get; init; } = "";
    public string DxfPath { get; init; } = "";
    public string PdfPath { get; init; } = "";
    public int Vertices { get; init; }
    public int Edges { get; init; }
    public WireModel? Model { get; init; }
    public DrawingSheet? Sheet { get; init; }
}

public readonly record struct ConvertProgress(int Current, int Total, string Message);

public static class Converter
{
    public static List<ConvertResult> ConvertPath(
        string inputPath,
        string outputDir,
        Action<string>? log = null,
        IProgress<ConvertProgress>? progress = null,
        bool generatePdf = false)
    {
        Directory.CreateDirectory(outputDir);
        var jobs = EnumerateJobs(inputPath);
        var results = new List<ConvertResult>(jobs.Count);

        progress?.Report(new ConvertProgress(0, jobs.Count, "正在准备转换…"));

        for (int i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            string name = Path.GetFileName(job.Path);
            progress?.Report(new ConvertProgress(i, jobs.Count, $"正在转换 {name}"));

            results.Add(job.IsProduct
                ? ConvertProduct(job.Path, outputDir, log, generatePdf)
                : ConvertPart(job.Path, outputDir, log, generatePdf));

            progress?.Report(new ConvertProgress(i + 1, jobs.Count, $"已完成 {name}"));
        }

        return results;
    }

    private static List<(string Path, bool IsProduct)> EnumerateJobs(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            string ext = Path.GetExtension(inputPath);
            if (ext.Equals(".CATProduct", StringComparison.OrdinalIgnoreCase))
                return [(inputPath, true)];
            if (ext.Equals(".CATPart", StringComparison.OrdinalIgnoreCase))
                return [(inputPath, false)];
            throw new InvalidDataException("请选择 CATPart、CATProduct 或包含 CATIA 文件的文件夹。");
        }

        if (!Directory.Exists(inputPath))
            throw new DirectoryNotFoundException(inputPath);

        var jobs = new List<(string Path, bool IsProduct)>();
        foreach (var part in Directory.GetFiles(inputPath, "*.CATPart").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            jobs.Add((part, false));
        foreach (var product in Directory.GetFiles(inputPath, "*.CATProduct").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            jobs.Add((product, true));

        if (jobs.Count == 0)
            throw new InvalidDataException("未找到 CATPart 或 CATProduct 文件。");

        return jobs;
    }

    private static ConvertResult ConvertPart(string path, string outputDir, Action<string>? log, bool generatePdf)
    {
        log?.Invoke($"正在读取零件  {Path.GetFileName(path)}");
        var model = CatiaParser.LoadPart(path);
        ModelAligner.AlignFaceUp(model);
        log?.Invoke($"  {model.Name}：{model.Vertices.Count} 顶点，{model.Edges.Count} 边，{model.Circles.Count} 圆，{model.CatiaVersion}");
        return ExportModel(model, outputDir, log, generatePdf);
    }

    private static ConvertResult ConvertProduct(string path, string outputDir, Action<string>? log, bool generatePdf)
    {
        log?.Invoke($"正在读取装配  {Path.GetFileName(path)}");
        string folder = Path.GetDirectoryName(path) ?? ".";
        var partFiles = Directory.GetFiles(folder, "*.CATPart");
        var loaded = new List<WireModel>();
        foreach (var partFile in partFiles)
        {
            try
            {
                loaded.Add(CatiaParser.LoadPart(partFile));
            }
            catch (Exception ex)
            {
                log?.Invoke($"  已跳过 {Path.GetFileName(partFile)}：{ex.Message}");
            }
        }

        if (loaded.Count == 0)
            throw new InvalidDataException($"无法加载装配 {Path.GetFileName(path)} 的子零件。");

        var childNames = CatiaParser.LoadProductChildNames(path);
        var matched = loaded
            .Where(m => childNames.Count == 0 || childNames.Any(c =>
                m.Name.Equals(c, StringComparison.OrdinalIgnoreCase) ||
                c.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase) ||
                m.Name.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matched.Count == 0)
            matched = loaded;

        log?.Invoke($"  装配使用 {matched.Count} 个零件：{string.Join("，", matched.Select(m => m.Name))}");
        var model = CatiaParser.Combine(Path.GetFileNameWithoutExtension(path), path, matched);
        ModelAligner.AlignFaceUp(model);
        return ExportModel(model, outputDir, log, generatePdf);
    }

    private static ConvertResult ExportModel(WireModel model, string outputDir, Action<string>? log, bool generatePdf)
    {
        var sheet = DrawingBuilder.Build(model);
        string stem = SanitizeFileName(model.Name);
        string dwg = Path.Combine(outputDir, stem + ".dwg");
        string dxf = Path.Combine(outputDir, stem + ".dxf");
        string pdf = Path.Combine(outputDir, stem + ".pdf");

        CadExport.WriteDxf(sheet, dxf);
        log?.Invoke($"  已写入 {Path.GetFileName(dxf)}");
        CadExport.WriteDwg(sheet, dwg);
        log?.Invoke($"  已写入 {Path.GetFileName(dwg)}  比例 {sheet.ScaleLabel}");

        if (generatePdf)
        {
            PdfExport.Write(sheet, pdf);
            log?.Invoke($"  已写入 {Path.GetFileName(pdf)}");
        }

        return new ConvertResult
        {
            Source = model.SourcePath,
            DwgPath = dwg,
            DxfPath = dxf,
            PdfPath = generatePdf ? pdf : "",
            Vertices = model.Vertices.Count,
            Edges = model.Edges.Count,
            Model = model,
            Sheet = sheet
        };
    }

    public static WireModel LoadModel(string inputPath, Action<string>? log = null)
    {
        if (File.Exists(inputPath))
        {
            string ext = Path.GetExtension(inputPath);
            if (ext.Equals(".CATProduct", StringComparison.OrdinalIgnoreCase))
                return LoadProductModel(inputPath, log);
            if (ext.Equals(".CATPart", StringComparison.OrdinalIgnoreCase))
            {
                var part = CatiaParser.LoadPart(inputPath);
                ModelAligner.AlignFaceUp(part);
                return part;
            }
            throw new InvalidDataException("请选择 CATPart、CATProduct 或包含 CATIA 文件的文件夹。");
        }

        if (!Directory.Exists(inputPath))
            throw new DirectoryNotFoundException(inputPath);

        var products = Directory.GetFiles(inputPath, "*.CATProduct");
        if (products.Length > 0)
            return LoadProductModel(products[0], log);

        var parts = Directory.GetFiles(inputPath, "*.CATPart");
        if (parts.Length == 0)
            throw new InvalidDataException("未找到 CATPart 或 CATProduct 文件。");

        var loaded = parts.Select(p => CatiaParser.LoadPart(p)).ToList();
        var folderModel = CatiaParser.Combine(Path.GetFileName(inputPath.TrimEnd('\\', '/')), inputPath, loaded);
        ModelAligner.AlignFaceUp(folderModel);
        return folderModel;
    }

    private static WireModel LoadProductModel(string path, Action<string>? log)
    {
        string folder = Path.GetDirectoryName(path) ?? ".";
        var loaded = new List<WireModel>();
        foreach (var partFile in Directory.GetFiles(folder, "*.CATPart"))
        {
            try
            {
                loaded.Add(CatiaParser.LoadPart(partFile));
            }
            catch (Exception ex)
            {
                log?.Invoke($"  已跳过 {Path.GetFileName(partFile)}：{ex.Message}");
            }
        }
        if (loaded.Count == 0)
            throw new InvalidDataException($"无法加载装配 {Path.GetFileName(path)} 的子零件。");
        var product = CatiaParser.Combine(Path.GetFileNameWithoutExtension(path), path, loaded);
        ModelAligner.AlignFaceUp(product);
        return product;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "drawing" : name;
    }
}
