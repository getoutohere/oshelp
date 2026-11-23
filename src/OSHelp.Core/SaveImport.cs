using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OSHelp.Core;

public sealed record SaveSlot(string Directory, string Character, string Name, DateTimeOffset Date, string Version);
public sealed record SaveStockSource(string Id, string Name, Dictionary<string, decimal> Items);
public sealed class SaveProvenance
{
    public string Directory { get; set; } = "";
    public string Character { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTimeOffset SaveDate { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public List<InputHash> Files { get; set; } = [];
    public List<string> StockSources { get; set; } = [];
    public List<string>? StockSourceIds { get; set; }
}
public sealed record SaveImportResult(Profile Profile, SaveSlot Slot, IReadOnlyList<SaveStockSource> StockSources, IReadOnlyList<string> Notes)
{
    public Profile CreateProfile(IEnumerable<string> sources)
    {
        var profile = ProfileStore.Clone(Profile);
        var selected = sources.ToHashSet(StringComparer.Ordinal);
        if (selected.Except(StockSources.Select(s => s.Id)).Any()) throw new InvalidDataException("Unknown stock source");
        var totals = new Dictionary<string, decimal>();
        foreach (var source in StockSources.Where(s => selected.Contains(s.Id)))
        foreach (var item in source.Items) totals[item.Key] = checked(totals.GetValueOrDefault(item.Key) + item.Value);
        profile.Stock = totals.ToDictionary(x => x.Key, x => checked((long)decimal.Floor(x.Value)));
        profile.ImportedSave!.StockSources = StockSources.Where(s => selected.Contains(s.Id)).Select(s => s.Name).ToList();
        profile.ImportedSave.StockSourceIds = selected.Order(StringComparer.Ordinal).ToList();
        ProfileStore.Validate(profile); return profile;
    }
    public string Summary(Catalog catalog) => $"{Slot.Character.Replace('_', ' ')} / {Slot.Name}\r\nSaved: {Slot.Date.LocalDateTime:g} / Game {Slot.Version}\r\n\r\n" +
        $"{Profile.Apartments.Count} apartments / {Profile.BuildingLevels.Count} building facilities\r\n{Profile.Apartments.Count(a => !string.IsNullOrEmpty(a.Occupant) && !a.OnlyFurniture)} occupied apartments\r\n" +
        $"{Profile.Jobs.Count(j => !j.ReadyForPayment)} renovations underway / {Profile.Jobs.Count(j => j.ReadyForPayment)} awaiting final payment\r\n" +
        $"Materials ×{Profile.ResourceMultiplier:g} / Money ×{Profile.MoneyMultiplier:g} / Speed ×{Profile.SpeedMultiplier:g}\r\nTenement blueprint: {(Profile.HasRentalPermission == true ? "Owned" : "Not owned")}\r\n\r\n" +
        string.Join("\r\n", Notes) + "\r\n\r\nImport uses profile name: " + Profile.Name + ". An existing name requires overwrite confirmation. Game saves are preserved.";
}

public static class SaveImport
{
    public static string DefaultSaveDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Loiste Interactive", "Obenseuer", "Saves");
    public static IReadOnlyList<SaveSlot> Discover(string? root = null)
    {
        root ??= DefaultSaveDirectory;
        if (!Directory.Exists(root)) return [];
        var slots = new List<SaveSlot>();
        foreach (var character in Directory.EnumerateDirectories(root))
        foreach (var directory in Directory.EnumerateDirectories(character))
        {
            var info = Path.Combine(directory, "Info.tnmt");
            if (!File.Exists(info) || !File.Exists(Path.Combine(directory, "Globals.tnmt"))) continue;
            try { using var json = Parse(File.ReadAllBytes(info)); slots.Add(Slot(directory, json.RootElement)); }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or FormatException) { }
        }
        return slots.OrderByDescending(s => s.Date).ToArray();
    }
    static SaveSlot Slot(string directory, JsonElement header) => new(directory, RequiredString(header, "CharacterName"), RequiredString(header, "SaveName"), DateTimeOffset.Parse(RequiredString(header, "Date"), CultureInfo.InvariantCulture), RequiredString(header, "ApplicationVersion"));
    static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes.AsMemory(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0), new JsonDocumentOptions { MaxDepth = 256 });
    static string RequiredString(JsonElement value, string name) => value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : throw new InvalidDataException("Save is missing " + name);

