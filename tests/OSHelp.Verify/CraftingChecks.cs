using System.Diagnostics;
using System.Text.Json;
using OSHelp.Core;

internal static class CraftingChecks
{
    public static async Task Run(string repository, string gameRoot, Action<bool, string> check)
    {
        var dataDirectory = Path.Combine(repository, "local/crafting-check-data");
        Directory.CreateDirectory(dataDirectory);
        var warm = Path.Combine(repository, "local/ui-smoke-data/game-file-hashes.json");
        var stamps = Path.Combine(dataDirectory, "game-file-hashes.json");
        if (!File.Exists(stamps) && File.Exists(warm)) File.Copy(warm, stamps);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "docs/evidence/manifest.json")));
        string expectedAssembly = manifest.RootElement.GetProperty("sourceHashes").EnumerateArray()
            .Single(i => i.String("file") == "Managed/Assembly-CSharp.dll").String("sha256");
        var stopwatch = Stopwatch.StartNew();
        var data = await CraftingImport.LoadAsync(repository, gameRoot, dataDirectory, expectedAssembly);
        var index = new CraftingDirectory(data);
        Console.WriteLine($"Crafting: {data.Items.Count} item/furniture/group definitions / {data.Recipes.Count} recipes / {data.Stations.Count} equipment definitions; cache load + index {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        check(index.Search("", CraftingSearchMode.Input).Rows.Count == 0 && index.Search("   ", CraftingSearchMode.Output).Total == 0, "crafting starts empty and rejects whitespace searches");
        using var recipes = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameRoot, "Obenseuer_Data/StreamingAssets/Recipes.json")));
        check(data.Recipes.Count(r => r.Type != "Furniture") == recipes.RootElement.GetProperty("Recipes").GetArrayLength(), "all installed recipe definitions are imported");
        check(data.Recipes.Count(r => r.Key.StartsWith("recipe:10421:", StringComparison.Ordinal)) == 2, "duplicate game IDs preserve both distinct recipe records");
        var imported = data.Recipes.Where(r => r.Type != "Furniture").ToArray();
        int position = 0;
        foreach (var source in recipes.RootElement.GetProperty("Recipes").EnumerateArray())
        {
            var actual = imported[position++];
            var rawInputs = source.Array("input").Select(l => (Key: CatalogImport.ItemKey(l.GetProperty("Key").String("itemnamespace"), l.GetProperty("Key").Int("ID")), Amount: l.GetProperty("Value").GetDouble())).ToArray();
            check(actual.Inputs.Take(rawInputs.Length).Select(l => (l.Key, l.Amount)).SequenceEqual(rawInputs), "every recipe preserves exact source ingredient order, identity and fractional amounts");
            var rawOutputs = source.Array("output").Select(l => (Key: CatalogImport.ItemKey(l.GetProperty("Key").String("itemnamespace"), l.GetProperty("Key").Int("ID")), Amount: l.GetProperty("Value").GetDouble()));
            check(actual.Outputs.Select(l => (l.Key, l.Amount)).SequenceEqual(rawOutputs), "every successful product is imported without confusing it with failure products");
        }
        var tiles = index.Search("tiles", CraftingSearchMode.Output).Rows.Single(e => e.Recipe.Key == "recipe:10779");
        check(tiles.Recipe.Inputs.Single(l => l.Key == "game:15580").Amount == 5 && tiles.Recipe.Inputs.Single(l => l.Key == "game:16040").Amount == 1, "tiles materials come from the game");
        check(tiles.Workstations.Any(s => s.Station.Name == "Electric Kiln") && tiles.Workstations.Any(s => s.Station.Name == "Gas Furnace"), "tiles show compatible electric and gas furnaces");
        check(tiles.Workstations.All(s => !s.Station.Modifiers.Intersect(tiles.Recipe.Excluded).Any()), "excluded crude/makeshift equipment cannot be listed as compatible");
        check(index.Search("SLAG", CraftingSearchMode.Input, limit: 2000).Rows.Any(e => e.Recipe.Key == tiles.Recipe.Key), "input search finds slag-consuming tiles recipe");
        check(index.Search("clay", CraftingSearchMode.Input).Rows.Any(e => e.Recipe.Key == tiles.Recipe.Key), "input search works for a second ingredient");
        var fancy = index.Search("Fancy Glass Bottle", CraftingSearchMode.Output).Rows.Where(e => e.Recipe.Key is "recipe:10763" or "recipe:10764").ToArray();
        check(fancy.Length == 2 && fancy.All(e => e.Recipe.Outputs.Any(l => l.Key == "game:18140")), "output search checks every success product, retaining distinct recipe variants");
        var lamp = index.Search("Flowerbox With Lamp", CraftingSearchMode.Output).Rows.Single(e => e.Recipe.Type == "Furniture");
        check(lamp.Workstations.Any(s => s.Station.Name.Contains("Work", StringComparison.OrdinalIgnoreCase)) && lamp.Recipe.Inputs.Count == 3, "furniture blueprints include their worktable and exact bill");
        check(index.Search("Scrap Metal", CraftingSearchMode.Input, limit: 2000).Rows.Any(e => e.Recipe.Key == lamp.Recipe.Key), "item input search includes furniture construction");
        check(!tiles.IsFood && !lamp.IsFood && fancy.All(e => !e.IsFood), "construction and glass recipes are non-food despite any food-related ingredients");
        check(index.Search("Bag of Green Mushrooms (Low Potency)", CraftingSearchMode.Input, limit: 2000, includeNonFood: false).Rows.Any(e => e.Recipe.Key == "recipe:10192"), "food filter retains actual pizza recipes when searching an ingredient");
        var sawdustFood = index.Search("sawdust", CraftingSearchMode.Input, limit: 2000, includeNonFood: false);
        var sawdustOther = index.Search("sawdust", CraftingSearchMode.Input, limit: 2000, includeFood: false);
        var doughRecipes = data.Recipes.Where(r => r.Name.StartsWith("Sawdust ", StringComparison.Ordinal) && r.Name.EndsWith("Dough", StringComparison.Ordinal)).ToArray();
        check(doughRecipes.Length == 6 && doughRecipes.All(r => sawdustFood.Rows.Any(e => e.Recipe.Key == r.Key))
            && doughRecipes.All(r => sawdustOther.Rows.All(e => e.Recipe.Key != r.Key)), "all six sawdust dough recipes belong to Food, not Non-food, despite lacking the game's Food tag");
        check(sawdustOther.Rows.Any(e => e.Recipe.Name == "ANFO") && sawdustOther.Rows.Any(e => e.Recipe.Name == "Plywood"), "food-preparation classification leaves industrial sawdust uses in Non-food");
        check(index.Search("Dough", CraftingSearchMode.Output, limit: 2000, includeFood: false).Total == 0, "both raw dough and prepared dough products are excluded from non-food output searches");
        foreach (var mode in Enum.GetValues<CraftingSearchMode>())
            foreach (var sort in Enum.GetValues<CraftingSort>())
                foreach (bool descending in new[] { false, true })
                {
                    var all = index.Search("a", mode, includeFailures: true, limit: 2000, sort: sort, descending: descending);
                    foreach (var flags in new[] { (Food: true, Other: true), (Food: true, Other: false), (Food: false, Other: true), (Food: false, Other: false) })
                    {
                        var expected = all.Rows.Where(e => e.IsFood ? flags.Food : flags.Other).ToArray();
                        var filtered = index.Search("a", mode, includeFailures: true, limit: 10, sort: sort, descending: descending, includeFood: flags.Food, includeNonFood: flags.Other);
                        check(filtered.Total == expected.Length && filtered.Rows.Select(e => e.Recipe.Key).SequenceEqual(expected.Take(10).Select(e => e.Recipe.Key)), "food filters compose with both modes, failure products, total counts, global sorting and pagination");
                    }
                    check(all.Rows.Any(e => e.IsFood) && all.Rows.Any(e => !e.IsFood), "real-game fixture includes both food and non-food recipes");
                }
        var foodFixture = new CraftingCatalog {
            Items = new() {
                ["food"] = new() { Key = "food", Name = "Edible", Categories = ["Food"] },
                ["other"] = new() { Key = "other", Name = "Material" },
                ["group"] = new() { Key = "group", Name = "Alternatives" }
            },
            Recipes = [
                new() { Key = "byproduct", Name = "Food byproduct", Outputs = [new() { Key = "other" }, new() { Key = "food" }] },
                new() { Key = "grouped", Name = "Food alternative", Outputs = [new() { Key = "group", Alternatives = ["food"] }] },
                new() { Key = "failure", Name = "Failure only", Inputs = [new() { Key = "food" }], Outputs = [new() { Key = "other" }], FailureOutputs = [new() { Key = "food" }] }
            ]
        };
        var foodIndex = new CraftingDirectory(foodFixture);
        check(foodIndex.Search("Edible", CraftingSearchMode.Output, includeNonFood: false).Total == 2, "successful food byproducts and concrete output alternatives count as food");
        check(foodIndex.Search("Edible", CraftingSearchMode.Output, includeFailures: true, includeFood: false).Rows.Single().Recipe.Key == "failure", "food ingredients and failure products do not classify a non-food recipe as food");
        check(index.Search("Aquarium", CraftingSearchMode.Input, limit: 2000).Rows.Any(e => e.Recipe.Type == "Furniture" && e.Recipe.Name == "Chemical Lab"), "furniture-group alternatives are searchable");
        check(index.Search("Pekka Coffee", CraftingSearchMode.Input, limit: 2000).Rows.Count > 0, "ingredient-group members are searchable");
        check(!index.Search("Glass Shards", CraftingSearchMode.Output, limit: 2000).Rows.Any(e => e.Recipe.Key == "recipe:10763")
            && index.Search("Glass Shards", CraftingSearchMode.Output, true, 2000).Rows.Any(e => e.Recipe.Key == "recipe:10763"), "failure-only products require the explicit output-search option");
        var low = index.Search("Bag of Green Mushrooms (Low Potency)", CraftingSearchMode.Input, limit: 2000);
        check(low.Rows.Any(e => e.Recipe.Key == "recipe:10192"), "additional variant ingredients are indexed");
        check(!index.Search("Bag of Green Mushrooms (Clean)", CraftingSearchMode.Input, limit: 2000).Rows.Any(e => e.Recipe.Key == "recipe:10192"), "variant matching follows the serialized allowed list rather than guessing from item names");
        check(data.Stations.Any(s => s.Name == "Fume Cabinet" && s.Types.Contains("ChemicalLab") && s.Locations.Contains("Interior Tenement A")), "world-only laboratory equipment includes its scene identity");
        var supported = data.Stations.Single(s => s.Name == "Hydroponic Farm With Support" && s.Types.Contains("GrowVines"));
        check(data.Recipes.Where(r => r.Type == "GrowVines").All(r => CraftingDirectory.MatchStations(data, r).Any(s => s.Station == supported)), "vine recipes match supported hydroponics");
        check(data.Recipes.Where(r => r.Type == "GrowVines").All(r => CraftingDirectory.MatchStations(data, r).All(s => s.Station.Name != "Flowerbox With Lamp")), "grow-light flowerbox cannot incorrectly satisfy vine recipes");
        check(index.Search("t", CraftingSearchMode.Output, limit: 10).Rows.Count > 0, "search begins with the first character");
        var broad = index.Search("a", CraftingSearchMode.Input, limit: 10);
        check(broad.Total > broad.Rows.Count && broad.Rows.Count == 10, "broad searches are bounded with a total count");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) index.Search(i % 2 == 0 ? "a" : "glass", i % 3 == 0 ? CraftingSearchMode.Input : CraftingSearchMode.Output, includeFood: i % 4 != 0, includeNonFood: i % 4 != 1);
        watch.Stop(); check(watch.ElapsedMilliseconds < 5000, "200 hot crafting searches complete within five seconds");
        Console.WriteLine($"Crafting search benchmark: 200 in-memory queries in {watch.Elapsed.TotalMilliseconds:N0} ms.");
        long before = File.GetLastWriteTimeUtc(Path.Combine(dataDirectory, "crafting.json")).Ticks;
        var warmWatch = Stopwatch.StartNew();
        var cached = await CraftingImport.LoadAsync(repository, gameRoot, dataDirectory, expectedAssembly);
        check(cached.LoadedFromCache && cached.FileValidation!.FilesHashed == 0 && cached.FileValidation.BytesHashed == 0, "warm crafting load reads no game contents and skips extraction");
        check(File.GetLastWriteTimeUtc(Path.Combine(dataDirectory, "crafting.json")).Ticks == before, "warm crafting load does not rewrite its cache");
        check(CraftingImport.Fingerprint(data.Inputs) == CraftingImport.Fingerprint(data.Inputs.AsEnumerable().Reverse()), "crafting fingerprint is deterministic across input enumeration order");
        foreach (string file in new[] { "StreamingAssets/Recipes.json", "StreamingAssets/Items.json", "sharedassets0.assets", "level6" })
            check(CraftingImport.Fingerprint(data.Inputs.Select(h => h.File == file ? new InputHash(h.File, new string('0', 64)) : h)) != data.Fingerprint, "crafting cache invalidates after a source changes: " + file);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); bool observed = false;
            try { index.Search("a", CraftingSearchMode.Input, cancel: canceled.Token); } catch (OperationCanceledException) { observed = true; }
            check(observed, "crafting searches honor cancellation");
        }
        var synthetic = new CraftingCatalog {
            Stations = [new() { Name = "Lab", Types = ["Chemistry"], Kind = 2, MaxUpgrades = 2 }],
            Addons = [new() { Name = "Cleaner", Identity = "cleaner", Kind = 2, RequiredTypes = ["Chemistry"], Modifiers = ["Clean"] },
                new() { Name = "Analyzer", Identity = "analyzer", Kind = 2, RequiredTypes = ["Chemistry"], Modifiers = ["Upgraded"] }]
        };
        var needsBoth = new CraftingRecipe { Type = "Chemistry", Required = ["Clean", "Upgraded"] };
        check(CraftingDirectory.MatchStations(synthetic, needsBoth).Single().Addons.Count == 2, "workstation matching combines required compatible add-ons");
        synthetic.Stations[0].MaxUpgrades = 1;
        check(CraftingDirectory.MatchStations(synthetic, needsBoth).Count == 0, "workstation connection limits prevent impossible upgrade combinations");
        Console.WriteLine($"Crafting warm cache: {warmWatch.Elapsed.TotalMilliseconds:N0} ms; zero game bytes hashed.");
    }
}
