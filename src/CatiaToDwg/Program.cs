using System.Runtime.InteropServices;
using System.Text;

namespace CatiaToDwg;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0)
        {
            if (!AttachConsole(AttachParentProcess))
                AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.OutputEncoding = Encoding.UTF8;
            Environment.ExitCode = RunCli(args);
            return;
        }

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
            };
        }

        Application.Run(new MainForm());
    }

    private static int RunCli(string[] args)
    {
        try
        {
            string input = args[0];
            string output = "output";
            bool generatePdf = false;
            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
                    output = args[++i];
                else if (args[i] == "--pdf")
                    generatePdf = true;
            }

            if (!Path.IsPathRooted(output))
                output = Path.GetFullPath(output);

            Console.WriteLine("CATIA → 二维 DWG 转换器");
            Console.WriteLine($"输入 : {input}");
            Console.WriteLine($"输出 : {output}");
            Console.WriteLine($"PDF  : {(generatePdf ? "是" : "否")}");
            var progress = new Progress<ConvertProgress>(p =>
                Console.WriteLine($"[{p.Current}/{p.Total}] {p.Message}"));
            var results = Converter.ConvertPath(input, output, Console.WriteLine, progress, generatePdf);
            Console.WriteLine($"完成。已写入 {results.Count} 张图纸。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
}
