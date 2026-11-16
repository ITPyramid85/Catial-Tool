namespace CatiaToDwg;

public sealed class MainForm : Form
{
    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly TextBox _logBox;
    private readonly Button _openButton;
    private readonly Button _convertButton;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly CheckBox _pdfCheck;
    private readonly Label _statusLabel;
    private readonly Label _progressLabel;
    private readonly Label _navLabel;
    private readonly ProgressBar _progressBar;
    private readonly Model3DView _view3d;
    private readonly Drawing2DView _view2d;
    private readonly SplitContainer _viewerSplit;
    private readonly List<ConvertResult> _results = [];
    private int _resultIndex = -1;
    private bool _busy;

    public MainForm()
    {
        Text = "CATIA 转 2D DWG 转换器  —  三维 / 二维查看器";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        AllowDrop = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _inputBox = new TextBox { Dock = DockStyle.Fill };
        _outputBox = new TextBox { Dock = DockStyle.Fill };
        root.Controls.Add(MakePathRow("三维输入（CATPart / CATProduct / 文件夹）", _inputBox, BrowseInput));
        root.Controls.Add(MakePathRow("二维输出文件夹", _outputBox, BrowseOutput));

        _viewerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BorderStyle = BorderStyle.FixedSingle
        };
        _view3d = new Model3DView { Dock = DockStyle.Fill };
        _view2d = new Drawing2DView { Dock = DockStyle.Fill };
        _viewerSplit.Panel1.Controls.Add(_view3d);
        _viewerSplit.Panel2.Controls.Add(_view2d);
        root.Controls.Add(_viewerSplit);
        Shown += (_, _) => LayoutViewers();
        Resize += (_, _) => LayoutViewers();

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            Font = new Font("Consolas", 9f),
            BackColor = Color.White
        };
        root.Controls.Add(_logBox);

        var navRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        navRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _prevButton = new Button
        {
            Text = "上一张",
            AutoSize = true,
            Enabled = false,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(0, 0, 8, 0)
        };
        _prevButton.Click += (_, _) => ShowResult(_resultIndex - 1);
        _nextButton = new Button
        {
            Text = "下一张",
            AutoSize = true,
            Enabled = false,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(0, 0, 12, 0)
        };
        _nextButton.Click += (_, _) => ShowResult(_resultIndex + 1);
        _navLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 6, 0, 0),
            Text = "图纸 0 / 0"
        };
        navRow.Controls.Add(_prevButton, 0, 0);
        navRow.Controls.Add(_nextButton, 1, 0);
        navRow.Controls.Add(_navLabel, 2, 0);
        root.Controls.Add(navRow);

        var progressRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        _progressLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 4, 0, 0),
            Text = "0 / 0"
        };
        progressRow.Controls.Add(_progressBar, 0, 0);
        progressRow.Controls.Add(_progressLabel, 1, 0);
        root.Controls.Add(progressRow);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0)
        };
        _openButton = new Button
        {
            Text = "打开三维模型",
            AutoSize = true,
            Padding = new Padding(16, 6, 16, 6)
        };
        _openButton.Click += (_, _) => Open3D();
        _convertButton = new Button
        {
            Text = "全部转换为二维图纸",
            AutoSize = true,
            Padding = new Padding(18, 6, 18, 6)
        };
        _convertButton.Click += async (_, _) => await ConvertNowAsync();
        _pdfCheck = new CheckBox
        {
            Text = "生成 PDF 文档",
            AutoSize = true,
            Checked = false,
            Padding = new Padding(12, 8, 8, 0)
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 8, 0, 0),
            Text = "就绪 — 左侧：三维查看器 / 右侧：二维图纸查看器"
        };
        bottom.Controls.Add(_openButton);
        bottom.Controls.Add(_convertButton);
        bottom.Controls.Add(_pdfCheck);
        bottom.Controls.Add(_statusLabel);
        root.Controls.Add(bottom);

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += OnDrop;

        string workspace = FindWorkspace();
        _inputBox.Text = Directory.Exists(Path.Combine(workspace, "B14"))
            ? Path.Combine(workspace, "B14")
            : workspace;
        _outputBox.Text = Path.Combine(workspace, "output");
    }

    private void LayoutViewers()
    {
        if (_viewerSplit.Width > 80)
            _viewerSplit.SplitterDistance = Math.Max(280, _viewerSplit.Width / 2);
    }

    private Control MakePathRow(string caption, TextBox box, EventHandler browse)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0, 0, 0, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.SetColumnSpan(panel.Controls[0], 3);
        panel.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 1);
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(box, 1, 1);
        var button = new Button { Text = "浏览...", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        button.Click += browse;
        panel.Controls.Add(button, 2, 1);
        return panel;
    }

    private void BrowseInput(object? sender, EventArgs e)
    {
        using var fileDialog = new OpenFileDialog
        {
            Title = "打开 CATIA 三维文件",
            Filter = "CATIA 文件 (*.CATPart;*.CATProduct)|*.CATPart;*.CATProduct|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_inputBox.Text)
                ? _inputBox.Text
                : (File.Exists(_inputBox.Text) ? Path.GetDirectoryName(_inputBox.Text) : FindWorkspace())
        };
        if (fileDialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputBox.Text = fileDialog.FileName;
            Open3D();
            return;
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "或选择包含 CATIA 文件的文件夹",
            SelectedPath = Directory.Exists(_inputBox.Text) ? _inputBox.Text : FindWorkspace()
        };
        if (folderDialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputBox.Text = folderDialog.SelectedPath;
            Open3D();
        }
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 DWG 输出文件夹",
            SelectedPath = Directory.Exists(_outputBox.Text) ? _outputBox.Text : FindWorkspace()
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.SelectedPath;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_busy)
            return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        _inputBox.Text = files[0];
        Open3D();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _openButton.Enabled = !busy;
        _convertButton.Enabled = !busy;
        _pdfCheck.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateNavButtons();
    }

    private void ApplyProgress(ConvertProgress p)
    {
        int total = Math.Max(p.Total, 1);
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Maximum = total;
        _progressBar.Value = Math.Clamp(p.Current, 0, total);
        _progressLabel.Text = $"{p.Current} / {p.Total}";
        _statusLabel.Text = p.Message;
    }

    private void Open3D()
    {
        if (_busy)
            return;

        _logBox.Clear();
        SetBusy(true);
        _statusLabel.Text = "正在加载三维模型…";
        try
        {
            var model = Converter.LoadModel(_inputBox.Text.Trim(), Log);
            _view3d.SetModel(model);
            _statusLabel.Text = $"三维查看器：{model.Name}  （{model.Vertices.Count} 顶点，{model.Edges.Count} 边，{model.Circles.Count} 圆）";
            Log("已打开三维模型 → 三维查看器");
            Log($"  {model.Name}");
            Log($"  顶点 {model.Vertices.Count}  边 {model.Edges.Count}  圆 {model.Circles.Count}");
        }
        catch (Exception ex)
        {
            _view3d.SetModel(null);
            _statusLabel.Text = "打开三维模型失败";
            Log("错误：" + ex.Message);
            MessageBox.Show(this, ex.Message, "打开三维模型失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConvertNowAsync()
    {
        if (_busy)
            return;

        SetBusy(true);
        _progressBar.Value = 0;
        _progressLabel.Text = "0 / 0";
        _statusLabel.Text = "正在将全部三维文件转换为二维图纸…";

        var progress = new Progress<ConvertProgress>(ApplyProgress);
        string input = _inputBox.Text.Trim();
        string output = _outputBox.Text.Trim();
        bool generatePdf = _pdfCheck.Checked;

        try
        {
            var results = await Task.Run(() =>
                Converter.ConvertPath(input, output, LogSafe, progress, generatePdf));
            _results.Clear();
            _results.AddRange(results);
            int show = _results.FindIndex(r =>
                r.Source.EndsWith(".CATProduct", StringComparison.OrdinalIgnoreCase));
            if (show < 0)
                show = _results
                    .Select((r, i) => (r, i))
                    .OrderByDescending(t => t.r.Vertices)
                    .Select(t => t.i)
                    .FirstOrDefault();
            ShowResult(show);
            int pdfCount = _results.Count(r => !string.IsNullOrEmpty(r.PdfPath));
            string done = pdfCount > 0
                ? $"完成：已保存 {results.Count} 张图纸（含 {pdfCount} 个 PDF）"
                : $"完成：已保存 {results.Count} 张图纸";
            ApplyProgress(new ConvertProgress(results.Count, results.Count, done));
            Log("");
            Log($"当前图纸：{_results[_resultIndex].Sheet?.Title}");
            Log($"已保存 {results.Count} 张图纸 → {output}");
            if (pdfCount > 0)
                Log($"已同时生成 {pdfCount} 个 PDF 文档");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "转换失败";
            Log("错误：" + ex.Message);
            MessageBox.Show(this, ex.Message, "转换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowResult(int index)
    {
        if (_results.Count == 0)
        {
            _resultIndex = -1;
            UpdateNavButtons();
            return;
        }

        _resultIndex = Math.Clamp(index, 0, _results.Count - 1);
        var shown = _results[_resultIndex];
        if (shown.Model != null)
            _view3d.SetModel(shown.Model);
        _view2d.SetSheet(shown.Sheet);
        UpdateNavButtons();
        _statusLabel.Text = $"图纸 {_resultIndex + 1} / {_results.Count}  {shown.Sheet?.Title}  {shown.Sheet?.ScaleLabel}";
    }

    private void UpdateNavButtons()
    {
        _prevButton.Enabled = !_busy && _resultIndex > 0;
        _nextButton.Enabled = !_busy && _resultIndex >= 0 && _resultIndex < _results.Count - 1;
        _navLabel.Text = _results.Count == 0
            ? "图纸 0 / 0"
            : $"图纸 {_resultIndex + 1} / {_results.Count}  {_results[_resultIndex].Sheet?.Title}";
    }

    private void LogSafe(string message)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(() => Log(message));
        else
            Log(message);
    }

    private void Log(string message)
    {
        _logBox.AppendText(message + Environment.NewLine);
    }

    private static string FindWorkspace()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "B14")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
