using System.Drawing;

namespace DlcAutoExtractor;

public sealed class MainForm : Form
{
    private readonly TextBox sourceBox = new();
    private readonly TextBox outputBox = new();
    private readonly TextBox gameBox = new();
    private readonly CheckBox allFilesBox = new() { Text = "Extract ALL files (not only map assets)" };
    private readonly CheckBox nestedBox = new() { Text = "Extract nested .rpf archives", Checked = true };
    private readonly CheckBox overwriteBox = new() { Text = "Overwrite existing output files (normally OFF)" };
    private readonly CheckBox gen9Box = new() { Text = "GTA V Enhanced / Gen9 (key loading)" };
    private readonly CheckBox exactResourcesBox = new() { Text = "Copy original RSC7/RSC8 bytes (recommended)", Checked = true };
    private readonly CheckBox verifyBox = new() { Text = "Verify each saved file (SHA256)", Checked = true };
    private readonly Button startButton = new() { Text = "START EXTRACTION" };
    private readonly Button cancelButton = new() { Text = "CANCEL", Enabled = false };
    private readonly ProgressBar progressBar = new();
    private readonly Label statusLabel = new() { Text = "Ready. Select folders to begin." };
    private readonly Label countersLabel = new() { Text = "Archives: 0    Extracted: 0    Skipped: 0    Errors: 0" };
    private readonly TextBox outputLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        BackColor = Color.FromArgb(19, 25, 35),
        ForeColor = Color.FromArgb(208, 221, 239),
        Font = new Font("Consolas", 9f),
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill
    };
    private CancellationTokenSource? cts;
    private readonly Color bg = Color.FromArgb(14, 20, 31);
    private readonly Color panel = Color.FromArgb(26, 35, 49);
    private readonly Color foreground = Color.FromArgb(226, 235, 245);
    private readonly Color muted = Color.FromArgb(157, 174, 196);
    private readonly Color accent = Color.FromArgb(83, 151, 253);
    private int logLines;

    public MainForm()
    {
        Text = "GTA V DLC Auto Extractor v0.2  |  CodeWalker.Core";
        MinimumSize = new Size(830, 730);
        Size = new Size(1030, 855);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = bg;
        ForeColor = foreground;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(20), BackColor = bg
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 203));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(main);

        main.Controls.Add(CreateHeader(), 0, 0);
        main.Controls.Add(CreateFoldersPanel(), 0, 1);
        main.Controls.Add(CreateOptionsPanel(), 0, 2);
        main.Controls.Add(CreateStatusPanel(), 0, 3);
        main.Controls.Add(CreateLogPanel(), 0, 4);
        startButton.Click += StartButton_Click;
        cancelButton.Click += (_, _) => { cancelButton.Enabled = false; cts?.Cancel(); AppendLog("Cancellation requested..."); };
    }

    private Control CreateHeader()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(new Label
        {
            Text = "GTA V DLC AUTO EXTRACTOR", Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 20, FontStyle.Bold), ForeColor = foreground, Height = 44
        });
        root.Controls.Add(new Label
        {
            Text = "Recursive RPF extraction  •  original resource bytes  •  SHA256 verification",
            Dock = DockStyle.Bottom, Height = 30, ForeColor = muted
        });
        return root;
    }

    private Control CreateFoldersPanel()
    {
        var group = Group("SOURCE & OUTPUT");
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(12, 12, 12, 10)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        for (int i = 0; i < 3; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));

        AddFolderRow(table, 0, "DLC source folder", sourceBox, "Select folder containing one or more .rpf archives");
        AddFolderRow(table, 1, "Output folder", outputBox, "Choose a separate output folder");
        AddFolderRow(table, 2, "GTA V folder", gameBox, "Optional for OPEN archives; required for protected RPF files");
        group.Controls.Add(table);
        return group;
    }

    private void AddFolderRow(TableLayoutPanel table, int row, string label, TextBox box, string hint)
    {
        var l = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted };
        box.Dock = DockStyle.Fill;
        box.BackColor = Color.FromArgb(19, 25, 35);
        box.ForeColor = foreground;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin = new Padding(2, 7, 9, 11);
        box.PlaceholderText = hint;
        var browse = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 9) };
        StyleButton(browse, panel);
        browse.Click += (_, _) => BrowseFolder(box);
        table.Controls.Add(l, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    private static void BrowseFolder(TextBox box)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(box.Text)) dlg.SelectedPath = box.Text;
        if (dlg.ShowDialog() == DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    private Control CreateOptionsPanel()
    {
        var group = Group("EXTRACTION OPTIONS");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(18, 18, 12, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int r = 0; r < 3; r++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
        var items = new[] { nestedBox, allFilesBox, overwriteBox, gen9Box, exactResourcesBox, verifyBox };
        for (int i = 0; i < items.Length; i++)
        {
            items[i].ForeColor = foreground;
            items[i].AutoSize = true;
            layout.Controls.Add(items[i], i % 2, i / 2);
        }
        group.Controls.Add(layout);
        return group;
    }

    private Control CreateStatusPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
            Padding = new Padding(0, 13, 0, 3)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.AutoEllipsis = true;
        statusLabel.ForeColor = foreground;
        countersLabel.Dock = DockStyle.Fill;
        countersLabel.ForeColor = muted;
        progressBar.Dock = DockStyle.Fill;
        progressBar.Maximum = 100;
        progressBar.Margin = new Padding(2, 6, 10, 8);
        StyleButton(startButton, accent);
        StyleButton(cancelButton, Color.FromArgb(151, 69, 69));
        startButton.Dock = DockStyle.Fill;
        cancelButton.Dock = DockStyle.Fill;
        startButton.Margin = new Padding(10, 0, 8, 3);
        cancelButton.Margin = new Padding(7, 0, 0, 3);
        root.Controls.Add(statusLabel, 0, 0);
        root.Controls.Add(startButton, 1, 0);
        root.Controls.Add(cancelButton, 2, 0);
        root.Controls.Add(progressBar, 0, 1);
        root.SetColumnSpan(progressBar, 3);
        root.Controls.Add(countersLabel, 0, 2);
        root.SetColumnSpan(countersLabel, 3);
        return root;
    }

    private Control CreateLogPanel()
    {
        var group = Group("EVENT LOG (errors and status)");
        var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        wrap.Controls.Add(outputLog);
        group.Controls.Add(wrap);
        return group;
    }

    private Panel Group(string title)
    {
        // Reserve the top 31px for the heading so Dock=Fill children do not cover it.
        var outer = new Panel { Dock = DockStyle.Fill, BackColor = panel, Padding = new Padding(0, 31, 0, 0) };
        outer.Controls.Add(new Label
        {
            Text = title, Location = new Point(12, 7), Size = new Size(600, 22),
            Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = accent
        });
        return outer;
    }

    private void StyleButton(Button button, Color color)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = foreground;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (cts != null) return;
        ExtractionOptions options;
        try
        {
            options = new ExtractionOptions(
                sourceBox.Text.Trim(), outputBox.Text.Trim(),
                string.IsNullOrWhiteSpace(gameBox.Text) ? null : gameBox.Text.Trim(),
                allFilesBox.Checked, nestedBox.Checked, overwriteBox.Checked, gen9Box.Checked,
                exactResourcesBox.Checked, verifyBox.Checked);
            if (!Directory.Exists(options.SourceFolder)) throw new ArgumentException("Select an existing source folder.");
            if (string.IsNullOrWhiteSpace(options.OutputFolder)) throw new ArgumentException("Select an output folder.");
            if (options.GameFolder is not null && !Directory.Exists(options.GameFolder))
                throw new ArgumentException("The optional GTA V folder does not exist.");
            ExtractionPaths.ValidateDisjointFolders(options.SourceFolder, options.OutputFolder);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid options", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        cts = new CancellationTokenSource();
        startButton.Enabled = false;
        cancelButton.Enabled = true;
        sourceBox.Enabled = outputBox.Enabled = gameBox.Enabled = false;
        allFilesBox.Enabled = nestedBox.Enabled = overwriteBox.Enabled = gen9Box.Enabled = exactResourcesBox.Enabled = verifyBox.Enabled = false;
        logLines = 0;
        outputLog.Clear();
        progressBar.Value = 0;
        AppendLog("Starting extraction...");
        var progress = new Progress<ExtractorProgress>(OnProgress);
        try
        {
            var result = await Task.Run(() => ExtractionEngine.Run(options, progress, cts.Token));
            AppendLog($"{(result.Cancelled ? "CANCELLED" : "DONE")} | archives={result.ScannedArchives} | extracted={result.Extracted} | skipped={result.Skipped} | errors={result.Failed}");
            AppendLog("Log: " + result.LogFile);
            AppendLog("Manifest: " + result.ManifestFile);
            AppendLog($"Exact resources: {result.ExactCopied:N0}; CodeWalker fallbacks: {result.Fallbacks:N0}");
            MessageBox.Show($"Archives: {result.ScannedArchives}\nExtracted: {result.Extracted}\nSkipped: {result.Skipped}\nErrors: {result.Failed}\nExact copies: {result.ExactCopied}\nFallbacks: {result.Fallbacks}\n\nLog:\n{result.LogFile}",
                result.Cancelled ? "Extraction cancelled" : "Extraction finished", MessageBoxButtons.OK,
                result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            AppendLog("FATAL ERROR: " + ex);
            MessageBox.Show(ex.Message, "Extraction failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            startButton.Enabled = true;
            cancelButton.Enabled = false;
            sourceBox.Enabled = outputBox.Enabled = gameBox.Enabled = true;
            allFilesBox.Enabled = nestedBox.Enabled = overwriteBox.Enabled = gen9Box.Enabled = exactResourcesBox.Enabled = verifyBox.Enabled = true;
        }
    }

    private void OnProgress(ExtractorProgress p)
    {
        statusLabel.Text = p.Status;
        countersLabel.Text = $"Roots: {p.RootDone}/{p.RootTotal}    RPF archives: {p.ScannedArchives:N0}    Extracted: {p.Extracted:N0}    Skipped: {p.Skipped:N0}    Errors: {p.Failed:N0}    Exact: {p.ExactCopied:N0}    Fallbacks: {p.Fallbacks:N0}";
        if (p.RootTotal > 0)
            progressBar.Value = Math.Clamp((int)Math.Round(100.0 * p.RootDone / p.RootTotal), 0, 100);
        if (p.Detail != null && (p.Status is "ERROR" or "WARNING")) AppendLog(p.Status + " " + p.Detail);
    }

    private void AppendLog(string message)
    {
        if (++logLines > 400)
        {
            outputLog.Clear();
            logLines = 1;
            outputLog.AppendText("[Showing only latest 400 events; see full log on disk]" + Environment.NewLine);
        }
        outputLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
