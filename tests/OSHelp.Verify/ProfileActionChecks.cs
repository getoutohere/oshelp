using System.Text.Json;
using OSHelp.Core;

internal static class ProfileActionChecks
{
    public static void Run(Catalog catalog, string scratch, Action<bool, string> check)
    {
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { check(true, message); return; }
            check(false, message);
        }
        var root = Path.Combine(scratch, "sync-saves"); var character = Path.Combine(root, "CharacterFolder");
        SaveSlot Slot(string folder, string name, int hour, string who = "Fixture_Player")
        {
            var path = Path.GetFullPath(Path.Combine(folder, name)); Directory.CreateDirectory(path);
            var date = new DateTimeOffset(2026, 11, 23, hour, 0, 0, TimeSpan.Zero);
            var header = JsonSerializer.Serialize(new { CharacterName = who, SaveName = name, Date = date.ToString("O"), ApplicationVersion = "0.5.19" });
            File.WriteAllText(Path.Combine(path, "Info.tnmt"), header); File.WriteAllText(Path.Combine(path, "Globals.tnmt"), header);
            return new(path, who, name, date, "0.5.19");
        }
        var manual = Slot(character, "Slot1", 10); var auto = Slot(character, "Autosave", 11); var quick = Slot(character, "Quicksave", 12);
        Slot(Path.Combine(root, "OtherFolder"), "Slot1", 23, "Fixture_Player");
        Slot(character, "DifferentCharacter", 23, "Someone_Else");
        Profile Fresh(string name)
        {
            var p = Profile.Create(catalog, name);
            foreach (var c in Categories.All) if (c.Building) p.BuildingLevels[c.Key] = 0; else foreach (var a in p.Apartments) a.Levels[c.Key] = 0;
            return p;
        }
        var current = Fresh("My renamed planner"); current.ImportedSave = new() { Character = manual.Character, Directory = manual.Directory, Slot = manual.Name, SaveDate = manual.Date, StockSources = ["Carried inventory + equipped items", "Backpack contents"] };
        check(ProfileSync.Latest(current).Directory == quick.Directory, "sync selects newest quicksave, excludes same-name character in another folder and other characters");
        File.SetLastWriteTimeUtc(Path.Combine(manual.Directory, "Info.tnmt"), DateTime.UtcNow.AddDays(10));
        check(ProfileSync.Latest(current).Directory == quick.Directory, "sync uses saved date rather than filesystem modification date");
        File.Delete(Path.Combine(quick.Directory, "Info.tnmt"));
        check(ProfileSync.Latest(current).Directory == auto.Directory, "sync finds newest remaining autosave when old slot is missing");
        var missing = ProfileStore.Clone(current); missing.ImportedSave!.Directory = Path.Combine(root, "Missing", "Slot1");
        Reject(() => ProfileSync.Latest(missing), "missing character saves fail without selecting another character");
        Reject(() => ProfileSync.Latest(Fresh("Manual")), "unlinked profiles need explicit character selection");

