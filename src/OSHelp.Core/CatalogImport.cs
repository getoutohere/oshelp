using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OSHelp.Core;

public static class CatalogImport
{
    public static readonly string[] InputFiles = ["Managed/Assembly-CSharp.dll", "Managed/Assembly-CSharp-firstpass.dll", "globalgamemanagers.assets", "resources.assets", "sharedassets0.assets", "level0", "level2", "StreamingAssets/Items.json"];
    public static string FindRepository(string? from = null)
    {
        for (var dir = new DirectoryInfo(from ?? AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tools", "Inspect-Obenseuer.ps1"))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot locate OSHelp tools. Keep the executable inside the oshelp directory.");
    }
    public static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static async Task<Catalog> LoadAsync(string repository, string gameRoot, string dataDirectory, bool force, IProgress<string>? progress = null, CancellationToken cancel = default, GameFileValidation? validation = null)
    {
        Directory.CreateDirectory(dataDirectory);
        validation ??= new GameFileValidation(gameRoot, dataDirectory, force);
        var inputs = new List<InputHash>();
        foreach (var file in InputFiles)
        {
            cancel.ThrowIfCancellationRequested(); progress?.Report("Checking " + file);
            inputs.Add(new(file, await validation.HashAsync(file, cancel)));
        }
        // The data schemas are discovered, but executable rental/cost semantics are verified for this assembly.
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "docs/evidence/manifest.json")));
        var expected = baseline.RootElement.GetProperty("sourceHashes").EnumerateArray().Single(x => x.GetProperty("file").GetString() == "Managed/Assembly-CSharp.dll").GetProperty("sha256").GetString();
        if (inputs[0].Hash != expected) throw new InvalidDataException("The game's rental/cost code has changed. This prototype cannot certify its rules for that assembly. Your saved profiles have not been changed.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("catalog-v3\n" + string.Join("\n", inputs.Select(x => x.File + ":" + x.Hash)))));
        var cachedPath = Path.Combine(dataDirectory, "catalog.json");
        if (!force && File.Exists(cachedPath))
        {
            var cached = Json.Read<Catalog>(cachedPath);
            if (cached.Fingerprint == fingerprint) { cached.GameRoot = gameRoot; cached.FileValidation = validation; await TenantLocations.EnrichAsync(repository, gameRoot, dataDirectory, cached, progress, cancel); await RentalIncome.EnrichAsync(repository, gameRoot, dataDirectory, cached, progress, cancel); validation.Save(); progress?.Report("Verified cached game catalog and tenant locations"); return cached; }
        }
        var staging = Path.Combine(dataDirectory, "import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("Reading tenant and renovation definitions from game assets...");
            await RunScript(repository, "Inspect-Obenseuer.ps1", gameRoot, staging, cancel);
            progress?.Report("Reading apartments, difficulty presets, and recruitment dialogue...");
            await RunScript(repository, "Export-PlanningData.ps1", gameRoot, staging, cancel);
            progress?.Report("Verifying rental UI availability for apartment types...");
            await RunScript(repository, "Inspect-ApartmentUi.ps1", gameRoot, staging, cancel);
            cancel.ThrowIfCancellationRequested();
            var catalog = Build(staging, gameRoot);
            catalog.Fingerprint = fingerprint; catalog.AssemblyHash = inputs[0].Hash; catalog.Inputs = inputs; catalog.FileValidation = validation;
            await TenantLocations.EnrichAsync(repository, gameRoot, dataDirectory, catalog, progress, cancel);
            await RentalIncome.EnrichAsync(repository, gameRoot, dataDirectory, catalog, progress, cancel);
            Validate(catalog);
            AtomicFile.Write(cachedPath, JsonSerializer.Serialize(catalog, Json.Options));
            validation.Save();
            return catalog;
        }
        finally { try { if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(staging, true); } catch (IOException) { } }
    }
    internal static async Task RunScript(string repository, string script, string gameRoot, string output, CancellationToken cancel)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(repository, "tools", script), "-GameRoot", gameRoot, "-OutputDirectory", output }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the Windows PowerShell asset reader.");
        using var registration = cancel.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(cancel); var stderr = process.StandardError.ReadToEndAsync(cancel);
        await process.WaitForExitAsync(cancel);
        var error = await stderr; var log = await stdout;
        if (process.ExitCode != 0) throw new InvalidDataException("Game import failed: " + error + "\n" + log);
    }
    public static Catalog Build(string directory, string gameRoot)
    {
        var records = Json.Read<List<SourceRecord>>(Path.Combine(directory, "catalog.json"));
        var supplemental = Json.Read<List<SourceRecord>>(Path.Combine(directory, "planning.json"));
        var all = records.Concat(supplemental).ToDictionary(x => x.Location);
        SourceRecord? Resolve(SourceRecord owner, JsonElement pointer)
        {
            if (pointer.ValueKind != JsonValueKind.Object) return null;
            var id = pointer.GetProperty("pathId").GetInt64(); if (id == 0) return null;
            var fileId = pointer.Int("fileId");
            var file = fileId == 0 ? owner.SourceFile : Path.GetFileName(owner.ExternalFiles[fileId - 1]);
            return all.GetValueOrDefault(file + ":" + id) ?? throw new InvalidDataException($"Unresolved planning pointer {file}:{id} in {owner.Location}");
        }
        string Identity(SourceRecord r) => r.Fields.String("addressableAssetPath") is { Length: > 0 } path ? path : "type:" + r.Name;
        var catalog = new Catalog { GameRoot = gameRoot };
        using var enums = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "enums.json")));
        foreach (var e in enums.RootElement.EnumerateObject()) catalog.Enums[e.Name] = e.Value.EnumerateObject().ToDictionary(x => x.Value.GetInt32(), x => x.Name);
        using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameRoot, "Obenseuer_Data/StreamingAssets/Items.json")));
        foreach (var item in items.RootElement.EnumerateArray())
        {
            var id = item.Int("ID"); var key = ItemKey("", id); catalog.Items.Add(key, new(key, id, item.String("Title")));
        }
        foreach (var record in records.Where(x => x.Class == "TenementUpgrade"))
        {
            var f = record.Fields;
            catalog.Upgrades.Add(new() { Id = Identity(record), Name = record.Name, Source = record,
                StartOc = f.Int("priceStartOC"), FinalRm = f.Int("priceFinalRM"), Hours = f.Int("requiredTimeHours"),
                Effects = Categories.All.Where(c => f.Bool(c.Flag)).Select(c => new Effect(c.Key, f.Int(c.Value))).ToList(),
                Materials = f.Array("requiredItems").Select(l => new Material(ItemKey(l.GetProperty("Item").String("itemnamespace"), l.GetProperty("Item").Int("ID")), l.Int("itemAmount"))).ToList(),
                Prerequisites = f.Array("requiredUpgrades").Select(p => Identity(Resolve(record, p) ?? throw new InvalidDataException("Null prerequisite"))).ToList()
            });
        }
        List<string> Group(SourceRecord owner, JsonElement reference)
        {
            var group = Resolve(owner, reference); return group == null ? [] : group.Fields.Array("updateGroup").Select(p => Identity(Resolve(group, p) ?? throw new InvalidDataException("Null group upgrade"))).ToList();
        }
        foreach (var record in records.Where(x => x.Class == "TenementApartmentType"))
        {
            var type = new ApartmentType { Id = Identity(record), Name = record.Name.Replace("Apartment", ""), Size = record.Fields.Int("size") };
            foreach (var c in Categories.All.Where(x => !x.Building)) type.Upgrades[c.Key] = Group(record, record.Fields.GetProperty(c.Group));
            catalog.Types.Add(type);
        }
        using var apartmentUi = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "apartment-info.json")));
        foreach (var type in catalog.Types)
        {
            var ui = apartmentUi.RootElement.EnumerateArray().Single(r => r.String("hierarchy").Contains("Apartment Info -> Apartment " + type.Name + " ->", StringComparison.Ordinal));
            type.HasRentalUi = ui.GetProperty("rentUI").GetProperty("pathId").GetInt64() != 0;
        }
        foreach (var record in records.Where(x => x.Class == "TenementResident"))
        {
            var f = record.Fields; var npc = f.GetProperty("resident");
            catalog.Tenants.Add(new() { Id = Identity(record), Name = record.Name, NpcId = npc.Int("ID"), Source = record,
                Sizes = new[] { "smallApartment", "mediumApartment", "largeApartment" }.Select((key, i) => (key, i)).Where(x => f.Bool(x.key)).Select(x => x.i).ToList(),
                Household = f.Array("otherResidents").Select(x => new Npc(x.Int("ID"), x.String("name"))).ToList(),
                Requirements = Categories.All.Where(c => c.Resident != null).Select(c => new Requirement(c.Key, f.Int(c.Resident!))).ToList(),
                IncomeOc = f.Int("incomeOc"), IncomeRm = f.Int("incomeRm"), DisableEviction = f.Bool("disableEvictButton"), NoUpgrade = f.Bool("cantUpgradeApartment"), DialogueActor = f.String("dialogueActor")
            });
        }
        var controller = supplemental.Single(x => x.Class == "TenementController" && x.SourceFile == "level2");
        var general = controller.Fields.GetProperty("general");
        foreach (var category in Categories.All.Where(x => x.Building))
        {
            catalog.BuildingUpgrades[category.Key] = Group(controller, general.GetProperty(category.Group));
            catalog.InitialBuildingLevels[category.Key] = general.Int(category.State);
        }
        foreach (var a in controller.Fields.Array("apartments"))
        {
            var tenantRecord = Resolve(controller, a.GetProperty("resident"));
            var apt = new ApartmentDefinition { Number = a.Int("apartmentNumber"), Floor = a.Int("apartmentFloor"), TypeId = Identity(Resolve(controller, a.GetProperty("type"))!), InitialTenant = tenantRecord == null ? null : Identity(tenantRecord) };
            foreach (var c in Categories.All.Where(x => !x.Building)) apt.InitialLevels[c.Key] = a.Int(c.State);
            catalog.Apartments.Add(apt);
            if (tenantRecord != null) catalog.Tenants.Single(x => x.Id == apt.InitialTenant).InitialApartment = apt.Number;
        }
        catalog.Apartments = catalog.Apartments.OrderBy(x => x.Number).ToList();
        foreach (var record in records.Where(x => x.Class == "TenementContractor"))
        {
            catalog.Contractors.Add(new() { Name = record.Name, ApartmentLimit = record.Fields.Int("maxApartmentUpgrade"), ShopLimit = record.Fields.Int("maxShopUpgrade"),
                Limits = Categories.All.Where(x => x.Building).ToDictionary(c => c.Key, c => record.Fields.Int(c.Contractor)) });
        }
        var difficulty = supplemental.Single(x => x.Class == "DifficultyController" && x.SourceFile == "level0");
        foreach (var preset in difficulty.Fields.Array("difficulties"))
            catalog.Presets.Add(new(preset.String("difficultyName"), GameRules.Multiplier(preset.Int("renovationResourceCosts")), GameRules.Multiplier(preset.Int("renovationCost")), GameRules.Multiplier(preset.Int("renovationSpeed"))));
        var panel = supplemental.Single(x => x.Class == "TenementPanelUI" && x.SourceFile == "level2");
        catalog.PermissionItem = Resolve(panel, panel.Fields.GetProperty("requiredTaskItem"))?.Fields.String("itemName") ?? throw new InvalidDataException("Rental task item not resolved");
        foreach (var db in supplemental.Where(x => x.Class.EndsWith("DialogueDatabase")))
        foreach (var conversation in db.Fields.Array("conversations"))
        {
            var entries = conversation.Array("entries").ToList();
            var conversationTenantIds = entries.SelectMany(e => e.Array("rentTargets")).Select(p => Resolve(db, p)).Where(r => r != null).Select(r => Identity(r!)).ToHashSet();
            foreach (var tenant in catalog.Tenants.Where(t => conversationTenantIds.Contains(t.Id)))
            {
                tenant.RecruitmentContext += "\r\nCONVERSATION: " + conversation.String("title") + " [" + conversation.Int("id") + "]\r\n" +
                    string.Join("\r\n\r\n", entries.Select(e => $"Entry {e.Int("id")}: {e.String("text")}\r\nCondition: {e.String("conditions")}\r\nScript: {e.String("script")}\r\nNext: " + string.Join(", ", e.Array("links").Select(l => $"{l.Int("destinationConversationID")}:{l.Int("destinationDialogueID")}"))));
            }
            foreach (var entry in entries)
            {
                var targets = entry.Array("rentTargets").Select(x => Resolve(db, x)).Where(x => x != null).Select(x => Identity(x!)).ToHashSet();
                foreach (var tenant in catalog.Tenants.Where(x => targets.Contains(x.Id)))
                {
                    var incoming = entries.Where(e => e.Array("links").Any(l => l.Int("destinationDialogueID") == entry.Int("id") && l.Int("destinationConversationID") == conversation.Int("id"))).Select(e => e.Int("id")).ToList();
                    tenant.Recruitment.Add(new(conversation.String("title"), entry.Int("id"), int.TryParse(entry.String("actor"), out var actor) ? actor : 0, entry.String("text"), entry.String("conditions"), entry.String("script"), incoming, $"{db.Location}; conversation {conversation.Int("id")}; entry {entry.Int("id")}"));
                    // Preserve the whole relevant conversation, including predecessor conditions, for source inspection.
                }
            }
        }
        return catalog;
    }
    public static string ItemKey(string ns, int id) => (string.IsNullOrWhiteSpace(ns) ? "game" : ns) + ":" + id;
    public static void Validate(Catalog catalog)
    {
        if (catalog.Tenants.Count == 0 || catalog.Upgrades.Count == 0 || catalog.Apartments.Count == 0) throw new InvalidDataException("Incomplete game catalog");
        if (catalog.Tenants.Select(x => x.Id).Distinct().Count() != catalog.Tenants.Count || catalog.Upgrades.Select(x => x.Id).Distinct().Count() != catalog.Upgrades.Count) throw new InvalidDataException("Duplicate stable game IDs");
        foreach (var u in catalog.Upgrades)
        {
            if (u.Effects.Count == 0) throw new InvalidDataException("Upgrade has no effect: " + u.Name);
            foreach (var line in u.Materials) if (!catalog.Items.ContainsKey(line.ItemKey) || line.Amount < 0) throw new InvalidDataException("Invalid material in " + u.Name);
            foreach (var p in u.Prerequisites) _ = catalog.Upgrade(p);
        }
        var done = new HashSet<string>(); var visiting = new HashSet<string>();
        void Visit(Upgrade u) { if (done.Contains(u.Id)) return; if (!visiting.Add(u.Id)) throw new InvalidDataException("Cyclic upgrade prerequisites"); foreach (var p in u.Prerequisites) Visit(catalog.Upgrade(p)); visiting.Remove(u.Id); done.Add(u.Id); }
        foreach (var u in catalog.Upgrades) Visit(u);
        foreach (var a in catalog.Apartments) _ = catalog.ApartmentType(a.Number);
    }
}
