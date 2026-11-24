using System.Diagnostics;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    async Task VerifyCraftingView()
    {
        var oldPage = activePage; var oldSize = ClientSize;
        ShowPage("Crafting");
        if (craftingGrid.Rows.Count != 0 || craftingLoad != null || craftingSearchRuns != 0 || !craftingFood.Checked || !craftingNonFood.Checked)
            throw new InvalidOperationException("The crafting page must start empty without loading its catalog.");
        async Task Settled()
        {
            var watch = Stopwatch.StartNew();
            while (craftingRenderedRevision != craftingRevision)
            {
                if (watch.Elapsed > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("Crafting search did not finish: " + craftingStatus.Text);
                await Task.Delay(25);
            }
        }
        try
        {
            int runs = craftingSearchRuns;
            foreach (var query in new[] { "s", "sl", "sla", "slag" }) craftingSearch.Text = query;
            if (craftingSearchRuns != runs || craftingGrid.Rows.Count != 0) throw new InvalidOperationException("Rapid typing bypassed the crafting debounce.");
            await Settled();
            if (craftingSearchRuns != runs + 1 || !craftingGrid.Rows.Cast<DataGridViewRow>().Any(r => ((CraftingEntry)r.Tag!).Recipe.Key == "recipe:10779"))
                throw new InvalidOperationException("Input search must find the slag-consuming Tiles recipe in one debounced search.");
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Crafting-input-slag.png")); }
            craftingMode.SelectedIndex = 1; craftingSearch.Text = "tiles"; await Settled();
            if (!craftingDetails.Text.Contains("Gas Furnace") || !craftingDetails.Text.Contains("Electric Kiln") || !craftingDetails.Text.Contains("Clay"))
                throw new InvalidOperationException("Output search details must show actual ingredients and compatible workstations.");
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Crafting-output-tiles.png")); }
            craftingSearch.Text = "Fancy Glass Bottle"; await Settled();
            if (craftingGrid.Rows.Cast<DataGridViewRow>().Count(r => ((CraftingEntry)r.Tag!).Recipe.Key is "recipe:10763" or "recipe:10764") != 2)
                throw new InvalidOperationException("Crafting search lost an actual glass bottle variant.");
            craftingSearch.Text = "Glass Shards"; await Settled();
            bool BottleVariant() => craftingGrid.Rows.Cast<DataGridViewRow>().Any(r => ((CraftingEntry)r.Tag!).Recipe.Key == "recipe:10763");
            if (BottleVariant()) throw new InvalidOperationException("Failure-only output appeared in the successful-products search.");
            craftingFailures.Checked = true; await Settled();
            if (!BottleVariant()) throw new InvalidOperationException("Optional failure-product search omitted a matching recipe.");
            craftingFailures.Checked = false; craftingSearch.Text = "Flowerbox With Lamp"; await Settled();
            if (craftingGrid.Rows.Count != 1 || !craftingDetails.Text.Contains("Work table")) throw new InvalidOperationException("Furniture crafting did not show its worktable.");
            craftingMode.SelectedIndex = 0; craftingSearch.Text = "Pekka Coffee"; await Settled();
            if (craftingGrid.Rows.Count == 0) throw new InvalidOperationException("Ingredient-group alternative was not searchable.");
            craftingSearch.Text = "a"; await Settled();
            if (craftingGrid.Rows.Count != 100 || !craftingMore.Visible) throw new InvalidOperationException("A one-character search should return a bounded page of results.");
            craftingMore.PerformClick(); await Settled();
            if (craftingGrid.Rows.Count != 200) throw new InvalidOperationException("Show more did not load the next bounded result page.");
            foreach (var sort in Enum.GetValues<CraftingSort>())
                foreach (bool descending in new[] { false, true })
                {
                    craftingSort = sort; craftingDescending = descending;
                    await SearchCrafting(++craftingRevision);
                    var expected = craftingDirectory!.Search("a", CraftingSearchMode.Input, limit: craftingLimit, sort: sort, descending: descending).Rows.Select(e => e.Recipe.Key);
                    if (!expected.SequenceEqual(craftingGrid.Rows.Cast<DataGridViewRow>().Select(r => ((CraftingEntry)r.Tag!).Recipe.Key)))
                        throw new InvalidOperationException("Crafting result sorting must apply before pagination.");
                }
            foreach (var mode in Enum.GetValues<CraftingSearchMode>())
                foreach (var flags in new[] { (Food: true, Other: false), (Food: false, Other: true), (Food: false, Other: false), (Food: true, Other: true) })
                {
                    craftingMode.SelectedIndex = (int)mode;
                    craftingFood.Checked = flags.Food; craftingNonFood.Checked = flags.Other;
                    await Settled();
                    var expected = craftingDirectory!.Search("a", mode, limit: 100, sort: craftingSort, descending: craftingDescending, includeFood: flags.Food, includeNonFood: flags.Other);
                    if (!expected.Rows.Select(e => e.Recipe.Key).SequenceEqual(craftingGrid.Rows.Cast<DataGridViewRow>().Select(r => ((CraftingEntry)r.Tag!).Recipe.Key))
                        || craftingMore.Visible != (expected.Total > expected.Rows.Count)
                        || craftingSort != CraftingSort.Workstations || !craftingDescending)
                        throw new InvalidOperationException("Food filters must refresh both search modes, totals and pagination while retaining sorting.");
                    if (!flags.Food && !flags.Other && !craftingStatus.Text.Contains("Enable Food or Non-food"))
                        throw new InvalidOperationException("Empty food selection must explain how to restore results.");
                }
            craftingMode.SelectedIndex = 0; craftingSearch.Text = "sawdust";
            craftingFood.Checked = false; craftingNonFood.Checked = true; await Settled();
            var sawdust = craftingGrid.Rows.Cast<DataGridViewRow>().Select(r => (CraftingEntry)r.Tag!).ToArray();
            if (sawdust.Any(e => e.Recipe.Name.EndsWith("Dough", StringComparison.Ordinal)) || !sawdust.Any(e => e.Recipe.Name == "ANFO") || craftingFailures.Enabled)
                throw new InvalidOperationException("The sawdust non-food search must omit dough while retaining industrial uses.");
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Crafting-sawdust-nonfood.png")); }
            craftingFood.Checked = true; craftingNonFood.Checked = false; await Settled();
            if (craftingGrid.Rows.Cast<DataGridViewRow>().Count(r => ((CraftingEntry)r.Tag!).Recipe.Name.EndsWith("Dough", StringComparison.Ordinal)) != 6)
                throw new InvalidOperationException("All six fresh sawdust dough recipes must appear in the food filter.");
            craftingNonFood.Checked = true;
            // Complete a held asynchronous load after clearing its query. No
            // result from the old request may repopulate the empty page.
            var directory = craftingDirectory!; var loaded = craftingLoad;
            var held = new TaskCompletionSource<CraftingDirectory>(TaskCreationOptions.RunContinuationsAsynchronously);
            craftingLoad = held.Task; craftingSearch.Text = "tiles"; craftingDebounce.Stop();
            var pending = SearchCrafting(craftingRevision);
            craftingSearch.Clear(); held.SetResult(directory); await pending;
            craftingLoad = loaded; craftingDirectory = directory;
            await Task.Delay(craftingDebounce.Interval + 50);
            if (craftingGrid.Rows.Count != 0 || craftingMore.Visible) throw new InvalidOperationException("A stale crafting request repopulated cleared results.");
            craftingMode.SelectedIndex = 1; craftingSearch.Text = "tiles"; await Settled();
            ClientSize = new(1120, 740); PerformLayout(); await Task.Delay(100);
            foreach (Control control in new Control[] { craftingSearch, craftingMode, craftingFailures, craftingFood, craftingNonFood })
                if (!control.Parent!.ClientRectangle.Contains(control.Bounds)) throw new InvalidOperationException("Crafting search controls are clipped at the minimum window size.");
            foreach (var button in navigation.Values)
                if (!button.Parent!.ClientRectangle.Contains(button.Bounds)) throw new InvalidOperationException("The added Crafting navigation clips at the minimum window size.");
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new(0, 0, Width, Height)); image.Save(Path.Combine(SmokeDirectory!, "Crafting-minimum-size.png")); }
            File.WriteAllText(Path.Combine(SmokeDirectory!, "crafting.txt"), "PASS: empty initial page and lazy cache / 300 ms typing debounce / input and output modes / real ingredient groups / success versus failure products / distinct recipe variants / furniture worktables / one-character query / bounded results and show more / global sorting / Food and Non-food filters in both modes / fresh sawdust dough classification / disabled checkbox capture / stale-result suppression / clear / minimum-size controls.\n");
        }
        finally
        {
            ClientSize = oldSize; craftingSearch.Clear(); craftingMode.SelectedIndex = 0; craftingFailures.Checked = false;
            craftingFood.Checked = craftingNonFood.Checked = true;
            craftingSort = CraftingSort.Recipe; craftingDescending = false; ShowPage(oldPage);
        }
    }
}
