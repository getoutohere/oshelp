using System.Text.Json;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    async Task VerifyCartCleanup()
    {
        var original = profile;
        var originalSize = ClientSize;
        var originalPage = activePage;
        try
        {
            var test = Profile.Create(catalog, "UI completed cart cleanup");
            foreach (var key in test.BuildingLevels.Keys) test.BuildingLevels[key] = 0;
            foreach (var apartment in test.Apartments)
                foreach (var key in apartment.Levels.Keys) apartment.Levels[key] = 0;
            var bathroom = catalog.Upgrades.Single(u => u.Name == "Small-Bathroom-1-Makeshift");
            var walls = catalog.Upgrades.Single(u => u.Name == "Small-Walls-1-Makeshift");
            Planner.Confirm(catalog, test, [new() { Apartment = 4, UpgradeId = bathroom.Id }, new() { Apartment = 4, UpgradeId = walls.Id }, new() { Apartment = 10, UpgradeId = bathroom.Id }]);
            foreach (var line in Planner.Cart(catalog, test)) test.Stock[line.ItemKey] = line.Required;
            var completed = test.Jobs.Single(j => j.Apartment == 4 && j.UpgradeId == bathroom.Id);
            Planner.Start(catalog, test, completed); Planner.Complete(catalog, test, completed);
            Planner.RecordUnderway(catalog, test, test.Jobs.Single(j => j.Apartment == 10));
            store.Save(test); profile = test; RefreshProfiles(); RefreshViews(); ShowPage("Shopping Cart");
            if (!removeCompletedButton.Visible || !removeCompletedButton.Enabled || jobGrid.Rows.Count != 3)
                throw new InvalidOperationException("Completed cart cleanup must be available for mixed job states.");
            var stock = JsonSerializer.Serialize(profile.Stock, Json.Options);
            var apartmentsBefore = JsonSerializer.Serialize(profile.Apartments, Json.Options);
            var remaining = JsonSerializer.Serialize(profile.Jobs.Where(j => j.Status != JobStatus.Completed), Json.Options);
            var materials = JsonSerializer.Serialize(Planner.Cart(catalog, profile), Json.Options);
            foreach (var size in new[] { originalSize, new Size(1120, 740) })
            {
                ClientSize = size; PerformLayout(); await Task.Delay(100);
                var toolbar = removeCompletedButton.Parent!;
                if (!toolbar.Parent!.ClientRectangle.Contains(toolbar.Bounds)
                    || toolbar.Controls.Cast<Control>().Any(c => !toolbar.ClientRectangle.Contains(c.Bounds)))
                    throw new InvalidOperationException("Shopping cart actions are clipped at a supported window size.");
            }
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Cart-before-cleanup.png")); }
            removeCompletedButton.PerformClick();
            if (removeCompletedButton.Enabled || jobGrid.Rows.Count != 2 || profile.Jobs.Any(j => j.Status == JobStatus.Completed)
                || JsonSerializer.Serialize(profile.Jobs, Json.Options) != remaining
                || JsonSerializer.Serialize(profile.Stock, Json.Options) != stock
                || JsonSerializer.Serialize(profile.Apartments, Json.Options) != apartmentsBefore
                || JsonSerializer.Serialize(Planner.Cart(catalog, profile), Json.Options) != materials)
                throw new InvalidOperationException("Completed cleanup changed outstanding work, recorded levels, or stock.");
            profile = store.Load(profile.Id); RefreshViews();
            if (profile.Jobs.Count != 2 || profile.Jobs.Any(j => j.Status == JobStatus.Completed) || profile.Level("bathroom", 4) != 1)
                throw new InvalidOperationException("Completed cart cleanup did not survive save/reload.");
            Planner.Confirm(catalog, profile, profile.Goals.ToArray()); RefreshViews();
            if (profile.Jobs.Any(j => j.Key == completed.Key)) throw new InvalidOperationException("Cleared completed work returned during replanning.");
            int revision = profile.Revision; RemoveCompletedJobs();
            if (profile.Revision != revision) throw new InvalidOperationException("Empty cleanup should not write a new profile revision.");
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Cart-after-cleanup.png")); }
            File.WriteAllText(Path.Combine(SmokeDirectory!, "cart-cleanup.txt"), "PASS: completed rows removed / planned and underway jobs preserved / recorded levels and stock unchanged / save and reload / no return on replanning / disabled when empty / action bar fits at minimum window size.\n");
        }
        finally
        {
            profile = original; ClientSize = originalSize; RefreshProfiles(); RefreshViews(); ShowPage(originalPage);
        }
    }
}
