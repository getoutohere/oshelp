using System.ComponentModel;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    CheckBox[] tenantSizes = [];
    DarkCombo tenantRental = null!;
    sealed record TenantSort(string Column, ListSortDirection Direction);
    TenantSort? tenantSort;

    Control BuildTenantFilters(Control search)
    {
        var panel = Theme.Panel(DockStyle.Top, 0); panel.Height = 158;
        var sizeRow = Theme.Flow(DockStyle.Top, 48);
        sizeRow.Controls.Add(Theme.Text("Size:"));
        tenantSizes = new[] { "Small", "Medium", "Large" }.Select(name => Check(name, true)).ToArray();
        foreach (var option in tenantSizes) { sizeRow.Controls.Add(option); option.CheckedChanged += (_, _) => RefreshTenants(); }
        var rentalRow = Theme.Flow(DockStyle.Top, 52);
        rentalRow.Controls.Add(Theme.Text("Rental status:")); tenantRental = Theme.Combo(190);
        tenantRental.Items.AddRange(["Both", "Renting", "Not renting"]); tenantRental.SelectedIndex = 0;
        rentalRow.Controls.Add(tenantRental); tenantRental.SelectedIndexChanged += (_, _) => RefreshTenants();
        panel.Controls.Add(rentalRow); panel.Controls.Add(sizeRow); panel.Controls.Add(search);
        tenantGrid.Sorted += (_, _) =>
        {
            if (!refreshing && tenantGrid.SortedColumn is { } column && tenantGrid.SortOrder != SortOrder.None)
                tenantSort = new(column.Name, tenantGrid.SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
        };
        return panel;
    }

    async Task VerifyTenantFilters()
    {
        var original = profile;
        var test = ProfileStore.Clone(profile);
        foreach (var apartment in test.Apartments) { apartment.Occupant = ""; apartment.OnlyFurniture = false; }
        var jonne = catalog.Tenants.Single(t => t.Name == "Jonne Reis");
        var fenella = catalog.Tenants.Single(t => t.Name == "Fenella Hunt");
        test.Apartment(4).Occupant = jonne.Id;
        var furnitureApartment = test.Apartments.First(a => a.Number is not (1 or 2 or 4));
        furnitureApartment.Occupant = fenella.Id; furnitureApartment.OnlyFurniture = true;
        profile = test;
        try
        {
            void Sizes(params int[] enabled)
            {
                for (int i = 0; i < tenantSizes.Length; i++) tenantSizes[i].Checked = enabled.Contains(i);
            }
            bool Visible(Tenant tenant) => tenantGrid.Rows.Cast<DataGridViewRow>().Any(r => (r.Tag as Tenant)?.Id == tenant.Id);
            foreach (DataGridViewColumn column in tenantGrid.Columns)
                foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                {
                    tenantGrid.Sort(column, direction);
                    for (int mask = 0; mask < 8; mask++)
                    {
                        var sizes = Enumerable.Range(0, 3).Where(i => (mask & 1 << i) != 0).ToArray(); Sizes(sizes);
                        for (int rental = 0; rental < 3; rental++)
                        {
                            tenantRental.SelectedIndex = rental;
                            var expected = TenantDirectory.Filter(catalog, profile, sizes, (TenantRentalFilter)rental).Select(t => t.Id).Order().ToArray();
                            var actual = tenantGrid.Rows.Cast<DataGridViewRow>().Select(r => ((Tenant)r.Tag!).Id).Order().ToArray();
                            if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Tenant size/rental filters returned the wrong residents.");
                            if (tenantGrid.SortedColumn?.Name != column.Name || tenantGrid.SortOrder != (direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending)) throw new InvalidOperationException("Tenant filters lost the chosen sort.");
                            var values = tenantGrid.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells[column.Name].Value) ?? "").ToArray();
                            for (int i = 1; i < values.Length; i++)
                            {
                                int compared = StringComparer.CurrentCulture.Compare(values[i - 1], values[i]);
                                if (direction == ListSortDirection.Ascending ? compared > 0 : compared < 0) throw new InvalidOperationException("Filtered tenant rows are out of order.");
                            }
                        }
                    }
                }
            tenantRental.SelectedIndex = 0;
            Sizes(0); if (!Visible(jonne)) throw new InvalidOperationException("Small/medium tenant missing from Small.");
            Sizes(1); if (!Visible(jonne) || !Visible(fenella)) throw new InvalidOperationException("Multi-size tenants missing from Medium.");
            Sizes(2); if (Visible(jonne) || !Visible(fenella)) throw new InvalidOperationException("Large filter mishandled accepted sizes.");
            Sizes(0,1,2); tenantRental.SelectedIndex = 1;
            if (tenantGrid.Rows.Count != 1 || SelectedTenant?.Id != jonne.Id) throw new InvalidOperationException("Renting filter must include active renters and exclude furniture-only placement.");
            tenantSearch.Text = "Deekula A";
            if (tenantGrid.Rows.Count != 0 || tenantHeading.Text != "No matching resident") throw new InvalidOperationException("Search and rental filters did not compose or clear the empty detail view.");
            tenantSearch.Text = ""; tenantRental.SelectedIndex = 2;
            if (Visible(jonne) || !Visible(fenella)) throw new InvalidOperationException("Not renting filter is incorrect.");
            profile.Apartment(4).OnlyFurniture = true; RefreshViews();
            if (!Visible(jonne)) throw new InvalidOperationException("Rental filters failed to update after profile state changed.");
            profile = original; RefreshViews();
            if (tenantRental.SelectedIndex != 2 || tenantSizes.Any(c => !c.Checked)) throw new InvalidOperationException("Profile refresh reset the tenant filters.");
            var size = ClientSize; ClientSize = new(1120,740); PerformLayout();
            foreach (var control in tenantSizes.Cast<Control>().Append(tenantRental)) if (!control.Parent!.ClientRectangle.Contains(control.Bounds)) throw new InvalidOperationException("Tenant filters are clipped at the minimum window size.");
            ClientSize = size; ShowPage("Tenants"); await Task.Delay(100);
            using var image = new Bitmap(Width,Height); DrawToBitmap(image,new(0,0,Width,Height)); image.Save(Path.Combine(SmokeDirectory!,"Tenant-filters.png"));
            File.WriteAllText(Path.Combine(SmokeDirectory!,"tenant-filters.txt"),"PASS: Small/Medium/Large only / multi-size acceptance / all size combinations and rental states / search composition / furniture-only exclusion / state refresh / sort preservation / empty details / minimum window size.\n");
        }
        finally
        {
            profile = original; tenantSearch.Text = ""; tenantRental.SelectedIndex = 0;
            foreach (var option in tenantSizes) option.Checked = true;
            tenantSort = null; tenantGrid.Sort(tenantGrid.Columns["name"], ListSortDirection.Ascending);
            RefreshViews();
        }
    }
}
