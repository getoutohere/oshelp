using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OSHelp.Core;

public static class CraftingImport
{
    public static string[] SourceFiles(string gameRoot) => Directory.EnumerateFiles(Path.Combine(gameRoot, "Obenseuer_Data"))
        .Select(Path.GetFileName).Where(f => f!.EndsWith(".assets", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(f, "^level[0-9]+$"))
        .Cast<string>().Concat(["globalgamemanagers", "Managed/Assembly-CSharp.dll", "Managed/Assembly-CSharp-firstpass.dll", "StreamingAssets/Items.json", "StreamingAssets/Recipes.json"])
        .Order(StringComparer.Ordinal).ToArray();

    public static async Task<CraftingCatalog> LoadAsync(string repository, string gameRoot, string dataDirectory,
        string expectedAssemblyHash, bool force = false, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(dataDirectory);
        var validation = new GameFileValidation(gameRoot, dataDirectory, force);
        var hashes = new List<InputHash>();
        foreach (string file in SourceFiles(gameRoot))
        {
            cancel.ThrowIfCancellationRequested(); progress?.Report("Checking crafting data: " + file);
            hashes.Add(new(file, await validation.HashAsync(file, cancel).ConfigureAwait(false)));
        }
        if (hashes.Single(h => h.File == "Managed/Assembly-CSharp.dll").Hash != expectedAssemblyHash)
            throw new InvalidDataException("The game crafting code has changed. Refresh the game catalog before using this view.");
        string fingerprint = Fingerprint(hashes);
        string path = Path.Combine(dataDirectory, "crafting.json");
        if (!force && File.Exists(path))
        {
            try
            {
                var cached = Json.Read<CraftingCatalog>(path);
                if (cached.FormatVersion == CraftingCatalog.CurrentFormat && cached.Fingerprint == fingerprint)
                { Validate(cached); cached.FileValidation = validation; cached.LoadedFromCache = true; validation.Save(); return cached; }
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or IOException) { /* Rebuild a damaged derived cache. */ }
        }
        string staging = Path.Combine(dataDirectory, "crafting-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("Reading workstation and furniture blueprint definitions...");
            await CatalogImport.RunScript(repository, "Export-CraftingData.ps1", gameRoot, staging, cancel).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            progress?.Report("Building crafting search indexes...");
            var result = Build(Path.Combine(gameRoot, "Obenseuer_Data/StreamingAssets/Items.json"),
                Path.Combine(gameRoot, "Obenseuer_Data/StreamingAssets/Recipes.json"), Path.Combine(staging, "crafting-assets.json"));
            result.Fingerprint = fingerprint; result.Inputs = hashes; Validate(result);
            cancel.ThrowIfCancellationRequested();
            if (!SourceFiles(gameRoot).SequenceEqual(hashes.Select(h => h.File))) throw new IOException("Game files changed during crafting import. Retry the search.");
            foreach (var input in hashes)
                if (await validation.HashAsync(input.File, cancel).ConfigureAwait(false) != input.Hash)
                    throw new IOException("Game data changed during crafting import. Retry the search.");
            result.FileValidation = validation;
            AtomicFile.Write(path, JsonSerializer.Serialize(result, Json.Options)); validation.Save(); return result;
        }
        finally
        {
            if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(staging, true); } catch (IOException) { }
        }
    }
    public static string Fingerprint(IEnumerable<InputHash> hashes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        "crafting-v" + CraftingCatalog.CurrentFormat + "\n" + string.Join("\n", hashes.OrderBy(h => h.File, StringComparer.Ordinal).Select(h => h.File + ":" + h.Hash)))));

    sealed class Record
    {
        public string SourceFile { get; set; } = "";
        public long PathId { get; set; }
        public string Class { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Enabled { get; set; }
        public bool Furniture { get; set; }
        public string Scene { get; set; } = "";
        public JsonElement Fields { get; set; }
        public List<string> ExternalFiles { get; set; } = [];
        public string Key => SourceFile + ":" + PathId;
        public string Pointer(JsonElement p) => p.GetProperty("pathId").GetInt64() == 0 ? "" :
            (p.Int("fileId") == 0 ? SourceFile : Path.GetFileName(ExternalFiles[p.Int("fileId") - 1])) + ":" + p.GetProperty("pathId").GetInt64();
    }
    public static CraftingCatalog Build(string itemsPath, string recipesPath, string assetsPath)
    {
        using var itemJson = JsonDocument.Parse(File.ReadAllText(itemsPath));
        using var recipeJson = JsonDocument.Parse(File.ReadAllText(recipesPath));
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (assets.RootElement.Int("formatVersion") != 2) throw new InvalidDataException("Unsupported crafting asset export format.");
        var data = new CraftingCatalog();
        var references = new Dictionary<string, JsonElement>();
        void Collect(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("$id", out var id)) references.Add(id.GetString()!, value);
                foreach (var property in value.EnumerateObject()) Collect(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Collect(child);
        }
        Collect(itemJson.RootElement);
        JsonElement Resolve(JsonElement value)
        {
            var visited = new HashSet<string>();
            while (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var reference))
            {
                string id = reference.GetString()!;
                if (!visited.Add(id) || !references.TryGetValue(id, out value)) throw new InvalidDataException("Unresolved crafting item metadata reference " + id);
            }
            return value;
        }
        string ItemKey(JsonElement reference)
        {
            reference = Resolve(reference);
            return CatalogImport.ItemKey(reference.String("itemnamespace"), reference.Int("ID"));
        }
        var itemNodes = itemJson.RootElement.EnumerateArray().ToArray();
        foreach (var node in itemNodes)
        {
            string key = CatalogImport.ItemKey("", node.Int("ID"));
            var categories = node.Array("Categories").Select(c => c.GetString()!).ToList();
            data.Items.Add(key, new() { Key = key, Name = node.String("Title"), Categories = categories, Liquid = categories.Contains("Liquid") });
        }
        foreach (var node in itemNodes)
        {
            var item = data.Items[CatalogImport.ItemKey("", node.Int("ID"))];
            foreach (var raw in node.Array("Meta"))
            {
                var meta = Resolve(raw);
                if (meta.String("$type") == "OS.Items.ItemGroupData")
                    foreach (var member in meta.Array("itemGroup"))
                        item.Members.Add(new(ItemKey(member.GetProperty("item")), member.String("groupItemTag"),
                            member.GetProperty("craftingValues").String("useOutputTag")));
                if (meta.TryGetProperty("liquidItem", out var liquid) && Resolve(liquid).ValueKind == JsonValueKind.Object
                    && Resolve(liquid).Int("ID") > 0 && meta.TryGetProperty("liquidAmount", out var amount) && amount.GetDouble() > 0)
                    item.ContainedLiquid = ItemKey(liquid);
            }
        }
        var records = assets.RootElement.GetProperty("records").Deserialize<List<Record>>(Json.Options)!;
        string Enum(string type, int number) => assets.RootElement.GetProperty("enums").GetProperty(type).GetProperty(number.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetString()!;
        List<string> Enums(JsonElement f, string field, string type) => f.Array(field).Select(x => Enum(type, x.GetInt32())).Where(x => x != "None").ToList();
        double Number(JsonElement f, string key) => f.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 0;
        foreach (var record in records.Where(r => r.Class == "Furniture"))
        {
            string key = "furniture:" + record.Key;
            data.Items.Add(key, new() { Key = key, Name = record.Fields.String("title"), Categories = ["Furniture"] });
        }
        foreach (var record in records.Where(r => r.Class == "FurnitureGroup"))
        {
            string key = "furniture-group:" + record.Key;
            data.Items.Add(key, new() { Key = key, Name = record.Fields.String("title"), Categories = ["Furniture"],
                Members = record.Fields.Array("furnitures").Select(p => new CraftingGroupMember("furniture:" + record.Pointer(p), "", "")).ToList() });
        }
        foreach (var variant in records.Where(r => r.Class == "ItemVariantManager").SelectMany(r => r.Fields.Array("variantCategories")))
        {
            string category = Enum("OS.Items.VariantCategory", variant.Int("category")), key = "variant:" + category;
            var alternatives = variant.Array("variants").Select(v => ItemKey(v.GetProperty("item"))).ToHashSet();
            if (variant.String("itemCategory") is { Length: > 0 } itemCategory)
                alternatives.UnionWith(data.Items.Values.Where(i => i.Categories.Contains(itemCategory)).Select(i => i.Key));
            if (data.Items.TryGetValue(key, out var existing)) alternatives.UnionWith(existing.Members.Select(m => m.Key));
            data.Items[key] = new() { Key = key, Name = variant.String("defaultName"),
                Members = alternatives.Order(StringComparer.Ordinal).Select(k => new CraftingGroupMember(k, "", "")).ToList() };
        }
        foreach (var record in records.Where(r => r.Enabled && r.Fields.TryGetProperty("Recipes", out _)))
        {
            var f = record.Fields; var types = Enums(f, "Recipes", "RecipeType"); if (types.Count == 0) continue;
            data.Stations.Add(new() { Name = record.Title, Types = types, Kind = f.Int("craftingKind"),
                Modifiers = Enums(f, "Modifiers", "RecipeCondition"), Efficiency = Number(f, "EquipmentQuality"),
                MaxUpgrades = f.Int("maxUpgrades"), Evidence = [record.Key], Furniture = record.Furniture,
                Locations = record.Scene.Length > 0 ? [record.Scene] : [] });
        }
        foreach (var record in records.Where(r => r.Enabled && r.Class == "FurnitureCrafting"))
            data.Stations.Add(new() { Name = record.Title, Types = ["Furniture"], Kind = -1, Evidence = [record.Key], Efficiency = 1,
                Furniture = record.Furniture, Locations = record.Scene.Length > 0 ? [record.Scene] : [] });
        data.Stations = data.Stations.GroupBy(s => JsonSerializer.Serialize(new { Name = CraftingDirectory.Normalize(s.Name), Types = s.Types.Order(), Modifiers = s.Modifiers.Order(), s.Efficiency, s.Kind, s.MaxUpgrades }))
            .Select(g => {
                var station = g.FirstOrDefault(s => s.Furniture) ?? g.First();
                station.Evidence = g.SelectMany(s => s.Evidence).Distinct().Order(StringComparer.Ordinal).ToList();
                station.Locations = g.SelectMany(s => s.Locations).Distinct().Order(StringComparer.Ordinal).ToList();
                return station;
            }).ToList();
        foreach (var record in records.Where(r => r.Enabled && r.Class == "CraftingUpgrade" && r.Fields.String("ID").Length > 0))
        {
            var f = record.Fields;
            data.Addons.Add(new() { Name = record.Title, Identity = f.String("ID"), Kind = f.Int("RequiredType"),
                RequiredTypes = Enums(f, "RequiredRecipes", "RecipeType"), AnyType = f.Bool("RequiredRecipesAny"),
                Modifiers = Enums(f, "AddModifiers", "RecipeCondition"), Efficiency = Number(f, "AddEquipmentQuality"),
                Range = Number(f, "MaxDistance"), Source = record.Key });
        }
        data.Addons = data.Addons.DistinctBy(a => JsonSerializer.Serialize(new { a.Name, a.Identity, a.Kind, a.RequiredTypes, a.AnyType, a.Modifiers, a.Efficiency, a.Range })).ToList();

        CraftingLine Line(string key, double amount, string note = "")
        {
            if (!data.Items.TryGetValue(key, out var item)) throw new InvalidDataException("Unknown crafting item " + key);
            return new() { Key = key, Amount = amount, Alternatives = item.Members.Select(m => m.Key).Distinct().ToList(), Note = note };
        }
        var recipeNodes = recipeJson.RootElement.GetProperty("Recipes").EnumerateArray().ToArray();
        var duplicateIds = recipeNodes.GroupBy(n => n.Int("ID")).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        int recipePosition = 0;
        foreach (var node in recipeNodes)
        {
            int position = recipePosition++;
            var recipe = new CraftingRecipe { Key = "recipe:" + node.Int("ID") + (duplicateIds.Contains(node.Int("ID")) ? ":" + position : ""), Name = node.String("Name"), Type = node.String("Type"),
                Required = node.Array("RequiredConditions").Select(x => x.GetString()!).Where(x => x != "None").ToList(),
                Excluded = node.Array("ExcludingConditions").Select(x => x.GetString()!).Where(x => x != "None").ToList(),
                Skill = node.String("RelevantSkill"), MinimumSkill = node.Int("MinimumSkill"), HardMinimumSkill = node.Bool("HardMinimumSkill"),
                BlueprintRequired = node.Bool("RecipeRequired"), StolenOnly = node.Bool("useOnlyStolenItems"),
                BaseSeconds = Number(node, "ProcessTime"), BaseFailureChance = Number(node, "FailChance"),
                Source = "StreamingAssets/Recipes.json / ID " + node.Int("ID") + " / entry " + position };
            if (node.TryGetProperty("metaSettings", out var meta) && meta.ValueKind == JsonValueKind.Object) recipe.LiquidContainers = meta.Bool("useLiquidContainers");
            foreach (var input in node.Array("input")) recipe.Inputs.Add(Line(ItemKey(input.GetProperty("Key")), input.GetProperty("Value").GetDouble()));
            var tags = new HashSet<string> { "" };
            foreach (var input in recipe.Inputs)
            {
                var group = data.Items[input.Key]; if (group.Members.Count == 0) continue;
                tags = tags.SelectMany(tag => tag.Length > 0 ? [tag] : group.Members.Select(m => m.OutputTag)).ToHashSet();
            }
            List<CraftingLine> Outputs(string property)
            {
                return node.Array(property).Select(output =>
                {
                    var line = Line(ItemKey(output.GetProperty("Key")), output.GetProperty("Value").GetDouble());
                    var members = data.Items[line.Key].Members;
                    if (members.Count > 0)
                    {
                        line.Alternatives = tags.Select(tag => members.FirstOrDefault(m => tag.Length > 0 && m.Tag == tag) ?? members[0]).Select(m => m.Key).Distinct().ToList();
                        line.Note = "The ingredient variant selects the product; unmatched or empty tags use the first alternative.";
                    }
                    return line;
                }).ToList();
            }
            recipe.Outputs = Outputs("output"); recipe.FailureOutputs = Outputs("outputFail");
            string variant = node.String("variantItemCategory");
            if (variant is not ("" or "None"))
            {
                var line = Line("variant:" + variant, Math.Max(1, node.Int("variantItemAmount")),
                    variant == "LiquidContainer" ? "Container requirement: contents, free capacity, and compatibility must match the output liquid. Containers are not ordinary ingredients." : "Additional variant ingredient required by this recipe.");
                recipe.Inputs.Add(line);
            }
            if (recipe.Name.Length == 0) recipe.Name = recipe.Outputs.Count > 0 ? data.Items[recipe.Outputs[0].Key].Name : recipe.Key;
            data.Recipes.Add(recipe);
        }
        foreach (var record in records.Where(r => r.Class == "TaskItemBlueprint"))
        {
            var f = record.Fields; string result = record.Pointer(f.GetProperty("result")); if (result.Length == 0) continue;
            var recipe = new CraftingRecipe { Key = "blueprint:" + record.Key, Name = data.Items["furniture:" + result].Name,
                Type = "Furniture", BlueprintRequired = true, Source = record.Key, Skill = Enum("PlayerStats+PlayerSkillType", f.Int("skill")),
                MinimumSkill = f.Int("minimumSkill"), BaseSeconds = Math.Max(1, Number(f, "processTime")) * 60 };
            foreach (var input in f.Array("resourceItems"))
            {
                string furniture = record.Pointer(input.GetProperty("furniture")), group = record.Pointer(input.GetProperty("furnitureGroup"));
                recipe.Inputs.Add(Line(furniture.Length > 0 ? "furniture:" + furniture : group.Length > 0 ? "furniture-group:" + group : ItemKey(input.GetProperty("item")), input.Int("amount")));
            }
            recipe.Outputs.Add(Line("furniture:" + result, Math.Max(1, f.Int("amount")))); data.Recipes.Add(recipe);
        }
        Validate(data); return data;
    }
    public static void Validate(CraftingCatalog data)
    {
        if (data.FormatVersion != CraftingCatalog.CurrentFormat || data.Items == null || data.Recipes == null || data.Stations == null || data.Addons == null
            || data.Items.Count == 0 || data.Recipes.Count == 0 || data.Recipes.Select(r => r.Key).Distinct().Count() != data.Recipes.Count)
            throw new InvalidDataException("Invalid crafting catalog.");
        foreach (var recipe in data.Recipes)
        {
            if (recipe.Inputs == null || recipe.Outputs == null || recipe.FailureOutputs == null || recipe.Required == null || recipe.Excluded == null
                || string.IsNullOrWhiteSpace(recipe.Key) || string.IsNullOrWhiteSpace(recipe.Name) || string.IsNullOrWhiteSpace(recipe.Type)
                || !double.IsFinite(recipe.BaseSeconds) || !double.IsFinite(recipe.BaseFailureChance))
                throw new InvalidDataException("Invalid crafting recipe.");
            foreach (var line in recipe.Inputs.Concat(recipe.Outputs).Concat(recipe.FailureOutputs))
                if (!double.IsFinite(line.Amount) || line.Amount < 0 || string.IsNullOrWhiteSpace(line.Key) || !data.Items.ContainsKey(line.Key)
                    || line.Alternatives == null || line.Alternatives.Any(k => !data.Items.ContainsKey(k)))
                    throw new InvalidDataException("Invalid crafting ingredient in " + recipe.Key);
        }
        foreach (var item in data.Items.Values)
            if (item.Members == null || item.Categories == null || item.Name == null
                || item.Members.Any(m => !data.Items.ContainsKey(m.Key)) || item.ContainedLiquid.Length > 0 && !data.Items.ContainsKey(item.ContainedLiquid))
                throw new InvalidDataException("Invalid ingredient group or liquid contents in " + item.Name);
        foreach (var station in data.Stations)
            if (station.Types == null || station.Modifiers == null || station.Evidence == null || station.Locations == null || !double.IsFinite(station.Efficiency))
                throw new InvalidDataException("Invalid crafting workstation.");
    }
}
