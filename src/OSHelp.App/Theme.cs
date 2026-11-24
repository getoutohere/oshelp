using System.Runtime.InteropServices;
using System.Text.Json;
using OSHelp.Core;

namespace OSHelp.App;

internal static class Theme
{
    static Dictionary<string, Color> colors = [];
    static readonly Icon appIcon = LoadIcon();
    static Icon LoadIcon()
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("OSHelp.App.Icon")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }
    public static readonly Font Body = new("Segoe UI", 10.5f);
    public static readonly Font Heading = new("Courier New", 14, FontStyle.Bold);
    public static readonly Font LabelFont = new("Courier New", 10.5f, FontStyle.Bold);
    public static readonly Font Number = new("Consolas", 11);
    public static Color C(string key) => colors[key];
    public static void Load(string repository)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "design/obenseuer-dark.json")));
        colors = json.RootElement.GetProperty("colors").EnumerateObject().ToDictionary(x => x.Name, x => ColorTranslator.FromHtml(x.Value.GetString()!));
    }
    public static void Apply(Control control)
    {
        control.Font = Body; control.BackColor = C(control is TextBoxBase or ListBox ? "recess" : "panel"); control.ForeColor = C("text");
        if (control is Form form) { form.BackColor = C("window"); form.Icon = appIcon; }
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false; button.BackColor = C("button"); button.FlatAppearance.BorderColor = C("accent"); button.FlatAppearance.MouseOverBackColor = C("buttonHover"); button.FlatAppearance.MouseDownBackColor = C("selection"); button.Font = LabelFont; button.AutoSize = true; button.Padding = new(8, 4, 8, 4); }
        if (control is TextBoxBase text) { text.BorderStyle = BorderStyle.FixedSingle; }
        if (control is DataGridView grid) StyleGrid(grid);
        foreach (Control child in control.Controls) Apply(child);
    }
    public static Panel Panel(DockStyle dock = DockStyle.Fill, int padding = 12) => new() { Dock = dock, Padding = new(padding), BackColor = C("panel"), ForeColor = C("text") };
    public static Label Text(string value, bool heading = false) => new() { Text = value, AutoSize = true, ForeColor = C(heading ? "accent" : "text"), Font = heading ? Heading : Body, BackColor = Color.Transparent, Margin = new(4, 6, 10, 6) };
    public static Button Button(string value, EventHandler action)
    {
        var b = new Button { Text = value, AutoSize = true, MinimumSize = new(90, 34), Margin = new(4, 4, 8, 4) }; Apply(b); b.Click += action; return b;
    }
    public static FlowLayoutPanel Flow(DockStyle dock = DockStyle.Top, int height = 48) => new() { Dock = dock, Height = height, WrapContents = true, Padding = new(4), BackColor = C("panel"), ForeColor = C("text") };
    public static TextBox Input(string text = "", int width = 220) { var box = new TextBox { Text = text, Width = width, Margin = new(4, 8, 8, 4) }; Apply(box); return box; }
    public static DarkCombo Combo(int width = 220) => new() { Width = width, Margin = new(4, 6, 8, 4) };
    public static DataGridView Grid(params (string Key, string Title, int Weight)[] columns)
    {
        var grid = new DarkGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, ShowCellToolTips = false };
        foreach (var c in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = c.Key, HeaderText = c.Title, FillWeight = c.Weight, MinimumWidth = 48, SortMode = DataGridViewColumnSortMode.Automatic });
        StyleGrid(grid); return grid;
    }
    static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = C("recess"); g.GridColor = C("border"); g.Font = Body; g.EnableHeadersVisualStyles = false;
        g.DefaultCellStyle = new() { BackColor = C("recess"), ForeColor = C("text"), SelectionBackColor = C("selection"), SelectionForeColor = C("text"), Padding = new(6), NullValue = "" };
        g.AlternatingRowsDefaultCellStyle = new() { BackColor = C("panel"), ForeColor = C("text"), SelectionBackColor = C("selection"), SelectionForeColor = C("text") };
        g.ColumnHeadersDefaultCellStyle = new() { BackColor = C("navigation"), ForeColor = C("text"), Font = LabelFont, SelectionBackColor = C("navigation"), Padding = new(6) };
        g.RowHeadersDefaultCellStyle = g.ColumnHeadersDefaultCellStyle;
        g.RowTemplate.Height = 36; g.ColumnHeadersHeight = 38;
        g.EditingControlShowing += (_, e) => { if (e.Control != null) { Apply(e.Control); e.Control.BackColor = C("recess"); } };
        g.DataError += (_, e) => { e.ThrowException = false; };
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    public static void DarkCaption(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int enabled = 1; if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref enabled, 4);
    }
}

