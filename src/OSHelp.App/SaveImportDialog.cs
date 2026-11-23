using OSHelp.Core;

namespace OSHelp.App;

internal static class SaveImportDialog
{
    public static Profile? Show(IWin32Window owner, Catalog catalog, string? smokeDirectory = null, bool forSync = false)
    {
        using var window = Dialogs.Window(forSync ? "Choose character to sync" : "Import Obenseuer save", new(1080, 750));
        window.MinimumSize = new(820, 520);
        var grid = Theme.Grid(("character", "Character", 24), ("slot", "Save slot", 20), ("date", "Saved", 36), ("version", "Game version", 20));
        var details = new DarkViewer { Dock = DockStyle.Bottom, Height = 120 };
        var actions = Theme.Flow(DockStyle.Bottom, 58);
        Profile? result = null; string? selectedPath = null;
        Button read = null!, browse = null!;
        read = Theme.Button("Review selected save", async (_, _) => await Read());
        browse = Theme.Button("Browse for Info.tnmt", (_, _) =>
        {
            using var file = new OpenFileDialog { Title = "Select a save slot's Info.tnmt or Globals.tnmt", Filter = "Obenseuer save metadata|Info.tnmt;Globals.tnmt", InitialDirectory = Directory.Exists(SaveImport.DefaultSaveDirectory) ? SaveImport.DefaultSaveDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            if (file.ShowDialog(window) == DialogResult.OK) { selectedPath = file.FileName; details.Text = selectedPath; read.Enabled = true; }
        });
        var cancel = Theme.Button("Cancel", (_, _) => window.DialogResult = DialogResult.Cancel);
        actions.Controls.AddRange([read, browse, cancel]); window.CancelButton = cancel;
        window.Controls.Add(grid); window.Controls.Add(details); window.Controls.Add(actions);
        grid.CurrentCellChanged += (_, _) => { if (grid.CurrentRow?.Tag is SaveSlot slot) { selectedPath = slot.Directory; details.Text = slot.Directory + "\r\n\r\nAn existing profile name requires overwrite confirmation. Game saves are preserved."; read.Enabled = true; } };
        grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Read(); };
        window.Shown += async (_, _) =>
        {
            try
            {
                var slots = await Task.Run(() => SaveImport.Discover()); if (window.IsDisposed) return;
                foreach (var slot in slots) { int i = grid.Rows.Add(slot.Character.Replace('_', ' '), slot.Name, slot.Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), slot.Version); grid.Rows[i].Tag = slot; }
                if (grid.Rows.Count > 0) { grid.CurrentCell = grid.Rows[0].Cells[0]; selectedPath = slots[0].Directory; details.Text = selectedPath + "\r\n\r\nSelect a slot to review facilities, renovations, and stock sources."; }
                else { read.Enabled = false; details.Text = "No saves found under " + SaveImport.DefaultSaveDirectory + ". Browse for Info.tnmt or Globals.tnmt."; }
                if (smokeDirectory != null && slots.Count > 0)
                {
                    foreach (DataGridViewRow row in grid.Rows) if (row.Tag is SaveSlot slot && slot.Name.StartsWith("Slot", StringComparison.Ordinal)) { grid.CurrentCell = row.Cells[0]; break; }
                    await Task.Delay(150); Capture(window, Path.Combine(smokeDirectory, "Save-picker.png")); await Read();
                }
            }
            catch (Exception e) { if (!window.IsDisposed) details.Text = e.Message + "\r\nUse Browse to select a save directly."; }
        };
        async Task Read()
        {
            if (selectedPath == null || !read.Enabled) return;
            read.Enabled = browse.Enabled = grid.Enabled = false; details.Text = "Reading and validating saved state...";
            try
            {
                var path = selectedPath; var import = await Task.Run(() => SaveImport.Read(path, catalog));
                if (window.IsDisposed) return;
                var accepted = Review(window, catalog, import, smokeDirectory, forSync);
                if (accepted != null) { result = accepted; window.DialogResult = DialogResult.OK; }
                else details.Text = "Review cancelled. No profile was created.\r\n" + path;
            }
            catch (Exception e) { if (!window.IsDisposed) { details.Text = "Import failed. No profile was created.\r\n" + e.Message; if (smokeDirectory != null) { File.WriteAllText(Path.Combine(smokeDirectory, "error.txt"), e.ToString()); window.DialogResult = DialogResult.Cancel; } else Dialogs.Message(window, "Save import", e.Message); } }
            finally { if (!window.IsDisposed) read.Enabled = browse.Enabled = grid.Enabled = true; }
        }
        return window.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
    static Profile? Review(IWin32Window owner, Catalog catalog, SaveImportResult import, string? smokeDirectory, bool forSync)
    {
        using var window = Dialogs.Window(forSync ? "Link character and choose stock sources" : "Review game save import", new(1080, 850));
        window.MinimumSize = new(900, 700);
        var summary = new DarkViewer { Dock = DockStyle.Fill, Text = forSync
            ? $"Link this profile to {import.Slot.Character.Replace('_', ' ')}.\r\n\r\nSync uses the latest manual, automatic, or quick save in this character's folder. The current profile name and shopping goals are retained. Recorded game state and selected material stock are replaced.\r\n\r\nSelected character folder: {Path.GetDirectoryName(import.Slot.Directory)}"
            : import.Summary(catalog) };
        var sources = Theme.Panel(DockStyle.Bottom); sources.Height = Math.Max(160, 46 + import.StockSources.Count * 28);
        var label = Theme.Text("Include these sources in your material stock:"); label.Dock = DockStyle.Top; sources.Controls.Add(label);
        var choices = new List<(SaveStockSource Source, CheckBox Check)>();
        foreach (var source in import.StockSources)
        {
            var check = new CheckBox { Text = $"{source.Name} / {source.Items.Count} renovation materials / {source.Items.Values.Sum():N0} units", Checked = true, AutoSize = false, Height = 28, Width = 980,
                Location = new(10, 34 + choices.Count * 28), ForeColor = Theme.C("text"), BackColor = Theme.C("panel"), FlatStyle = FlatStyle.Flat, Font = Theme.Body };
            choices.Add((source, check)); sources.Controls.Add(check);
        }
        var actions = Theme.Flow(DockStyle.Bottom, 58); Profile? result = null;
        var confirm = Theme.Button(forSync ? "Use this character" : "Import selected state", (_, _) =>
        {
            try { result = import.CreateProfile(choices.Where(c => c.Check.Checked).Select(c => c.Source.Id)); window.DialogResult = DialogResult.OK; }
            catch (Exception e) { Dialogs.Message(window, "Save import", e.Message); }
        });
        var cancel = Theme.Button("Cancel", (_, _) => window.DialogResult = DialogResult.Cancel); actions.Controls.AddRange([confirm, cancel]); window.AcceptButton = confirm; window.CancelButton = cancel;
        window.Controls.Add(summary); window.Controls.Add(sources); window.Controls.Add(actions);
        window.ActiveControl = confirm;
        if (smokeDirectory != null) window.Shown += async (_, _) =>
        {
            // Exercise the actual source checkboxes, rather than accepting a programmatic stock list.
            foreach (var choice in choices) choice.Check.Checked = choice.Source.Id == "inventory";
            await Task.Delay(150); Capture(window, Path.Combine(smokeDirectory, "Save-review.png")); confirm.PerformClick();
        };
        return window.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
    public static bool ConfirmOverwrite(IWin32Window owner, Profile incoming, ProfileNameMatch target, int matches, string? smokeDirectory = null, bool? automatedResponse = null)
    {
        using var window = Dialogs.Window("Overwrite existing profile?", new(800, 440));
        var text = $"A profile named \"{incoming.Name.Trim()}\" already exists.\r\n\r\nExisting profile last saved: {target.SavedAt.LocalDateTime:g}\r\nImporting game save: {incoming.ImportedSave!.SaveDate.LocalDateTime:g}\r\n\r\nOverwrite its recorded state, material stock, shopping cart, construction history, and settings with this save?\r\n\r\nThe previous profile is retained as a .bak backup. Game saves are never modified.";
        if (matches > 1) text += $"\r\n\r\n{matches} profiles currently share this name. Overwriting will leave one entry; the other {matches - 1} will be archived under profiles/replaced.";
        window.Controls.Add(new DarkViewer { Dock = DockStyle.Fill, Text = text });
        var actions = Theme.Flow(DockStyle.Bottom, 58);
        var overwrite = Theme.Button("Overwrite", (_, _) => window.DialogResult = DialogResult.OK);
        var cancel = Theme.Button("Cancel", (_, _) => window.DialogResult = DialogResult.Cancel);
        actions.Controls.AddRange([overwrite, cancel]); window.Controls.Add(actions); window.AcceptButton = cancel; window.CancelButton = cancel; window.ActiveControl = cancel;
        if (automatedResponse.HasValue) window.Shown += async (_, _) =>
        {
            await Task.Delay(150);
            if (smokeDirectory != null) Capture(window, Path.Combine(smokeDirectory, automatedResponse.Value ? "Overwrite-confirm.png" : "Overwrite-cancel.png"));
            (automatedResponse.Value ? overwrite : cancel).PerformClick();
        };
        return window.ShowDialog(owner) == DialogResult.OK;
    }
    static void Capture(Form window, string path) { using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new(0, 0, window.Width, window.Height)); image.Save(path); }
}
