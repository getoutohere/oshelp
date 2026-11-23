using System.Globalization;
using System.ComponentModel;
using System.Text;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    VendorCatalog vendorCatalog = null!;
    VendorCatalog? directorySource;
    IReadOnlyList<VendorListing> vendorDirectory = [];
    DarkSplit vendorSplit = null!;
    DataGridView vendorGrid = null!, vendorWeek = null!;
    TextBox vendorSearch = null!, vendorTime = null!;
    DarkCombo vendorDay = null!, vendorRegion = null!, vendorKind = null!;
    CheckBox vendorOpenOnly = null!;
    Label vendorCount = null!, vendorHeading = null!;
    DarkViewer vendorNotes = null!;
    sealed record VendorSort(string Column, ListSortDirection Direction);
    VendorSort? vendorSort, vendorWeekSort;
    static VendorSort? ReadVendorSort(DataGridView grid) => grid.SortedColumn is { } column && grid.SortOrder != SortOrder.None
        ? new(column.Name, grid.SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending) : null;
    static void ApplyVendorSort(DataGridView grid, VendorSort? sort)
    {
        if (sort != null) grid.Sort(grid.Columns[sort.Column], sort.Direction);
    }
    VendorListing? SelectedVendor => vendorGrid?.CurrentRow?.Tag as VendorListing;
    int VendorDay => Math.Max(0, vendorDay.SelectedIndex);
    bool VendorMinute(out int minute)
    {
        bool valid = TimeOnly.TryParseExact(vendorTime.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        minute = time.Hour * 60 + time.Minute; return valid;
    }
    Control BuildVendors()
    {
        var page = Theme.Panel(); var split = vendorSplit = Split(true);
        vendorGrid = Theme.Grid(("name", "Vendor", 25), ("operator", "Operator", 23), ("location", "Location", 22), ("hours", "Opening hours", 17), ("status", "Status", 13));
        vendorGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; vendorGrid.RowTemplate.Height = 44;
        vendorGrid.CurrentCellChanged += (_, _) => { if (!refreshing) RenderVendor(); };
        vendorGrid.Sorted += (_, _) => { if (!refreshing) vendorSort = ReadVendorSort(vendorGrid) ?? vendorSort; };
        split.Panel1.Controls.Add(vendorGrid);
        var filters = Theme.Panel(DockStyle.Top, 0); filters.Height = 100;
        var search = Theme.Flow(DockStyle.Top, 48); search.Controls.Add(Theme.Text("Find vendor or operator:")); vendorSearch = Theme.Input("", 245); search.Controls.Add(vendorSearch);
        vendorRegion = Theme.Combo(160); vendorRegion.Items.AddRange(["All locations", "Tenement", "Bazaar", "Mines", "Other"]); vendorRegion.SelectedIndex = 0; search.Controls.Add(vendorRegion);
        vendorKind = Theme.Combo(210); vendorKind.Items.AddRange(["All vendors", "Traders and shop counters", "Vending machines"]); vendorKind.SelectedIndex = 0; search.Controls.Add(vendorKind);
        var clock = Theme.Flow(DockStyle.Bottom, 48); clock.Controls.Add(Theme.Text("In-game day:")); vendorDay = Theme.Combo(170); vendorDay.Items.AddRange(VendorHours.DayNames.Cast<object>()); vendorDay.SelectedIndex = 0; clock.Controls.Add(vendorDay);
        clock.Controls.Add(Theme.Text("Time (24-hour):")); vendorTime = Theme.Input("12:00", 75); clock.Controls.Add(vendorTime);
        vendorOpenOnly = Check("Open only", false); clock.Controls.Add(vendorOpenOnly); vendorCount = Theme.Text(""); clock.Controls.Add(vendorCount);
        filters.Controls.Add(search); filters.Controls.Add(clock);
        vendorSearch.TextChanged += (_, _) => RefreshVendors(); vendorDay.SelectedIndexChanged += (_, _) => RefreshVendors(); vendorTime.TextChanged += (_, _) => RefreshVendors(); vendorRegion.SelectedIndexChanged += (_, _) => RefreshVendors(); vendorKind.SelectedIndexChanged += (_, _) => RefreshVendors(); vendorOpenOnly.CheckedChanged += (_, _) => RefreshVendors();
        var details = Split(); details.SplitterDistance = 650;
        vendorWeek = Theme.Grid(("day", "Day", 40), ("hours", "Opening hours", 60)); vendorWeek.RowTemplate.Height = 34;
        vendorWeek.Sorted += (_, _) => { if (!refreshing) vendorWeekSort = ReadVendorSort(vendorWeek) ?? vendorWeekSort; };
        vendorWeek.CurrentCellChanged += (_, _) =>
        {
            if (!refreshing && vendorWeek.CurrentRow?.Tag is int day && vendorDay.SelectedIndex != day)
                BeginInvoke(() => { if (!IsDisposed && vendorWeek.CurrentRow?.Tag is int selected && selected == day && vendorDay.SelectedIndex != day) vendorDay.SelectedIndex = day; });
        };
        details.Panel1.Controls.Add(vendorWeek);
        vendorNotes = new DarkViewer { Dock = DockStyle.Fill }; details.Panel2.Controls.Add(vendorNotes);
        var detailHeader = Theme.Panel(DockStyle.Top, 7); detailHeader.Height = 48; vendorHeading = Theme.Text("", true); vendorHeading.Dock = DockStyle.Fill; detailHeader.Controls.Add(vendorHeading);
        var source = Theme.Button("Source details", (_, _) =>
        {
            if (SelectedVendor is not { } v) return;
            var text = new StringBuilder().AppendLine(v.Name).AppendLine(v.Location);
            foreach (var record in v.Sources)
            {
                text.AppendLine().AppendLine(record.Name + " · " + record.Kind + " · " + record.Location).AppendLine(string.Join(" / ", record.Aliases)).AppendLine(string.Join("\r\n", record.Notes)).AppendLine(string.Join("\r\n", record.Evidence));
                for (int d = 0; d < 7; d++) text.AppendLine(VendorHours.DayNames[d] + ": trader " + (record.Trading?.Format(d) ?? "game state") + "; entrance " + (record.EntranceHours?.Format(d) ?? "game state"));
                foreach (var e in record.Entrances.Concat(record.InternalAccess)) text.AppendLine().AppendLine(e.Name).AppendLine(string.Join("\r\n", e.Notes)).AppendLine(string.Join("\r\n", e.Evidence));
            }
            text.AppendLine().AppendLine("Asset files checked: " + vendorCatalog.FilesScanned).AppendLine("SOURCE HASHES").AppendLine(string.Join("\r\n", vendorCatalog.Inputs.Select(i => i.File + "  " + i.Hash)));
            Dialogs.Message(this, v.Name + " · source details", text.ToString());
        }); source.Dock = DockStyle.Right; detailHeader.Controls.Add(source);
        var detailPanel = Theme.Panel(DockStyle.Fill, 0); Attach(detailPanel, details, detailHeader); split.Panel2.Controls.Add(detailPanel);
        var description = Theme.Text("Opening hours include entrance door schedules."); description.Dock = DockStyle.Bottom; description.AutoSize = false; description.Height = 30;
        Attach(page, split, filters, description); return page;
    }
    void RefreshVendors()
    {
        if (vendorGrid == null || vendorCatalog == null) return;
        if (!ReferenceEquals(directorySource, vendorCatalog)) { vendorDirectory = VendorDirectory.Build(vendorCatalog.Vendors); directorySource = vendorCatalog; }
        var selected = SelectedVendor?.Id; bool old = refreshing; refreshing = true;
        try
        {
            bool validTime = VendorMinute(out int minute); vendorGrid.Rows.Clear();
            foreach (var v in vendorDirectory)
            {
                if (!(v.Name + " " + v.Operator + " " + v.Location + " " + v.Kind + " " + string.Join(" ", v.Sources.SelectMany(s => s.Aliases.Prepend(s.Name)))).Contains(vendorSearch.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                if (vendorRegion.SelectedIndex > 0 && !v.InRegion(vendorRegion.SelectedItem!.ToString()!)) continue;
                if (vendorKind.SelectedIndex == 1 && v.Kind == "Vending machine" || vendorKind.SelectedIndex == 2 && v.Kind != "Vending machine") continue;
                var hours = v.OpeningHours;
                if (vendorOpenOnly.Checked && (!validTime || v.Inactive || hours == null || !hours.Contains(VendorDay, minute))) continue;
                string state = validTime ? v.Status(VendorDay, minute) : "Enter HH:mm";
                AddRow(vendorGrid, v, v.Name, v.Operator.Length > 0 ? v.Operator : v.Kind == "Vending machine" ? "Self-service" : "—", v.Location, hours?.Format(VendorDay) ?? "Check requirements", state);
                vendorGrid.Rows[^1].Cells["status"].Style.ForeColor = Theme.C(state == "Open" ? "success" : state is "Closed" or "Unavailable" ? "danger" : "warning");
            }
            ApplyVendorSort(vendorGrid, vendorSort);
            foreach (DataGridViewRow row in vendorGrid.Rows) if ((row.Tag as VendorListing)?.Id == selected) { vendorGrid.CurrentCell = row.Cells[0]; break; }
            vendorCount.Text = $"{vendorGrid.Rows.Count} / {vendorDirectory.Count} vendors" + (validTime ? "" : " · enter HH:mm");
        }
        finally { refreshing = old; }
        RenderVendor();
    }
    void RenderVendor()
    {
        if (vendorWeek == null) return; bool old = refreshing; refreshing = true;
        try
        {
            vendorWeek.Rows.Clear(); if (SelectedVendor is not { } v) { vendorHeading.Text = "No matching vendors"; vendorNotes.Text = "Change the search or filters to show vendors."; return; }
            vendorHeading.Text = v.Name.ToUpper(CultureInfo.CurrentCulture);
            for (int d = 0; d < 7; d++) AddRow(vendorWeek, d, VendorHours.DayNames[d], v.OpeningHours?.Format(d) ?? "Check requirements");
            ApplyVendorSort(vendorWeek, vendorWeekSort);
            vendorWeek.CurrentCell = vendorWeek.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is int day && day == VendorDay).Cells[0];
            vendorWeek.FirstDisplayedScrollingRowIndex = 0;
            var text = new StringBuilder().AppendLine(v.Location);
            if (v.Operator.Length > 0) text.AppendLine("Operator: " + v.Operator);
            if (VendorMinute(out int minute)) text.AppendLine().AppendLine(VendorHours.DayNames[VendorDay] + " " + VendorHours.Clock(minute) + ": " + v.Status(VendorDay, minute));
            if (v.Inactive) text.AppendLine().AppendLine("Available after the shop is unlocked in the game.");
            else if (v.Conditional) text.AppendLine().AppendLine("Availability also depends on game progress or access requirements.");
            if (v.ActiveSources.Any(s => s.Notes.Contains("Open this shop through the operator's dialogue."))) text.AppendLine().AppendLine("Talk to the operator to shop.");
            vendorNotes.Text = text.ToString();
        }
        finally { refreshing = old; }
    }

    async Task VerifyVendorSort()
    {
        vendorSearch.Text = ""; vendorRegion.SelectedIndex = vendorKind.SelectedIndex = 0; vendorOpenOnly.Checked = false; vendorTime.Text = "12:00";
        void CheckSort(DataGridView grid, string column, ListSortDirection direction)
        {
            var order = direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
            if (grid.SortedColumn?.Name != column || grid.SortOrder != order || grid.Columns[column].HeaderCell.SortGlyphDirection != order)
                throw new InvalidOperationException("Vendor sort column/direction/glyph was lost during refresh: " + column);
            var values = grid.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells[column].Value, CultureInfo.CurrentCulture) ?? "").ToArray();
            for (int i = 1; i < values.Length; i++)
            {
                int comparison = StringComparer.CurrentCulture.Compare(values[i - 1], values[i]);
                if (direction == ListSortDirection.Ascending ? comparison > 0 : comparison < 0)
                    throw new InvalidOperationException("Vendor rows are out of order after refresh: " + column);
            }
        }
        var hardware = vendorGrid.Rows.Cast<DataGridViewRow>().Single(r => (r.Tag as VendorListing)?.Name == "Hardware Stall");
        vendorGrid.CurrentCell = hardware.Cells[0]; var selected = SelectedVendor!.Id;
        foreach (DataGridViewColumn column in vendorGrid.Columns)
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                vendorGrid.Sort(column, direction);
                for (int day = 0; day < 7; day++)
                {
                    vendorDay.SelectedIndex = day; CheckSort(vendorGrid, column.Name, direction);
                    if (SelectedVendor?.Id != selected) throw new InvalidOperationException("Changing day/sort lost the selected vendor.");
                }
                vendorTime.Text = "18:30"; CheckSort(vendorGrid, column.Name, direction);
                vendorSearch.Text = "no-vendor-matches-this-search"; CheckSort(vendorGrid, column.Name, direction);
                vendorDay.SelectedIndex = 1; vendorSearch.Text = ""; CheckSort(vendorGrid, column.Name, direction);
                vendorRegion.SelectedIndex = 3; CheckSort(vendorGrid, column.Name, direction);
                vendorRegion.SelectedIndex = 0;
                vendorKind.SelectedIndex = 2; CheckSort(vendorGrid, column.Name, direction);
                vendorKind.SelectedIndex = 0;
                vendorOpenOnly.Checked = true; CheckSort(vendorGrid, column.Name, direction);
                vendorOpenOnly.Checked = false; vendorTime.Text = "12:00";
                RefreshViews(); CheckSort(vendorGrid, column.Name, direction);
                vendorGrid.CurrentCell = vendorGrid.Rows.Cast<DataGridViewRow>().Single(r => (r.Tag as VendorListing)?.Id == selected).Cells[0];
            }
        foreach (DataGridViewColumn column in vendorWeek.Columns)
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                vendorWeek.Sort(column, direction);
                vendorDay.SelectedIndex = (VendorDay + 1) % 7; CheckSort(vendorWeek, column.Name, direction);
                if (vendorWeek.CurrentRow?.Tag is not int day || day != VendorDay) throw new InvalidOperationException("Sorted weekly table selected the wrong day.");
            }
        vendorWeek.CurrentCell = vendorWeek.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is int day && day == 4).Cells[0];
        await Task.Delay(100);
        if (VendorDay != 4 || vendorWeek.CurrentRow?.Tag is not int chosen || chosen != 4) throw new InvalidOperationException("Selecting a sorted weekday selected the wrong game day.");
        CheckSort(vendorWeek, "hours", ListSortDirection.Descending);
    }
}