internal sealed class DarkCombo : Control
{
    readonly ContextMenuStrip popup = new() { ShowImageMargin = false, ShowCheckMargin = false, Renderer = new DarkMenuRenderer() };
    int index = -1;
    public List<object> Items { get; } = [];
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex
    {
        get => index;
        set { var next = value >= 0 && value < Items.Count ? value : -1; bool changed = index != next; index = next; Invalidate(); if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    public object? SelectedItem { get => index >= 0 && index < Items.Count ? Items[index] : null; set => SelectedIndex = value == null ? -1 : Items.IndexOf(value); }
    public DarkCombo() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true); Height = 32; BackColor = Theme.C("recess"); ForeColor = Theme.C("text"); Font = Theme.Body; TabStop = true; AccessibleRole = AccessibleRole.ComboBox; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); using var pen = new Pen(Theme.C(Focused ? "focus" : "inputBorder")); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, SelectedItem?.ToString() ?? "", Font, new Rectangle(7, 2, Math.Max(1, Width - 30), Height - 4), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        int x = Width - 16, y = Height / 2; using var arrow = new SolidBrush(ForeColor); e.Graphics.FillPolygon(arrow, new Point[] { new(x - 4, y - 2), new(x + 4, y - 2), new(x, y + 3) });
    }
    public void OpenPopup()
    {
        popup.Items.Clear(); popup.BackColor = Theme.C("recess"); popup.ForeColor = Theme.C("text"); popup.Font = Font;
        for (int i = 0; i < Items.Count; i++) { int selected = i; var item = new ToolStripMenuItem(Items[i].ToString()) { ForeColor = Theme.C("text"), BackColor = Theme.C("recess"), Checked = i == index }; item.Click += (_, _) => SelectedIndex = selected; popup.Items.Add(item); }
        popup.Show(this, new Point(0, Height));
    }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); OpenPopup(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Space) { OpenPopup(); e.Handled = true; } else if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(Items.Count - 1, index + 1); e.Handled = true; } else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, index - 1); e.Handled = true; } base.OnKeyDown(e); }
    protected override void Dispose(bool disposing) { if (disposing) popup.Dispose(); base.Dispose(disposing); }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.C("text"); base.OnRenderItemText(e); }
    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.C("selection"); public override Color MenuItemBorder => Theme.C("accent");
        public override Color ToolStripDropDownBackground => Theme.C("recess"); public override Color ImageMarginGradientBegin => Theme.C("recess");
        public override Color ImageMarginGradientMiddle => Theme.C("recess"); public override Color ImageMarginGradientEnd => Theme.C("recess");
        public override Color MenuBorder => Theme.C("inputBorder"); public override Color SeparatorDark => Theme.C("border"); public override Color SeparatorLight => Theme.C("border");
    }
}

internal sealed class DarkNumber : TextBox
{
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 4;
    public decimal Value { get => decimal.TryParse(Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var n) && n >= Minimum && n <= Maximum ? n : throw new InvalidOperationException($"Multiplier must be between {Minimum} and {Maximum}."); set => Text = value.ToString("0.00"); }
    public DarkNumber() { BackColor = Theme.C("recess"); ForeColor = Theme.C("text"); Font = Theme.Number; BorderStyle = BorderStyle.FixedSingle; }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Up or Keys.Down) { try { Value = Math.Clamp(Value + (e.KeyCode == Keys.Up ? .25m : -.25m), Minimum, Maximum); } catch (InvalidOperationException) { Value = 1; } e.Handled = true; } base.OnKeyDown(e); }
}

internal sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

internal sealed class DarkScroll : Control
{
    int value;
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public event EventHandler? ValueChanged;
    public int Value { get => value; set { int next = Math.Clamp(value, 0, Math.Max(0, Total - Page)); if (this.value != next) { this.value = next; ValueChanged?.Invoke(this, EventArgs.Empty); } Invalidate(); } }
    bool dragging;
    public DarkScroll() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Width = 14; Dock = DockStyle.Right; TabStop = false; AccessibleRole = AccessibleRole.ScrollBar; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.C("recess")); if (Total <= Page) return;
        int height = Math.Max(20, Height * Page / Math.Max(1, Total)); int top = (int)((Height - height) * (value / (double)Math.Max(1, Total - Page)));
        using var brush = new SolidBrush(Theme.C(dragging ? "accent" : "inputBorder")); e.Graphics.FillRectangle(brush, 3, top, Math.Max(1, Width - 6), height);
    }
    void MoveTo(int y) { int thumb = Math.Max(20, Height * Page / Math.Max(1, Total)); Value = (int)Math.Round((y - thumb / 2.0) / Math.Max(1, Height - thumb) * Math.Max(0, Total - Page)); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; Capture = true; MoveTo(e.Y); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) MoveTo(e.Y); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value -= e.Delta / 120 * 3; base.OnMouseWheel(e); }
}