    public static SaveImportResult Read(string path, Catalog catalog)
    {
        try { return ReadSnapshot(path, catalog); }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("This save does not match the supported save schema: " + e.Message, e); }
    }
    static SaveImportResult ReadSnapshot(string path, Catalog catalog)
    {
        var directory = Path.GetFullPath(File.Exists(path) ? Path.GetDirectoryName(path)! : path);
        var bytes = new Dictionary<string, byte[]>(); var times = new Dictionary<string, (long Length, DateTime Time)>();
        foreach (var name in new[] { "Info.tnmt", "Globals.tnmt", "Globals.dialogue" })
        {
            var file = Path.Combine(directory, name);
            if (!File.Exists(file)) { if (name == "Globals.dialogue") continue; throw new InvalidDataException("Select a save slot containing Info.tnmt and Globals.tnmt."); }
            var before = new FileInfo(file);
            if (before.Length > 128 * 1024 * 1024) throw new InvalidDataException("Save file exceeds the supported size: " + name);
            times[name] = (before.Length, before.LastWriteTimeUtc);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); bytes[name] = buffer.ToArray();
        }
        using var info = Parse(bytes["Info.tnmt"]); using var globals = Parse(bytes["Globals.tnmt"]);
        var slot = Slot(directory, info.RootElement); var globalSlot = Slot(directory, globals.RootElement);
        if (slot.Character != globalSlot.Character || slot.Name != globalSlot.Name) throw new InvalidDataException("Save metadata and globals belong to different characters or slots.");
        // Info and Globals get independent DateTime.Now values in the game. Globals dates the actual state.
        slot = globalSlot;
        var resolver = new References(globals.RootElement);
        var records = globals.RootElement.GetProperty("Data").EnumerateArray().ToDictionary(r => RequiredString(r, "GUID"), r => resolver.Resolve(r.GetProperty("Data")), StringComparer.Ordinal);
        JsonElement Record(string guid) => records.TryGetValue(guid, out var r) ? r : throw new InvalidDataException("Save has no " + guid + " record");
        var tenement = Record("TenementController"); var profile = Profile.Create(catalog, slot.Character.Replace('_', ' ') + " / " + slot.Name);
        profile.ImportedSave = new() { Directory = directory, Character = slot.Character, Slot = slot.Name, Version = slot.Version, SaveDate = slot.Date, ImportedAt = DateTimeOffset.UtcNow,
            Files = bytes.Select(x => new InputHash(x.Key, Convert.ToHexString(SHA256.HashData(x.Value)))).ToList() };
        var notes = new List<string>();
        var general = resolver.Resolve(tenement.GetProperty("general"));
        void CurrentIds(JsonElement owner, Dictionary<string, string> ids, bool building)
        {
            foreach (var category in Categories.All.Where(c => c.Building == building))
            {
                if (!owner.TryGetProperty(RentalIncome.UpgradeField(category), out var pointer)) continue;
                var upgrade = resolver.Resolve(pointer);
                var id = upgrade.ValueKind == JsonValueKind.Object ? upgrade.String("addressableAssetPath") : "";
                if (id.Length > 0) ids[category.Key] = catalog.Upgrade(id).Id;
            }
        }
        CurrentIds(general, profile.BuildingCurrentUpgradeIds, true);
        var legacyEnumValues = new Dictionary<(string Enum, string Name), int>();
        int Tier(JsonElement owner, Category category)
        {
            if (!owner.TryGetProperty(category.State, out var value)) throw new InvalidDataException("Save is missing facility " + category.State);
            // These two state fields really are TenementElevator in the installed assembly,
            // although their upgrade target fields use Fence/Greenhouse enums.
            var enumName = category.Key is "fence" or "greenhouse" ? "TenementElevator" : category.Enum;
            var enums = catalog.Enums["UpgradeType+" + enumName];
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && enums.ContainsKey(number)) return number;
            if (value.ValueKind == JsonValueKind.String)
            {
                var match = enums.Where(e => e.Value == value.GetString()).ToArray();
                if (match.Length == 1) return match[0].Key;
                if (legacyEnumValues.TryGetValue((enumName, value.GetString()!), out var known)) return known;
                var field = category.State.Replace("currentent", "current", StringComparison.Ordinal) + "Upgrade";
                if (enumName == "TenementElevator" && value.GetString() == "Off" && Version.TryParse(slot.Version, out var oldVersion) && oldVersion < new Version(0, 5) && owner.TryGetProperty(field, out var savedCurrent))
                {
                    var path = resolver.Resolve(savedCurrent).ValueKind == JsonValueKind.Object ? resolver.Resolve(savedCurrent).String("addressableAssetPath") : "";
                    var installed = catalog.Upgrades.SingleOrDefault(u => u.Id == path)?.Effects.SingleOrDefault(e => e.Category == category.Key);
                    if (installed?.Tier == 0) { legacyEnumValues[(enumName, value.GetString()!)] = installed.Tier; notes.Add($"Mapped legacy {category.Name} '{value}' through saved asset {path} to {catalog.TierName(category.Key, installed.Tier)}."); return installed.Tier; }
                }
            }
            throw new InvalidDataException($"The saved {category.Name} value '{value}' is not supported by this installed game catalog.");
        }
        foreach (var category in Categories.All.Where(c => c.Building)) profile.BuildingLevels[category.Key] = Tier(general, category);
        var importedNumbers = new HashSet<int>();
        void Progresses(JsonElement owner, int number)
        {
            foreach (var property in owner.EnumerateObject().Where(p => p.Name.EndsWith("Progress", StringComparison.Ordinal)))
            {
                var progress = resolver.Resolve(property.Value); if (progress.ValueKind == JsonValueKind.Null) continue;
                var savedUpgrade = resolver.Resolve(progress.GetProperty("upgrade"));
                var id = RequiredString(savedUpgrade, "addressableAssetPath"); var upgrade = catalog.Upgrade(id);
                var scope = upgrade.Building ? 0 : number;
                if (!catalog.Applicable(upgrade, scope) || progress.GetProperty("generalUpgrade").GetBoolean() != upgrade.Building || (!upgrade.Building && progress.GetProperty("apartmentNumber").GetInt32() != number)) throw new InvalidDataException("Saved renovation scope does not match " + id);
                // The game reloads ScriptableObjects by asset path. Its installed definition is authoritative.
                var contractor = resolver.Resolve(progress.GetProperty("contractor"));
                var job = new Job { Key = Planner.Key(scope, id), UpgradeId = id, Apartment = scope, Status = JobStatus.UnderConstruction,
                    ReadyForPayment = progress.GetProperty("upgradeReady").GetBoolean(), RemainingSeconds = progress.GetProperty("timeSecondsLeft").GetInt32(), Contractor = contractor.ValueKind == JsonValueKind.Object ? contractor.String("contractorName") : "" };
                if (profile.Jobs.Any(j => j.Key == job.Key)) throw new InvalidDataException("Duplicate saved renovation " + id);
                profile.Jobs.Add(job);
            }
        }
        Progresses(general, 0);
        foreach (var saved in tenement.GetProperty("apartments").EnumerateArray().Select(resolver.Resolve))
        {
            int number = saved.GetProperty("apartmentNumber").GetInt32();
            if (!importedNumbers.Add(number) || !catalog.Apartments.Any(a => a.Number == number)) throw new InvalidDataException("Unknown or duplicate saved apartment " + number);
            var apartment = profile.Apartment(number);
            foreach (var category in Categories.All.Where(c => !c.Building)) apartment.Levels[category.Key] = Tier(saved, category);
            apartment.Examined = saved.GetProperty("examined").GetBoolean(); apartment.OnlyFurniture = saved.GetProperty("onlyFurniture").GetBoolean();
            apartment.SavedUnderConstruction = saved.GetProperty("underConstruction").GetBoolean();
            apartment.MovingIn = saved.Bool("moveStarted"); CurrentIds(saved, apartment.CurrentUpgradeIds, false);
            apartment.PrimaryCleared = Clearing(saved, "Primary"); apartment.SecondaryCleared = Clearing(saved, "Secondary");
            var resident = resolver.Resolve(saved.GetProperty("resident")); apartment.Occupant = "";
            if (resident.ValueKind == JsonValueKind.Object)
            {
                var id = resident.String("addressableAssetPath"); var npc = resolver.Resolve(resident.GetProperty("resident")).GetProperty("ID").GetInt32();
                var tenant = catalog.Tenants.SingleOrDefault(t => t.Id == id) ?? catalog.Tenants.SingleOrDefault(t => t.NpcId == npc) ?? throw new InvalidDataException("Unknown saved resident ID " + npc);
                if ((number == 1 || !catalog.ApartmentType(number).HasRentalUi) && !apartment.OnlyFurniture) throw new InvalidDataException("The save assigns a tenant to a non-rental apartment.");
                apartment.Occupant = tenant.Id;
                if (!apartment.OnlyFurniture) profile.Recruitment[tenant.Id] = true;
                apartment.CanRenovate = tenant.DialogueActor.Length == 0 ? !tenant.NoUpgrade : null;
            }
            Progresses(saved, number);
        }
        if (!importedNumbers.SetEquals(catalog.Apartments.Select(a => a.Number))) throw new InvalidDataException("The save's apartment list differs from the installed catalog.");
        var difficulty = Record("DifficultyController"); int index = difficulty.GetProperty("currentDifficultyIndex").GetInt32();
        profile.RentalIncomeMultiplier = RentalIncome.SavedMultiplier(difficulty, catalog);
        profile.RentPaymentMode = RentalIncome.SavedPaymentMode(difficulty, catalog);
        if (index < 0 || index >= catalog.Presets.Count) throw new InvalidDataException("Unknown saved difficulty index " + index);
        var preset = catalog.Presets[index];
        if (preset.Name == "Custom")
        {
            var settings = resolver.Resolve(difficulty.GetProperty("customDifficultySettings"));
            profile.ResourceMultiplier = Multiplier(settings.GetProperty("renovationResourceCosts"), catalog);
            profile.MoneyMultiplier = Multiplier(settings.GetProperty("renovationCost"), catalog);
            profile.SpeedMultiplier = Multiplier(settings.GetProperty("renovationSpeed"), catalog);
        }
        else { profile.ResourceMultiplier = preset.Materials; profile.MoneyMultiplier = preset.Money; profile.SpeedMultiplier = preset.Speed; }
        profile.IgnoreCosts = tenement.GetProperty("ignoreUpgradePricesAndItems").GetBoolean();
        // PlayerHasRequiredItem requests amount 0, which skips the saved amount comparison entirely.
        profile.HasRentalPermission = Record("TaskItemsManager").GetProperty("taskItems").EnumerateArray().Select(resolver.Resolve).Any(entry =>
        {
            var task = resolver.Resolve(entry.GetProperty("taskItem"));
            return task.String("id") == "TenementBlueprint31";
        });
        if (bytes.TryGetValue("Globals.dialogue", out var dialogue))
        {
            var state = SavedLua.Read(Encoding.UTF8.GetString(dialogue).TrimStart('\uFEFF'));
            foreach (var apartment in profile.Apartments)
            {
                var tenant = catalog.Tenants.SingleOrDefault(t => t.Id == apartment.Occupant);
                if (tenant?.DialogueActor.Length > 0)
                    apartment.CanRenovate = SavedLua.ActorBoolean(state, tenant.DialogueActor, "CanUpgradeApartment");
            }
        }
        notes.Add("Imported facility levels, occupancy, clearing, permission item, difficulty, and active renovations.");
        notes.Add("Renovations awaiting final payment remain unfinished; their materials have already been consumed.");
        notes.Add("Recruitment is confirmed for current tenants. Unrented applicants still require confirmation of their dialogue/quest gates.");
        var sources = new List<SaveStockSource>();
        var materials = catalog.Upgrades.SelectMany(u => u.Materials).Select(m => m.ItemKey).ToHashSet();
        Dictionary<string, decimal> Items(IEnumerable<JsonElement> slots)
        {
            var items = new Dictionary<string, decimal>(); var seen = new HashSet<string>();
            foreach (var slotValue in slots)
            {
                var slotData = resolver.Resolve(slotValue); if (slotData.ValueKind == JsonValueKind.Null) continue;
                var stack = resolver.Resolve(slotData.GetProperty("itemStack")); if (stack.ValueKind == JsonValueKind.Null) continue;
                // A reference may appear in several serialized collections; count the actual stack once.
                if (stack.TryGetProperty("$id", out var refId) && !seen.Add(refId.GetString()!)) continue;
                int id = stack.GetProperty("itemId").GetInt32(), amount = stack.GetProperty("itemAmount").GetInt32();
                if (amount < 0) throw new InvalidDataException("Negative saved item amount");
                var key = CatalogImport.ItemKey(stack.String("itemnamespace"), id);
                if (id >= 0 && amount > 0 && materials.Contains(key)) items[key] = checked(items.GetValueOrDefault(key) + amount);
            }
            return items;
        }
        var inventory = Record("Inventory");
        sources.Add(new("inventory", "Carried inventory + equipped items", Items(inventory.GetProperty("_savingSlots").EnumerateArray().Concat(inventory.GetProperty("_savingCharacterSlots").EnumerateArray()))));
        foreach (var name in new[] { "Backpack", "Rucksack", "CoolerBag" })
            if (records.TryGetValue(name, out var bag) && bag.TryGetProperty("Slots", out var slots) && slots.ValueKind == JsonValueKind.Array) sources.Add(new(name, name + " contents", Items(slots.EnumerateArray())));
        if (tenement.TryGetProperty("resourceStorageSnapshotsByContractor", out var snapshots))
        foreach (var source in snapshots.EnumerateObject())
        {
            var items = new Dictionary<string, decimal>();
            var entries = resolver.Resolve(source.Value);
            foreach (var item in entries.EnumerateArray().Select(resolver.Resolve))
            {
                var key = CatalogImport.ItemKey("", item.GetProperty("Key").GetInt32()); var quantity = item.GetProperty("Value").GetDecimal();
                if (quantity < 0) throw new InvalidDataException("Negative contractor stock");
                if (materials.Contains(key)) items[key] = checked(items.GetValueOrDefault(key) + quantity);
            }
            var name = catalog.Contractors.FirstOrDefault(c => c.Name.Replace(" ", "") == source.Name)?.Name ?? source.Name;
            sources.Add(new("contractor:" + source.Name, name + " storage snapshot", items));
        }
        notes.Add("Choose stock sources to include. Contractor snapshots are separate depots; transferring materials may be necessary. Other world containers are excluded.");
        if (sources.SelectMany(s => s.Items.Values).Any(n => n != decimal.Floor(n))) notes.Add("Fractional stock is combined before flooring to whole usable units.");
        foreach (var file in times)
        {
            var after = new FileInfo(Path.Combine(directory, file.Key));
            if (after.Length != file.Value.Length || after.LastWriteTimeUtc != file.Value.Time) throw new IOException("The game changed this save during import. Wait for saving to finish and retry.");
        }
        ProfileStore.Validate(profile);
        return new(profile, slot, sources, notes);
    }
    static int Clearing(JsonElement apartment, string suffix)
    {
        int initial = apartment.GetProperty("preplacedFurnitureAmount" + suffix).GetInt32(), current = apartment.GetProperty("currentFurnitureAmount" + suffix).GetInt32();
        if (initial < 0 || current < 0) throw new InvalidDataException("Negative saved furniture count");
        return initial == 0 ? 100 : 100 - checked((int)((float)current / (float)initial * 100f));
    }
    static float Multiplier(JsonElement value, Catalog catalog)
    {
        int id;
        if (value.ValueKind == JsonValueKind.Number) id = value.GetInt32();
        else
        {
            var enums = catalog.Enums["DefaultMultiplier"];
            var match = enums.Where(x => x.Value == value.GetString()).ToArray();
            if (match.Length != 1) throw new InvalidDataException("Unknown saved multiplier " + value);
            id = match[0].Key;
        }
        return GameRules.Multiplier(id);
    }
    internal sealed class References
    {
        readonly Dictionary<string, JsonElement> ids = new(StringComparer.Ordinal);
        public References(JsonElement root) { Index(root); }
        void Index(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("$id", out var id) && !ids.TryAdd(id.GetString()!, value)) throw new InvalidDataException("Duplicate save reference ID " + id);
                foreach (var field in value.EnumerateObject()) Index(field.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var element in value.EnumerateArray()) Index(element);
        }
        public JsonElement Resolve(JsonElement value)
        {
            var seen = new HashSet<string>();
            while (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var id))
            {
                if (!seen.Add(id.GetString()!) || !ids.TryGetValue(id.GetString()!, out value)) throw new InvalidDataException("Unresolved or cyclic save reference " + id);
            }
            return value;
        }
    }
}

