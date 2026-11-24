using System.Diagnostics;
using System.Text;
using OSHelp.Core;

namespace OSHelp.App;

internal sealed partial class MainForm
{
    readonly System.Windows.Forms.Timer craftingDebounce = new() { Interval = 300 };
    TextBox craftingSearch = null!;
    DarkCombo craftingMode = null!;
    CheckBox craftingFailures = null!;
    CheckBox craftingFood = null!, craftingNonFood = null!;
    DataGridView craftingGrid = null!;
    DarkViewer craftingDetails = null!;
    Label craftingStatus = null!;
    Button craftingMore = null!, craftingRetry = null!;
    DarkSplit craftingSplit = null!;
    Task<CraftingDirectory>? craftingLoad;
    CraftingDirectory? craftingDirectory;
    int craftingRevision, craftingLimit = 100, craftingSearchRuns, craftingRenderedRevision;
    bool craftingRendering, craftingForceRefresh;
    CraftingSort craftingSort;
    bool craftingDescending;
    string? craftingSelection;

    Control BuildCrafting()
    {
        var page = Theme.Panel(DockStyle.Fill, 0);
        var header = Theme.Panel(DockStyle.Top, 0); header.AutoSize = true; header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var search = Theme.Flow(DockStyle.Top, 52); search.AutoSize = true; search.AutoSizeMode = AutoSizeMode.GrowAndShrink; search.MinimumSize = new(0, 52);
        craftingMode = Theme.Combo(150); craftingMode.AccessibleName = "Crafting search mode";
        craftingMode.Items.AddRange(["By input", "By output"]); craftingMode.SelectedIndex = 0;
        search.Controls.Add(craftingMode); search.Controls.Add(Theme.Text("Item:"));
        craftingSearch = Theme.Input("", 340); craftingSearch.PlaceholderText = "Type an item or furniture name..."; craftingSearch.AccessibleName = "Crafting item search";
        search.Controls.Add(craftingSearch);
        craftingFood = Check("Food", true); craftingNonFood = Check("Non-food", true);
        craftingFood.AccessibleDescription = craftingNonFood.AccessibleDescription = "Food includes successful outputs and alternatives in the game's Food or Dough categories. Ingredients and failure products do not determine the classification.";
        search.Controls.Add(craftingFood); search.Controls.Add(craftingNonFood);
        craftingFailures = Check("Include failure products", false); craftingFailures.Enabled = false; search.Controls.Add(craftingFailures);
        search.Controls.Add(Theme.Button("Clear", (_, _) => { craftingSearch.Clear(); craftingSearch.Focus(); }));
        var summary = Theme.Flow(DockStyle.Bottom, 48); summary.AutoSize = true; summary.AutoSizeMode = AutoSizeMode.GrowAndShrink; summary.MinimumSize = new(0, 48);
        craftingStatus = Theme.Text("Type an item name to find recipes. Nothing is searched until you type."); summary.Controls.Add(craftingStatus);
        craftingMore = Theme.Button("Show more", async (_, _) => { craftingLimit += 100; craftingDebounce.Stop(); await SearchCrafting(++craftingRevision); });
        craftingMore.Visible = false; summary.Controls.Add(craftingMore);
        craftingRetry = Theme.Button("Retry", (_, _) => ScheduleCraftingSearch()); craftingRetry.Visible = false; summary.Controls.Add(craftingRetry);
        header.Controls.Add(search); header.Controls.Add(summary);
        craftingSplit = Split(); craftingSplit.SplitterDistance = 740;
        craftingGrid = Theme.Grid(("recipe", "Recipe", 28), ("inputs", "Inputs", 23), ("outputs", "Products", 23), ("stations", "Workstations", 26));
        foreach (DataGridViewColumn column in craftingGrid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        craftingGrid.ColumnHeaderMouseClick += async (_, e) =>
        {
            if (e.ColumnIndex < 0 || craftingSearch.Text.Trim().Length == 0) return;
            var next = (CraftingSort)e.ColumnIndex; craftingDescending = craftingSort == next && !craftingDescending; craftingSort = next;
            craftingDebounce.Stop(); await SearchCrafting(++craftingRevision);
        };
        craftingGrid.CurrentCellChanged += (_, _) =>
        {
            if (craftingRendering) return;
            craftingSelection = (craftingGrid.CurrentRow?.Tag as CraftingEntry)?.Recipe.Key; RenderCraftingRecipe();
        };
        craftingSplit.Panel1.Controls.Add(craftingGrid);
        craftingDetails = new DarkViewer { Dock = DockStyle.Fill, Text = "Search by input to find uses for an item, or by output to find how to make it." };
        craftingSplit.Panel2.Controls.Add(craftingDetails); Attach(page, craftingSplit, header);
        craftingSearch.TextChanged += (_, _) => ScheduleCraftingSearch();
        craftingMode.SelectedIndexChanged += (_, _) => { craftingFailures.Enabled = craftingMode.SelectedIndex == 1; ScheduleCraftingSearch(); };
        craftingFailures.CheckedChanged += (_, _) => ScheduleCraftingSearch();
        craftingFood.CheckedChanged += (_, _) => ScheduleCraftingSearch();
        craftingNonFood.CheckedChanged += (_, _) => ScheduleCraftingSearch();
        craftingDebounce.Tick += async (_, _) => { craftingDebounce.Stop(); await SearchCrafting(craftingRevision); };
        FormClosed += (_, _) => { craftingDebounce.Stop(); craftingDebounce.Dispose(); };
        return page;
    }
    void ClearCraftingRows()
    {
        craftingRendering = true;
        try { craftingGrid.Rows.Clear(); }
        finally { craftingRendering = false; }
        craftingMore.Visible = false; craftingRetry.Visible = false;
        craftingDetails.Text = "Search by input to find uses for an item, or by output to find how to make it.";
    }
    void ScheduleCraftingSearch()
    {
        craftingDebounce.Stop(); craftingRevision++; craftingLimit = 100; ClearCraftingRows();
        if (craftingSearch.Text.Trim().Length == 0)
        { craftingStatus.Text = "Type an item name to find recipes. Nothing is searched until you type."; return; }
        craftingStatus.Text = "Waiting for typing to finish..."; craftingDebounce.Start();
    }
    void ResetCraftingCatalog()
    {
        craftingDebounce.Stop(); craftingRevision++; craftingDirectory = null; craftingLoad = null; craftingForceRefresh = true;
        ClearCraftingRows(); craftingStatus.Text = "Crafting data will refresh on the next search.";
    }
    async Task SearchCrafting(int revision)
    {
        if (IsDisposed || revision != craftingRevision || craftingSearch.Text.Trim().Length == 0) return;
        string query = craftingSearch.Text.Trim();
        var mode = (CraftingSearchMode)Math.Max(0, craftingMode.SelectedIndex);
        bool includeFailures = craftingFailures.Checked, descending = craftingDescending;
        bool includeFood = craftingFood.Checked, includeNonFood = craftingNonFood.Checked;
        int limit = craftingLimit; var sort = craftingSort;
        craftingSearchRuns++; craftingRetry.Visible = false;
        try
        {
            if (craftingLoad == null || craftingLoad.IsFaulted || craftingLoad.IsCanceled)
            {
                craftingStatus.Text = "Loading crafting data...";
                bool force = craftingForceRefresh;
                var progress = new Progress<string>(message =>
                {
                    if (!IsDisposed && revision == craftingRevision && craftingDirectory == null) craftingStatus.Text = message;
                });
                craftingLoad = Task.Run(async () => new CraftingDirectory(await CraftingImport.LoadAsync(repository, gameRoot,
                    dataDirectory, catalog.AssemblyHash, force, progress, cancellation.Token)), cancellation.Token);
            }
            var loading = craftingLoad;
            var directory = await loading;
            if (IsDisposed || revision != craftingRevision || !ReferenceEquals(loading, craftingLoad)) return;
            craftingDirectory = directory; craftingForceRefresh = false;
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(() => directory.Search(query, mode, includeFailures, limit, sort, descending, cancellation.Token, includeFood, includeNonFood), cancellation.Token);
            if (IsDisposed || revision != craftingRevision) return;
            craftingRendering = true; craftingGrid.SuspendLayout();
            try
            {
                craftingGrid.Rows.Clear();
                foreach (var entry in result.Rows) AddRow(craftingGrid, entry, entry.Name, entry.Inputs, entry.Outputs, entry.StationText);
                foreach (DataGridViewColumn column in craftingGrid.Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
                craftingGrid.Columns[(int)sort].HeaderCell.SortGlyphDirection = descending ? SortOrder.Descending : SortOrder.Ascending;
                foreach (DataGridViewRow row in craftingGrid.Rows)
                    if ((row.Tag as CraftingEntry)?.Recipe.Key == craftingSelection) { craftingGrid.CurrentCell = row.Cells[0]; break; }
            }
            finally { craftingGrid.ResumeLayout(); craftingRendering = false; }
            craftingRenderedRevision = revision;
            craftingStatus.Text = !includeFood && !includeNonFood ? "Enable Food or Non-food to show recipes." : result.Total == 0 ? $"No recipes found for “{query}”." : result.Rows.Count < result.Total
                ? $"Showing {result.Rows.Count} of {result.Total} recipes for “{query}”. Refine the search or show more."
                : $"{result.Total} recipe{(result.Total == 1 ? "" : "s")} for “{query}”.";
            craftingMore.Visible = result.Rows.Count < result.Total;
            RenderCraftingRecipe();
            if (SmokeDirectory != null) File.AppendAllText(Path.Combine(SmokeDirectory, "crafting-search-times.txt"), query + ": " + stopwatch.Elapsed.TotalMilliseconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ms including rendering\n");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (IsDisposed || revision != craftingRevision) return;
            ClearCraftingRows(); craftingStatus.Text = "Crafting data could not be loaded. Retry when the game files are available.";
            craftingDetails.Text = e.Message; craftingRetry.Visible = true;
            if (SmokeDirectory != null) throw;
        }
    }
    void RenderCraftingRecipe()
    {
        if (craftingDirectory == null || craftingGrid.CurrentRow?.Tag is not CraftingEntry entry) return;
        var recipe = entry.Recipe; var text = new StringBuilder().AppendLine(recipe.Name).AppendLine(recipe.Key).AppendLine();
        text.AppendLine("Food classification: " + (entry.IsFood ? "Food" : "Non-food") + " (successful outputs).").AppendLine();
        text.AppendLine("INPUTS"); foreach (var line in recipe.Inputs) text.AppendLine(craftingDirectory.Line(line, true));
        text.AppendLine().AppendLine("PRODUCTS ON SUCCESS"); foreach (var line in recipe.Outputs) text.AppendLine(craftingDirectory.Line(line, true));
        if (recipe.Outputs.Count == 0) text.AppendLine("No item products.");
        if (recipe.FailureOutputs.Count > 0)
        { text.AppendLine().AppendLine("PRODUCTS ON FAILURE"); foreach (var line in recipe.FailureOutputs) text.AppendLine(craftingDirectory.Line(line, true)); }
        text.AppendLine().AppendLine("WORKSTATIONS").AppendLine("Category: " + recipe.Type);
        foreach (var match in entry.Workstations)
        {
            text.AppendLine(match.Label + (match.Station.Efficiency > 0 ? $" ({match.Station.Efficiency:P0} efficiency)" : ""));
            foreach (var addon in match.Addons) text.AppendLine($"    {addon.Name} must be active and connected within {addon.Range:0.##} m.");
        }
        if (entry.Workstations.Count == 0) text.AppendLine("No compatible equipment definition was found. This does not establish that the recipe is available to the player.");
        if (recipe.Required.Count > 0) text.AppendLine("Requires: " + string.Join(", ", recipe.Required));
        if (recipe.Excluded.Count > 0) text.AppendLine("Cannot use: " + string.Join(", ", recipe.Excluded));
        text.AppendLine("Power, placement conditions, and access to the workstation still apply.");
        text.AppendLine().AppendLine("RECIPE REQUIREMENTS").AppendLine($"Skill: {recipe.Skill}; listed minimum {recipe.MinimumSkill}" + (recipe.HardMinimumSkill ? " (hard minimum)" : ""));
        text.AppendLine(recipe.BlueprintRequired ? "Learned recipe / blueprint required." : "No learned recipe required.");
        if (recipe.StolenOnly) text.AppendLine("This recipe requires stolen inputs; game-defined exemptions may apply.");
        if (recipe.LiquidContainers) text.AppendLine("Liquid ingredients can be supplied in compatible containers.");
        text.AppendLine($"Base recipe time: {TimeSpan.FromSeconds(recipe.BaseSeconds):g}. Skills, ingredients and equipment can change it.");
        if (recipe.Type.StartsWith("Grow", StringComparison.Ordinal) || recipe.Type == "BreedAnimals") text.AppendLine("Plant establishment, fruit growth and animal growth have their own timers.");
        if (recipe.Type != "Furniture") text.AppendLine($"Base failure chance: {recipe.BaseFailureChance:P2}; actual chance depends on skill and conditions.");
        text.AppendLine().AppendLine("SOURCE").AppendLine(recipe.Source);
        foreach (var match in entry.Workstations) text.AppendLine(match.Label + ": " + string.Join(", ", match.Station.Evidence));
        craftingDetails.Text = text.ToString();
    }
}