        var wall = catalog.Upgrades.Single(u => u.Name == "Small-Walls-1-Makeshift");
        var power = catalog.Upgrades.Single(u => u.Name == "Small-Electricity-1-On");
        var laterWall = catalog.Upgrades.Where(u => catalog.Applicable(u, 4) && u.Effects.Any(e => e.Category == "walls" && e.Tier > 1)).OrderBy(u => u.Tier).First();
        current.Goals = [new() { Apartment = 4, UpgradeId = wall.Id }, new() { Apartment = 4, UpgradeId = power.Id }, new() { Apartment = 4, UpgradeId = laterWall.Id }];
        Planner.Confirm(catalog, current, current.Goals.ToArray());
        current.Jobs.Single(j => j.UpgradeId == wall.Id).Status = JobStatus.UnderConstruction;
        current.Jobs.Single(j => j.UpgradeId == wall.Id).Consumed["game:35010"] = 20;
        var declined = catalog.Tenants.First(); current.Recruitment[declined.Id] = true;
        current.Stock["game:35010"] = 999;
        var incoming = Fresh("Different slot name"); incoming.ImportedSave = new() { Character = auto.Character, Directory = auto.Directory, Slot = auto.Name, SaveDate = auto.Date };
        incoming.SetLevel("walls", 4, 1); incoming.BuildingLevels["stairs"] = 1;
        incoming.Apartment(4).Occupant = declined.Id; incoming.Apartment(4).MovingIn = true;
        incoming.Apartment(4).PrimaryCleared = 100; incoming.HasRentalPermission = true;
        incoming.ResourceMultiplier = .5f; incoming.MoneyMultiplier = 2; incoming.SpeedMultiplier = 2; incoming.RentalIncomeMultiplier = 2; incoming.RentPaymentMode = 1;
        incoming.Jobs.Add(new() { Key = Planner.Key(4, power.Id), Apartment = 4, UpgradeId = power.Id, Status = JobStatus.UnderConstruction, ReadyForPayment = true, RemainingSeconds = 0, Contractor = "Rob Madsen" });
        var save = new SaveImportResult(incoming, auto, [new("inventory", "Carried inventory + equipped items", new() { ["game:35010"] = 7 }), new("contractor:RobMadsen", "Rob Madsen storage snapshot", new() { ["game:35010"] = 500 })], []);
        var before = JsonSerializer.Serialize(current, Json.Options); var sourceBefore = JsonSerializer.Serialize(incoming, Json.Options);
        var synced = ProfileSync.Merge(catalog, current, save);
        check(synced.Id == current.Id && synced.Name == current.Name && synced.Revision == current.Revision, "sync preserves renamed profile identity and revision for save");
        check(synced.Level("walls", 4) == 1 && synced.BuildingLevels["stairs"] == 1 && synced.Apartment(4).Occupant == declined.Id && synced.Apartment(4).MovingIn && synced.Apartment(4).PrimaryCleared == 100, "sync replaces apartment/building/occupancy/move-in/clearing state");
        check(synced.ResourceMultiplier == .5f && synced.MoneyMultiplier == 2 && synced.SpeedMultiplier == 2 && synced.RentalIncomeMultiplier == 2 && synced.RentPaymentMode == 1 && synced.HasRentalPermission == true, "sync replaces costs, income, payment schedule, and permission from save");
        check(synced.Stock["game:35010"] == 7 && synced.ImportedSave!.StockSourceIds!.Contains("Backpack"), "legacy source choices migrate; unselected depots excluded and absent bags remembered");
        check(synced.Goals.All(g => g.UpgradeId != wall.Id) && synced.Goals.Any(g => g.UpgradeId == laterWall.Id) && synced.Goals.Any(g => g.UpgradeId == power.Id), "completed goals removed; pending and active goals retained");
        check(synced.Jobs.Single(j => j.UpgradeId == wall.Id).Status == JobStatus.Completed && synced.Jobs.Single(j => j.UpgradeId == wall.Id).Consumed["game:35010"] == 20, "previous construction becomes completed history without losing recorded consumption");
        check(synced.Jobs.Single(j => j.UpgradeId == power.Id).ReadyForPayment && Planner.Cart(catalog, synced).SelectMany(l => l.Contributors).All(c => !c.Contains(catalog.UpgradeLabel(power))), "saved work awaiting payment is not charged materials again");
        check(synced.Recruitment[declined.Id] == true && synced.ImportedSave!.Directory == auto.Directory, "manual recruitment confirmations retained; latest source provenance updated");
        check(before == JsonSerializer.Serialize(current, Json.Options) && sourceBefore == JsonSerializer.Serialize(incoming, Json.Options), "sync merging never mutates current profile or incoming save on failure or success");
        var again = ProfileSync.Merge(catalog, synced, save);
        check(JsonSerializer.Serialize(again, Json.Options) == JsonSerializer.Serialize(synced, Json.Options), "repeated sync is idempotent and does not double-consume material stock");
        var withBag = save with { StockSources = save.StockSources.Append(new SaveStockSource("Backpack", "Backpack contents", new() { ["game:35010"] = 3 })).ToArray() };
        check(ProfileSync.Merge(catalog, synced, withBag).Stock["game:35010"] == 10, "remembered selected bag is counted when it returns");
        var noStock = ProfileStore.Clone(current); noStock.ImportedSave!.StockSourceIds = [];
        check(ProfileSync.Merge(catalog, noStock, save).Stock.Count == 0, "explicit empty stock selection stays empty on sync");
        var wrong = ProfileStore.Clone(incoming); wrong.ImportedSave!.Character = "Other";
        Reject(() => ProfileSync.Merge(catalog, current, save with { Profile = wrong }), "different character sync is rejected");
        wrong = ProfileStore.Clone(incoming); wrong.CatalogFingerprint = "different";
        Reject(() => ProfileSync.Merge(catalog, current, save with { Profile = wrong }), "different catalog sync is rejected");
        var reverted = ProfileStore.Clone(incoming); reverted.SetLevel("walls", 4, 0);
        var rolledBack = ProfileSync.Merge(catalog, current, save with { Profile = reverted });
        check(rolledBack.Jobs.Single(j => j.UpgradeId == wall.Id).Status == JobStatus.Planned, "construction absent in save is replanned from retained goals rather than left underway");

        var directory = Path.Combine(scratch, "profile-actions"); using var store = new ProfileStore(directory);
        store.Save(current); var originalHash = CatalogImport.Hash(store.PathFor(current.Id));
        synced = ProfileSync.Merge(catalog, current, save); store.SaveSync(current, synced);
        check(store.List().Count == 1 && store.Load(current.Id).Name == current.Name && synced.Revision == current.Revision + 1, "sync commits to same named profile with a new revision");
        check(CatalogImport.Hash(store.PathFor(current.Id) + ".bak") == originalHash, "sync preserves exact pre-sync backup");
        Reject(() => store.SaveSync(current, ProfileSync.Merge(catalog, current, save)), "stale sync cannot overwrite a newer profile");
        Reject(() => store.Delete(current), "stale deletion cannot remove a newer profile");
        using (var competing = new ProfileStore(directory))
        {
            Reject(() => competing.Delete(synced), "delete respects another instance's write lease");
            Reject(() => competing.SaveSync(synced, ProfileSync.Merge(catalog, synced, save)), "sync respects another instance's write lease");
        }
        var finalHash = CatalogImport.Hash(store.PathFor(current.Id)); var backupHash = CatalogImport.Hash(store.PathFor(current.Id) + ".bak");
        var archive = store.Delete(synced);
        check(store.List().Count == 0 && !File.Exists(store.PathFor(synced.Id)), "deleting the last profile removes it from the list");
        check(CatalogImport.Hash(Path.Combine(archive, synced.Id + ".json")) == finalHash && CatalogImport.Hash(Path.Combine(archive, synced.Id + ".json.bak")) == backupHash, "delete archives profile and backup byte-for-byte");
        Reject(() => store.SaveSync(synced, ProfileSync.Merge(catalog, synced, save)), "sync cannot resurrect a deleted profile");
        Console.WriteLine("Profile actions: latest character save / sync state and plan reconciliation / stock choices / idempotence / backups / delete and write leases passed.");
    }
}
