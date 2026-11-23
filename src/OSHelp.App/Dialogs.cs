using OSHelp.Core;

namespace OSHelp.App;

internal static class Dialogs
{
    public static Form Window(string title, Size size)
    {
        var f = new Form { Text = title, Size = size, MinimumSize = new(480, 260), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi };
        Theme.Apply(f); f.HandleCreated += (_, _) => Theme.DarkCaption(f); return f;
    }
    public static bool Message(IWin32Window owner, string title, string text, bool confirm = false)
    {
        using var f = Window(title, new(760, 430));
        var box = new DarkViewer { Text = text, Dock = DockStyle.Fill };
        f.Controls.Add(box);
        var buttons = Theme.Flow(DockStyle.Bottom, 54);
        var ok = Theme.Button(confirm ? "Confirm" : "Close", (_, _) => { f.DialogResult = DialogResult.OK; }); buttons.Controls.Add(ok); f.AcceptButton = ok;
        if (confirm) { var cancel = Theme.Button("Cancel", (_, _) => f.DialogResult = DialogResult.Cancel); buttons.Controls.Add(cancel); f.CancelButton = cancel; }
        else f.CancelButton = ok;
        f.Controls.Add(buttons); return f.ShowDialog(owner) == DialogResult.OK;
    }
    public static string? Prompt(IWin32Window owner, string title, string label, string initial)
    {
        using var f = Window(title, new(640, 220)); var input = Theme.Input(initial, 560); input.Dock = DockStyle.Top;
        var panel = Theme.Panel(); panel.Controls.Add(input); var caption = Theme.Text(label); caption.Dock = DockStyle.Top; panel.Controls.Add(caption); f.Controls.Add(panel);
        var buttons = Theme.Flow(DockStyle.Bottom, 54);
        var ok = Theme.Button("Save", (_, _) => f.DialogResult = DialogResult.OK); var cancel = Theme.Button("Cancel", (_, _) => f.DialogResult = DialogResult.Cancel);
        buttons.Controls.AddRange([ok, cancel]); f.Controls.Add(buttons); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
    public static (float Income, int Mode)? RentalSettings(IWin32Window owner, Profile profile)
    {
        using var f = Window("Rental income and payments", new(650, 310));
        var panel = Theme.Flow(DockStyle.Fill, 180); panel.FlowDirection = FlowDirection.TopDown;
        panel.Controls.Add(Theme.Text("Income multiplier (independent of renovation costs)"));
        var income = new NumericUpDown { Minimum = 0, Maximum = 4, DecimalPlaces = 2, Increment = .25m, Value = (decimal)(profile.RentalIncomeMultiplier ?? 1), Width = 160 };
        Theme.Apply(income); panel.Controls.Add(income);
        panel.Controls.Add(Theme.Text("Rent payment schedule")); var schedule = Theme.Combo(320);
        schedule.Items.AddRange(["Default (weekly)", "Daily (30 payments/month)", "Weekly (4 payments/month)", "Biweekly (2 payments/month)", "Monthly"]);
        schedule.SelectedIndex = profile.RentPaymentMode ?? 0; panel.Controls.Add(schedule); f.Controls.Add(panel);
        var buttons = Theme.Flow(DockStyle.Bottom, 54);
        var ok = Theme.Button("Save", (_, _) => f.DialogResult = DialogResult.OK); var cancel = Theme.Button("Cancel", (_, _) => f.DialogResult = DialogResult.Cancel);
        buttons.Controls.AddRange([ok, cancel]); f.Controls.Add(buttons); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? ((float)income.Value, schedule.SelectedIndex) : null;
    }
    public static bool Plan(IWin32Window owner, Catalog catalog, Profile profile, IReadOnlyList<Goal> goals)
    {
        var preview = Planner.Preview(catalog, profile, goals);
        using var f = Window("Review still-needed construction", new(1100, 760));
        var grid = Theme.Grid(("target", "Target", 22), ("job", "Construction job", 52), ("role", "Status", 26));
        foreach (var j in preview.Jobs.Where(j => j.Status == JobStatus.Planned)) grid.Rows.Add(catalog.ScopeName(j.Apartment), catalog.UpgradeLabel(catalog.Upgrade(j.UpgradeId)), goals.Any(g => g.UpgradeId == j.UpgradeId && g.Apartment == j.Apartment) ? "Selected goal" : "Shared / required prerequisite");
        f.Controls.Add(grid);
        var text = new DarkViewer { Dock = DockStyle.Bottom, Height = 140,
            Text = "Confirm that the listed jobs still need to be constructed in your game. Record existing upgrades in State to remove satisfied prerequisites.\r\n\r\n" + string.Join("\r\n", preview.Notes) };
        f.Controls.Add(text);
        var buttons = Theme.Flow(DockStyle.Bottom, 60); var ok = Theme.Button("Confirm still-needed work", (_, _) => f.DialogResult = DialogResult.OK); var cancel = Theme.Button("Cancel", (_, _) => f.DialogResult = DialogResult.Cancel);
        buttons.Controls.AddRange([ok, cancel]); f.Controls.Add(buttons); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK;
    }
}
