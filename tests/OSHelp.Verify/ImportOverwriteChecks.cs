using System.Text;
using OSHelp.Core;

internal static class ImportOverwriteChecks
{
    public static void Run(Catalog catalog, string scratch, Action<bool, string> check)
    {
        var directory = Path.Combine(scratch, "import-overwrite");
        Profile Incoming(string name = "Fixture Player / Slot1")
        {
            var p = Profile.Create(catalog, name); p.ImportedSave = new() { Character = "Fixture_Player", Slot = "Slot1", SaveDate = DateTimeOffset.UtcNow };
            p.Stock["game:35010"] = 5; return p;
        }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { check(true, message); return; }
            check(false, message);
        }
        using var store = new ProfileStore(directory);
        var incoming = Incoming(); var saved = store.SaveGameImport(incoming);
        check(store.List().Count == 1 && saved.Name == incoming.Name && incoming.Revision == 0, "first game import creates one profile without mutating input");
        var originalId = saved.Id; var originalHash = CatalogImport.Hash(store.PathFor(originalId));
        Reject(() => store.SaveGameImport(Incoming()), "existing name requires explicit overwrite approval");
        check(CatalogImport.Hash(store.PathFor(originalId)) == originalHash && store.List().Count == 1, "unapproved import leaves existing profile unchanged");
        var matches = store.FindNameMatches("  fixture player / SLOT1  ");
        check(matches.Count == 1 && matches[0].Id == originalId, "name matches ignore case and surrounding whitespace");
        using (var competing = new ProfileStore(directory))
        {
            Reject(() => competing.SaveGameImport(Incoming(), matches), "overwriting a profile open in another instance is rejected");
            check(CatalogImport.Hash(store.PathFor(originalId)) == originalHash, "competing overwrite leaves target unchanged");
        }
        var replacement = Incoming(); replacement.Stock["game:35010"] = 25;
        var overwritten = store.SaveGameImport(replacement, matches, originalId);
        check(overwritten.Id == originalId && overwritten.Revision == saved.Revision + 1 && store.List().Count == 1, "confirmed overwrite reuses identity and increments revision");
        check(store.Load(originalId).Stock["game:35010"] == 25 && CatalogImport.Hash(store.PathFor(originalId) + ".bak") == originalHash, "overwrite replaces snapshot and retains exact previous backup");
        var staleApproval = store.FindNameMatches(replacement.Name); var updated = store.Load(originalId); updated.Stock["game:35010"] = 26; store.Save(updated);
        var updatedHash = CatalogImport.Hash(store.PathFor(originalId));
        Reject(() => store.SaveGameImport(Incoming(), staleApproval), "changed profile requires a new overwrite confirmation");
        check(CatalogImport.Hash(store.PathFor(originalId)) == updatedHash, "stale approval cannot clobber newer data");

        // Seed names duplicated by older app versions, including a BOM and case variant.
        var legacy = Incoming("  fixture player / SLOT1  "); legacy.Stock["game:35010"] = 99; store.Save(legacy);
        var legacyPath = store.PathFor(legacy.Id); File.WriteAllText(legacyPath, File.ReadAllText(legacyPath), new UTF8Encoding(true));
        var legacyHash = CatalogImport.Hash(legacyPath);
        var unrelated = Incoming("Other playthrough"); store.Save(unrelated); var unrelatedHash = CatalogImport.Hash(store.PathFor(unrelated.Id));
        var duplicates = store.FindNameMatches(incoming.Name);
        check(duplicates.Count == 2, "existing duplicate names and BOM profiles are discovered");
        check(ProfileStore.ChooseImportTarget(duplicates, originalId)?.Id == originalId && ProfileStore.ChooseImportTarget(duplicates, null)?.Id == legacy.Id, "active matching profile preferred, otherwise most recent");
        var consolidated = store.SaveGameImport(Incoming(), duplicates, originalId);
        check(consolidated.Id == originalId && store.FindNameMatches(incoming.Name).Count == 1 && store.List().Count == 2, "confirmed overwrite consolidates pre-existing duplicate entries");
        var archived = Directory.EnumerateFiles(Path.Combine(directory, "replaced"), legacy.Id.ToString("D") + ".json", SearchOption.AllDirectories).Single();
        check(CatalogImport.Hash(archived) == legacyHash && !File.Exists(legacyPath), "extra duplicate is archived byte-for-byte");
        check(CatalogImport.Hash(store.PathFor(unrelated.Id)) == unrelatedHash, "unrelated playthrough preserved");

        using var failing = new ProfileStore(Path.Combine(scratch, "overwrite-failure"));
        var target = failing.SaveGameImport(Incoming()); var duplicate = Incoming(); failing.Save(duplicate);
        var targetHash = CatalogImport.Hash(failing.PathFor(target.Id)); var duplicateHash = CatalogImport.Hash(failing.PathFor(duplicate.Id));
        // Block the backup destination to fail the target's atomic replacement after archiving duplicates.
        Directory.CreateDirectory(failing.PathFor(target.Id) + ".bak");
        Reject(() => failing.SaveGameImport(Incoming(), failing.FindNameMatches(target.Name), target.Id), "failed target replacement is reported");
        check(CatalogImport.Hash(failing.PathFor(target.Id)) == targetHash && CatalogImport.Hash(failing.PathFor(duplicate.Id)) == duplicateHash && failing.FindNameMatches(target.Name).Count == 2, "failed overwrite restores archived duplicates and leaves original snapshots intact");
        Console.WriteLine("Save-name collisions: approval / case matching / existing duplicates / backup / concurrent writer / stale confirmation / rollback passed.");
    }
}