internal sealed class DarkGrid : DataGridView
{
    readonly DarkScroll bar = new();
    bool updating;
    public DarkGrid()
    {
        DoubleBuffered = true; ScrollBars = ScrollBars.None; Controls.Add(bar);
        bar.ValueChanged += (_, _) => { if (!updating && Rows.Count > 0 && bar.Value < Rows.Count) { try { FirstDisplayedScrollingRowIndex = bar.Value; } catch (InvalidOperationException) { } } };
        RowsAdded += (_, _) => UpdateBar(); RowsRemoved += (_, _) => UpdateBar(); Scroll += (_, _) => UpdateBar(); SizeChanged += (_, _) => UpdateBar();
    }
    void UpdateBar()
    {
        if (bar == null || updating) return; updating = true;
        try { bar.Total = Rows.Count; bar.Page = Math.Max(1, DisplayedRowCount(false)); bar.Visible = Rows.Count > bar.Page; bar.Value = Math.Max(0, FirstDisplayedScrollingRowIndex); bar.BringToFront(); bar.Invalidate(); }
        finally { updating = false; }
    }
    protected override void OnMouseWheel(MouseEventArgs e) { UpdateBar(); bar.Value -= e.Delta / 120 * 3; }
}

internal sealed class DarkViewer : UserControl
{
    readonly ViewerText inner = new();
    readonly DarkScroll bar = new();
    bool updating;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    public DarkViewer()
    {
        BackColor = Theme.C("recess"); ForeColor = Theme.C("text"); Font = Theme.Body;
        inner.Dock = DockStyle.Fill; inner.Multiline = true; inner.ReadOnly = true; inner.WordWrap = true; inner.BorderStyle = BorderStyle.None; inner.BackColor = BackColor; inner.ForeColor = ForeColor; inner.Font = Font; inner.ScrollBars = ScrollBars.None;
        Controls.Add(inner); Controls.Add(bar);
        inner.TextChanged += (_, _) => UpdateBar(); inner.SizeChanged += (_, _) => UpdateBar(); inner.KeyUp += (_, _) => UpdateBar();
        inner.Wheel = delta => { UpdateBar(); bar.Value -= delta / 120 * 3; };
        bar.ValueChanged += (_, _) => { if (!updating) { int current = (int)SendMessage(inner.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero); SendMessage(inner.Handle, 0xB6, IntPtr.Zero, (IntPtr)(bar.Value - current)); } };
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (inner != null) inner.Text = Text; }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (inner != null) inner.Font = Font; }
    void UpdateBar()
    {
        if (updating || !inner.IsHandleCreated) return; updating = true;
        try { bar.Total = (int)SendMessage(inner.Handle, 0xBA, IntPtr.Zero, IntPtr.Zero); bar.Page = Math.Max(1, inner.Height / Math.Max(1, inner.Font.Height)); bar.Visible = bar.Total > bar.Page; bar.Value = (int)SendMessage(inner.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero); }
        finally { updating = false; }
    }
    sealed class ViewerText : TextBox { public Action<int>? Wheel; protected override void OnMouseWheel(MouseEventArgs e) => Wheel?.Invoke(e.Delta); }
}

// Own the small splitter surface so hidden/loading layouts do not invoke the native
// SplitContainer's GDI repaint path. Both child panels remain ordinary controls.
internal sealed class DarkSplit : UserControl
{
    public Panel Panel1 { get; } = new BufferedPanel();
    public Panel Panel2 { get; } = new BufferedPanel();
    readonly Panel divider = new();
    bool dragging;
    double fraction = .5;
    public Orientation Orientation { get; set; }
    public int SplitterDistance
    {
        get => (int)((Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width) * fraction);
        set { int length = Orientation == Orientation.Horizontal ? ClientSize.Height : ClientSize.Width; fraction = Math.Clamp(value / (double)Math.Max(1, length), .1, .9); PerformLayout(); }
    }
    public DarkSplit()
    {
        DoubleBuffered = true; BackColor = Theme.C("window"); Panel1.BackColor = Panel2.BackColor = Theme.C("panel"); divider.BackColor = Theme.C("window");
        Controls.AddRange([Panel1, Panel2, divider]);
        divider.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { dragging = true; divider.Capture = true; } };
        divider.MouseUp += (_, _) => { dragging = false; divider.Capture = false; };
        divider.MouseMove += (_, _) => { if (dragging) { var p = PointToClient(MousePosition); SplitterDistance = Orientation == Orientation.Horizontal ? p.Y : p.X; } };
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (divider == null) return;
        bool horizontal = Orientation == Orientation.Horizontal;
        int length = Math.Max(0, horizontal ? ClientSize.Height : ClientSize.Width);
        int split = Math.Clamp((int)(length * fraction), 0, Math.Max(0, length - 8));
        if (horizontal) { Panel1.SetBounds(0, 0, ClientSize.Width, split); divider.SetBounds(0, split, ClientSize.Width, Math.Min(8, length)); Panel2.SetBounds(0, Math.Min(length, split + 8), ClientSize.Width, Math.Max(0, length - split - 8)); divider.Cursor = Cursors.HSplit; }
        else { Panel1.SetBounds(0, 0, split, ClientSize.Height); divider.SetBounds(split, 0, Math.Min(8, length), ClientSize.Height); Panel2.SetBounds(Math.Min(length, split + 8), 0, Math.Max(0, length - split - 8), ClientSize.Height); divider.Cursor = Cursors.VSplit; }
    }
}
