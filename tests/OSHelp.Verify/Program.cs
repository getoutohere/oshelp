using System.Text.Json;
using System.Runtime.InteropServices;
using OSHelp.Core;

var repository = CatalogImport.FindRepository();
var gameRoot = Directory.GetParent(repository)!.FullName;
var scratch = Path.Combine(repository, "local/verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new Exception("FAILED: " + message); }
void Throws<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { assertions++; return; } throw new Exception("FAILED: " + message); }

if (args.Contains("--benchmark-startup"))
{
    await StartupBenchmark.Run(repository, gameRoot);
    return 0;
}

if (args.Contains("--build-vendors"))
{
    var directory = Path.Combine(repository, "local/vendor-research");
    var rows = Json.Read<List<VendorRecord>>(Path.Combine(directory, "vendor-records.json"));
    if (File.Exists(Path.Combine(directory, "vendor-extra-records.json"))) rows.AddRange(Json.Read<List<VendorRecord>>(Path.Combine(directory, "vendor-extra-records.json")));
    if (File.Exists(Path.Combine(directory, "vendor-labels.json"))) rows.AddRange(Json.Read<List<VendorRecord>>(Path.Combine(directory, "vendor-labels.json")));
    AtomicFile.Write(Path.Combine(directory, "vendor-focus.json"), JsonSerializer.Serialize(rows.Where(r => r.Class != "Relay"), Json.Options));
    var relayKeys = rows.Where(r => r.Class == "RelayWeekdays").SelectMany(r => new[] { "onOpen", "onClosed" }.Select(f => r.Pointer(r.Fields.GetProperty(f)))).ToHashSet();
    AtomicFile.Write(Path.Combine(directory, "schedule-relays.json"), JsonSerializer.Serialize(rows.Where(r => relayKeys.Contains(r.Key)), Json.Options));
    var inputs = File.Exists(Path.Combine(directory, "vendor-inputs.json")) ? Json.Read<List<InputHash>>(Path.Combine(directory, "vendor-inputs.json")) : new List<InputHash>();
    var vendors = VendorImport.Build(rows, inputs, Json.Read<List<JsonElement>>(Path.Combine(directory, "vendor-scan.json")).Count);
    AtomicFile.Write(Path.Combine(directory, "vendors.json"), JsonSerializer.Serialize(vendors, Json.Options));
    foreach (var v in vendors.Vendors) Console.WriteLine($"{v.Id} | {v.Name} | {v.Operator} | {v.Location} | Sat trade: {v.Trading?.Format(5)} | entrance: {v.EntranceHours?.Format(5)} | usable: {v.Accessible?.Format(5)} | {string.Join("; ", v.Notes)}");
    Console.WriteLine($"{vendors.Vendors.Count} vendors, {vendors.FilesScanned} scanned files.");
    return 0;
}

if (args.Contains("--inspect-saves"))
{
    var installed = Json.Read<Catalog>(Path.Combine(repository, "local/userdata/catalog.json"));
    installed.RentalSettings = Json.Read<RentalSettings>(Path.Combine(repository, "docs/evidence/rental-settings.json"));
    foreach (var slot in SaveImport.Discover())
    {
        try { var save = SaveImport.Read(slot.Directory, installed); Console.WriteLine($"PASS {slot.Character}/{slot.Name} {slot.Version}: {save.Profile.Apartments.Count} apartments, {save.Profile.Jobs.Count} active jobs, {save.StockSources.Count} stock sources"); }
        catch (Exception e) { Console.WriteLine($"FAIL {slot.Character}/{slot.Name} {slot.Version}: {e.Message}"); }
    }
    return 0;
}