// Parses the saved Lua data tables only. No game or user-provided code is executed.
internal sealed class SavedLua
{
    readonly string text; int position;
    SavedLua(string text) { this.text = text; }
    public static Dictionary<string, object?> Read(string text)
    {
        var reader = new SavedLua(text); var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Skip()) { var key = reader.Identifier(); reader.Take('='); result.Add(key, reader.Value(0)); reader.Skip(); if (reader.position < text.Length && text[reader.position] == ';') reader.position++; }
        return result;
    }
    public static bool ActorBoolean(Dictionary<string, object?> state, string actor, string field)
    {
        if (!state.TryGetValue("Actor", out var value) || value is not Dictionary<string, object?> actors) return false;
        var entry = actors.Values.OfType<Dictionary<string, object?>>().FirstOrDefault(v => v.GetValueOrDefault("Name") as string == actor)
            ?? (actors.GetValueOrDefault(actor.Replace('/', '_').Replace(' ', '_')) as Dictionary<string, object?>);
        return entry?.GetValueOrDefault(field) is true;
    }
    bool Skip() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; return position < text.Length; }
    void Take(char c) { if (!Skip() || text[position++] != c) throw new InvalidDataException("Unsupported saved dialogue table near character " + position); }
    string Identifier() { Skip(); int start = position; while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++; if (start == position) throw new InvalidDataException("Invalid saved dialogue key"); return text[start..position]; }
    object? Value(int depth)
    {
        if (depth > 64 || !Skip()) throw new InvalidDataException("Invalid saved dialogue table depth");
        char c = text[position];
        if (c is '"' or '\'')
        {
            position++; var result = new StringBuilder(); bool closed = false;
            while (position < text.Length) { char ch = text[position++]; if (ch == c) { closed = true; break; } if (ch == '\\') { if (position == text.Length) break; ch = text[position++]; ch = ch switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => ch }; } result.Append(ch); }
            return closed ? result.ToString() : throw new InvalidDataException("Unclosed saved dialogue string");
        }
        if (c == '{')
        {
            position++; var table = new Dictionary<string, object?>(StringComparer.Ordinal); int index = 1;
            while (Skip() && text[position] != '}')
            {
                string key;
                if (text[position] == '[') { position++; key = Convert.ToString(Value(depth + 1), CultureInfo.InvariantCulture)!; Take(']'); Take('='); }
                else
                {
                    int start = position; key = Identifier(); Skip();
                    if (position < text.Length && text[position] == '=') position++;
                    else { position = start; key = (index++).ToString(CultureInfo.InvariantCulture); }
                }
                table.Add(key, Value(depth + 1)); Skip(); if (position < text.Length && text[position] is ',' or ';') position++; else break;
            }
            Take('}'); return table;
        }
        int tokenStart = position; while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not (',' or ';' or '}' or ']')) position++;
        var token = text[tokenStart..position];
        return token switch { "true" => true, "false" => false, "nil" => null, _ => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new InvalidDataException("Unsupported saved dialogue value " + token) };
    }
}