try
{
    Console.WriteLine("Runtime: " + RuntimeInformation.FrameworkDescription);
    var import = Path.Combine(repository, "local/planning-import");
    Directory.CreateDirectory(import);
    File.Copy(Path.Combine(repository, "docs/evidence/catalog.json"), Path.Combine(import, "catalog.json"), true);
    File.Copy(Path.Combine(repository, "docs/evidence/enums.json"), Path.Combine(import, "enums.json"), true);
    if (!File.Exists(Path.Combine(import, "apartment-info.json"))) File.Copy(Path.Combine(repository, "docs/evidence/apartment-info.json"), Path.Combine(import, "apartment-info.json"));
    var catalog = CatalogImport.Build(import, gameRoot); catalog.Fingerprint = "verification-baseline";
    catalog.RentalSettings = Json.Read<RentalSettings>(Path.Combine(repository, "docs/evidence/rental-settings.json"));
    CatalogImport.Validate(catalog);
    var locationPath = File.Exists(Path.Combine(import, "tenant-locations.json")) ? Path.Combine(import, "tenant-locations.json") : Path.Combine(repository, "docs/evidence/tenant-locations.json");
    TenantLocations.Apply(catalog, Json.Read<List<TenantPlacement>>(locationPath));
    Check(catalog.Tenants.All(t => t.LocationHint.Length > 0), "every resident has a location or special-case hint");
    var locationCases = new Dictionary<string, string> { ["Aleksi Kivi"] = "Deekula A", ["Amanda Korhonen"] = "Deekula B", ["Vivi Bondar"] = "Deekula C", ["Sara Claine"] = "Tenement district · bus", ["Ville Sköldgangster"] = "Speakeasy", ["Per Otter"] = "Bazaar · Inn", ["Elsa Varis"] = "Bazaar · Internet Cafe", ["Harri Hyvinkää"] = "Bazaar · Fungi Boutique", ["Einar Kaivuri"] = "Mines · Canteen", ["Veeti Pärisevä"] = "Bazaar · Bar" };
    foreach (var location in locationCases) Check(catalog.Tenants.Single(t => t.Name == location.Key).LocationHint == location.Value, "scene-derived location for " + location.Key);
    Check(catalog.Tenants.Single(t => t.Name == "Aleksi Kivi").Placements.Any(p => p.TestPlacement) && !catalog.Tenants.Single(t => t.Name == "Aleksi Kivi").LocationHint.Contains("Clock"), "debug placements excluded from offer hints");
    var rentalProfile = Profile.Create(catalog, "Rental annotations"); var rentalTenant = catalog.Tenants.Single(t => t.Name == "Aleksi Kivi");
    Check(TenantRentalStatus.Label(catalog, rentalProfile, rentalTenant) == "Not recorded", "unrecorded occupancy is not labelled vacant");
    rentalProfile.Apartment(4).Occupant = rentalTenant.Id;
    Check(TenantRentalStatus.Label(catalog, rentalProfile, rentalTenant) == "Already renting · Apt 04", "rental annotation includes apartment number");
    rentalProfile.Apartment(4).OnlyFurniture = true;
    Check(TenantRentalStatus.Apartments(catalog, rentalProfile, rentalTenant).Count == 0, "furniture-only resident is not currently renting");
    foreach (var apartment in rentalProfile.Apartments) apartment.Occupant = "";
    Check(TenantRentalStatus.Label(catalog, rentalProfile, rentalTenant) == "Not renting", "known vacant rental status");
    rentalProfile.Apartment(1).Occupant = rentalTenant.Id; rentalProfile.Apartment(2).Occupant = rentalTenant.Id;
    Check(TenantRentalStatus.Apartments(catalog, rentalProfile, rentalTenant).Count == 0, "shop and player apartment excluded from rental annotations");
    Check(catalog.Tenants.Count == 31, "all resident definitions"); Check(catalog.Upgrades.Count == 131, "all upgrade definitions");
    Check(catalog.Upgrades.Sum(u => u.Materials.Count) == 662, "all source material lines");
    Check(catalog.Apartments.Count == 17, "actual scene apartment count");
    Check(catalog.ApartmentType(1).Name == "Player" && catalog.ApartmentType(2).Size == 3 && catalog.ApartmentType(4).Size == 0, "actual target assignments");
    Check(!catalog.ApartmentType(2).HasRentalUi && catalog.ApartmentType(4).HasRentalUi, "shop rental UI absent in actual scene data");
    Check(catalog.PermissionItem.Equals("Tenement blueprint", StringComparison.OrdinalIgnoreCase), "actual rental task item");
    Check(catalog.Tenants.Count(t => t.Recruitment.Count > 0) == 29, "29 residents have StartRent callbacks");
    Check(catalog.Tenants.Single(t => t.Name == "Malone Petronis").InitialApartment == 7, "preplaced resident mapping");
    Check(catalog.Tenants.Single(t => t.Name == "Ville Sköldgangster").Recruitment.Any(r => r.Conditions.Contains("VilleDidGetDrink")), "recruitment gates from actual dialogue");
    Check(catalog.Tenants.Single(t => t.Name == "Amanda Korhonen").NpcId == 42, "compiled ID rather than stale JSON ID");
    var casual = catalog.Presets.Single(x => x.Name == "Casual");
    Check(casual.Materials == .5f && casual.Money == .5f && casual.Speed == 2, "actual Casual settings");
    Profile Fresh()
    {
        var p = Profile.Create(catalog, "Verification"); p.HasRentalPermission = true;
        foreach (var key in p.BuildingLevels.Keys) p.BuildingLevels[key] = 0;
        foreach (var a in p.Apartments) { foreach (var key in a.Levels.Keys) a.Levels[key] = 0; a.Occupant = ""; }
        foreach (var t in catalog.Tenants) p.Recruitment[t.Id] = true;
        return p;
    }
    foreach (var tenant in catalog.Tenants)
    {
        var p = Fresh(); int target = catalog.Apartments.First(a => a.Number > 2 && tenant.Sizes.Contains(catalog.ApartmentType(a.Number).Size)).Number;
        foreach (var r in tenant.Requirements)
        {
            p.SetLevel(r.Category, target, r.Minimum);
            Check(GameRules.Evaluate(catalog, p, tenant, target).Single(x => x.Category == r.Category).Met == true, tenant.Name + " at " + r.Category + " threshold");
            if (r.Minimum > 0)
            {
                p.SetLevel(r.Category, target, r.Minimum - 1);
                Check(GameRules.Evaluate(catalog, p, tenant, target).Single(x => x.Category == r.Category).Met == false, tenant.Name + " below " + r.Category);
                p.SetLevel(r.Category, target, null);
                Check(GameRules.Evaluate(catalog, p, tenant, target).Single(x => x.Category == r.Category).Met == null, tenant.Name + " unknown " + r.Category);
            }
        }
        Check(GameRules.Evaluate(catalog, p, tenant, 1)[0].Met == false, "player apartment excluded for " + tenant.Name);
        Check(GameRules.Evaluate(catalog, p, tenant, 2)[0].Met == false, "shop excluded for " + tenant.Name);
        Throws<InvalidOperationException>(() => Planner.ForTenant(catalog, p, tenant, 2), "shop cannot be tenant planning target");
    }
    var aleksi = catalog.Tenants.Single(t => t.Name == "Aleksi Kivi");
    var p1 = Fresh();
    Check(GameRules.Evaluate(catalog, p1, aleksi, 3).Single(r => r.Name == "Apartment size").Met == false, "medium is not acceptable for small-only Aleksi");
    p1.Apartment(4).Occupant = aleksi.Id;
    Check(GameRules.Evaluate(catalog, p1, aleksi, 4).Single(r => r.Name == "Occupancy").Met == false, "occupied rejection");
    p1.Apartment(4).OnlyFurniture = true;
    Check(GameRules.Evaluate(catalog, p1, aleksi, 4).Single(r => r.Name == "Occupancy").Met == true, "onlyFurniture exception");
    var ready = Fresh(); ready.Apartment(4).Examined = true; ready.Apartment(4).PrimaryCleared = 90; ready.Apartment(4).SecondaryCleared = 80;
    Check(GameRules.Available(catalog, ready, 4) == true, "clearing exact thresholds");
    ready.Apartment(4).PrimaryCleared = 89;
    Check(GameRules.Available(catalog, ready, 4) == false, "clearing one below threshold");
    ready.Apartment(4).Levels["bathroom"] = 1;
    Check(GameRules.Available(catalog, ready, 4) == true, "existing upgrade bypasses clearing branch");
    ready.Apartment(4).OnlyFurniture = true;
    Check(GameRules.Available(catalog, ready, 4) == false, "onlyFurniture does not use upgrade bypass");
    ready.Apartment(4).OnlyFurniture = false; ready.Apartment(4).Occupant = catalog.Tenants.Single(t => t.Name == "Crazy Neighbor").Id;
    Check(GameRules.Available(catalog, ready, 4) == null, "dynamic actor permission not invented");
    ready.Apartment(4).CanRenovate = false;
    Check(GameRules.Available(catalog, ready, 4) == false, "dynamic actor denies renovation");
    ready.Apartment(4).CanRenovate = true;
    Check(GameRules.Available(catalog, ready, 4) == true, "dynamic actor permits renovation");

    Upgrade Named(string name) => catalog.Upgrades.Single(u => u.Name == name);
    var bathroom = Named("Small-Bathroom-1-Makeshift"); var walls = Named("Small-Walls-1-Makeshift");
    var p2 = Fresh();
    var goals = new[] { new Goal { Apartment = 4, UpgradeId = bathroom.Id }, new Goal { Apartment = 4, UpgradeId = walls.Id } };
    Planner.Confirm(catalog, p2, goals);
    var expected = new Dictionary<int, int> { [35010] = 32, [35050] = 34, [35180] = 20, [35100] = 4, [14860] = 7 };
    foreach (var e in expected) Check(Planner.Cart(catalog, p2).Single(x => x.ItemKey == CatalogImport.ItemKey("", e.Key)).Required == e.Value, "verified material bill " + e.Key);
    Check(p2.Jobs.Count == 2, "no inferred lower tiers");
    var alreadyStarted = ProfileStore.Clone(p2); Planner.RecordUnderway(catalog, alreadyStarted, alreadyStarted.Jobs.First());
    Check(alreadyStarted.Stock.Count == 0 && alreadyStarted.Jobs.Any(j => j.Status == JobStatus.UnderConstruction), "preexisting construction does not debit current stock");
    p2.Stock["game:35010"] = 10;
    Check(Planner.Cart(catalog, p2).Single(x => x.ItemKey == "game:35010").Remaining == 22, "stock subtraction");
    Planner.Confirm(catalog, p2, goals.Concat(goals.Select(g => new Goal { Apartment = 10, UpgradeId = g.UpgradeId })).ToArray());
    Check(p2.Jobs.Count == 4, "same definitions in different apartments are separate jobs");
    Check(Planner.Cart(catalog, p2).Single(x => x.ItemKey == "game:35010").Remaining == 54, "stock subtracted only once");
    Planner.Confirm(catalog, p2, goals);
    Throws<InvalidOperationException>(() => Planner.Start(catalog, p2, p2.Jobs.First()), "insufficient stock cannot be consumed");
    foreach (var line in Planner.Cart(catalog, p2)) p2.Stock[line.ItemKey] = line.Required;
    var job1 = p2.Jobs.Single(j => j.UpgradeId == bathroom.Id);
    Planner.Start(catalog, p2, job1);
    Check(job1.Status == JobStatus.UnderConstruction && p2.Stock["game:35010"] == 30, "atomic stock consumption");
    Check(Planner.Cart(catalog, p2).Single(x => x.ItemKey == "game:35010").Required == 30, "started job excluded from material cart");
    Throws<InvalidOperationException>(() => Planner.Start(catalog, p2, job1), "cannot double consume");
    Planner.Complete(catalog, p2, job1);
    Check(p2.Apartment(4).Levels["bathroom"] == 1, "completion updates actual category");
    var cleanup = ProfileStore.Clone(p2);
    Planner.Confirm(catalog, cleanup, cleanup.Goals.Append(new Goal { Apartment = 10, UpgradeId = bathroom.Id }).ToArray());
    Planner.RecordUnderway(catalog, cleanup, cleanup.Jobs.Single(j => j.Apartment == 10));
    cleanup.Goals.Add(new Goal { Apartment = 4, UpgradeId = bathroom.Id });
    var cleanupStock = JsonSerializer.Serialize(cleanup.Stock, Json.Options);
    var cleanupState = JsonSerializer.Serialize(cleanup.Apartments, Json.Options);
    var outstanding = JsonSerializer.Serialize(cleanup.Jobs.Where(j => j.Status != JobStatus.Completed), Json.Options);
    var cleanupCart = JsonSerializer.Serialize(Planner.Cart(catalog, cleanup), Json.Options);
    Check(Planner.RemoveCompleted(cleanup) == 1, "cleanup removes completed history");
    Check(JsonSerializer.Serialize(cleanup.Jobs, Json.Options) == outstanding, "cleanup preserves planned and underway jobs including their consumption history");
    Check(JsonSerializer.Serialize(cleanup.Stock, Json.Options) == cleanupStock && JsonSerializer.Serialize(cleanup.Apartments, Json.Options) == cleanupState, "cleanup preserves stock and installed upgrades");
    Check(JsonSerializer.Serialize(Planner.Cart(catalog, cleanup), Json.Options) == cleanupCart, "cleanup preserves outstanding material requirements");
    Check(cleanup.Goals.Count == 2 && !cleanup.Goals.Any(g => g.Apartment == 4 && g.UpgradeId == bathroom.Id), "cleanup removes only completed goals in the matching apartment");
    Planner.Confirm(catalog, cleanup, cleanup.Goals.ToArray());
    Check(cleanup.Jobs.Count == 2 && cleanup.Jobs.All(j => j.Status != JobStatus.Completed), "cleared history does not return during replanning");
    Check(Planner.RemoveCompleted(cleanup) == 0, "repeated completed cleanup is a no-op");
    Planner.Confirm(catalog, p2, []);
    Check(Planner.Cart(catalog, p2).Count == 0 && p2.Jobs.Count == 1 && p2.Jobs[0].Status == JobStatus.Completed, "goal removal preserves history only");

    Check(GameRules.MaterialCost(3, .5f) + GameRules.MaterialCost(3, .5f) == 2, "line rounding before aggregate");
    Check(GameRules.MaterialCost(20, 0) == 1, "zero multiplier minimum one");
    foreach (var u in catalog.Upgrades)
    foreach (var line in u.Materials)
    foreach (var multiplier in new[] { 0, .25f, .5f, .75f, 1, 1.25f, 2, 4 })
        Check(GameRules.MaterialCost(line.Amount, multiplier) == Math.Max(1, (int)((float)line.Amount * multiplier)), "material line exact conversion");
    var p3 = Fresh();
    var sharedUpgrade = catalog.Upgrades.First(u => !u.Building && u.Prerequisites.Any(id => catalog.Upgrade(id).Building) && catalog.Applicable(u, 4) && catalog.ContractorsFor(u, 4).Any());
    var sharedGoals = new[] { new Goal { Apartment = 4, UpgradeId = sharedUpgrade.Id }, new Goal { Apartment = 10, UpgradeId = sharedUpgrade.Id } };
    Planner.Confirm(catalog, p3, sharedGoals);
    Check(p3.Jobs.Any(j => j.Apartment == 0), "real building prerequisite included");
    Check(p3.Jobs.Where(j => j.Apartment == 0).Select(j => j.UpgradeId).Distinct().Count() == p3.Jobs.Count(j => j.Apartment == 0), "shared building prerequisites counted once");
    Planner.Confirm(catalog, p3, sharedGoals.Take(1).ToArray());
    Check(p3.Jobs.Any(j => j.Apartment == 0), "shared prerequisite remains for remaining goal");
    var unavailable = catalog.Upgrades.First(u => u.Tier > 0 && catalog.Applicable(u, 4) && !catalog.ContractorsFor(u, u.Building ? 0 : 4).Any());
    Throws<InvalidOperationException>(() => Planner.Preview(catalog, Fresh(), [new Goal { Apartment = unavailable.Building ? 0 : 4, UpgradeId = unavailable.Id }]), "zero-cost unavailable tiers cannot become free construction");

    var profileDirectory = Path.Combine(scratch, "profiles");
    using (var store = new ProfileStore(profileDirectory))
    {
        store.Save(p3); var reloaded = store.Load(p3.Id);
        Check(reloaded.Goals.Count == p3.Goals.Count && reloaded.Jobs.Count == p3.Jobs.Count, "profile round trip");
        var stale = ProfileStore.Clone(reloaded); store.Save(reloaded);
        Throws<IOException>(() => store.Save(stale), "stale revision rejected");
        using var competing = new ProfileStore(profileDirectory);
        Throws<IOException>(() => competing.Load(p3.Id), "competing writer rejected");
        reloaded.Stock["game:35010"] = 17; store.Save(reloaded);
        Check(File.Exists(store.PathFor(p3.Id) + ".bak"), "backup retained");
        File.WriteAllText(store.PathFor(p3.Id), "corrupt");
        Throws<JsonException>(() => store.Load(p3.Id), "corrupt profile not silently reset");
        var recovered = store.Load(p3.Id, true); store.Save(recovered, true);
        Check(store.Load(p3.Id).Id == p3.Id, "backup recovery");
        var newer = ProfileStore.Clone(recovered); newer.SchemaVersion = 100;
        Throws<InvalidDataException>(() => ProfileStore.Validate(newer), "newer schema rejected");
    }
    await GameFileValidationChecks.Run(scratch, Check);
    RentalIncomeChecks.Run(catalog, scratch, Check);
    VendorChecks.Run(repository, Check);
    ImportOverwriteChecks.Run(catalog, scratch, Check);
    ProfileActionChecks.Run(catalog, scratch, Check);
    TenantDirectoryChecks.Run(catalog, Check);
    SaveImportChecks.Run(catalog, scratch, Check, (action, message) =>
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { Check(true, message); return; }
        Check(false, message);
    });
    Console.WriteLine($"PASS: {assertions:N0} assertions. 31 residents / 131 upgrades / 662 material lines / 17 actual apartments.");
    Console.WriteLine("Shared prerequisite fixture: " + sharedUpgrade.Name);
    AtomicFile.Write(Path.Combine(repository, "local/verification-results.txt"), $"{DateTimeOffset.Now:O}\n{RuntimeInformation.FrameworkDescription}\nPASS: {assertions} assertions\n");
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }
