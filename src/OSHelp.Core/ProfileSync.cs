namespace OSHelp.Core;

public static class ProfileSync
{
    public static SaveSlot Latest(Profile profile)
    {
        var origin = profile.ImportedSave ?? throw new InvalidOperationException("Import a game save to link this profile to a character first.");
        var characterDirectory = Path.GetDirectoryName(Path.GetFullPath(origin.Directory)) ?? throw new InvalidDataException("Missing character save directory.");
        var root = Path.GetDirectoryName(characterDirectory) ?? throw new InvalidDataException("Missing save root.");
        return SaveImport.Discover(root)
            .Where(s => s.Character == origin.Character && string.Equals(Path.GetDirectoryName(Path.GetFullPath(s.Directory)), characterDirectory, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Date).ThenBy(s => s.Directory, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            ?? throw new FileNotFoundException("No saves found for " + origin.Character.Replace('_', ' ') + " in " + characterDirectory);
    }

    public static Profile Merge(Catalog catalog, Profile current, SaveImportResult save)
    {
        if (current.CatalogFingerprint != catalog.Fingerprint || save.Profile.CatalogFingerprint != catalog.Fingerprint)
            throw new InvalidDataException("Cannot sync profiles from a different game catalog.");
        var origin = current.ImportedSave ?? throw new InvalidOperationException("This profile is not linked to a game save.");
        var incoming = save.Profile.ImportedSave ?? throw new InvalidDataException("The incoming profile has no game-save source.");
        if (origin.Character != incoming.Character || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(origin.Directory)), Path.GetDirectoryName(Path.GetFullPath(incoming.Directory)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The save belongs to a different character or character directory.");
        var selected = SourceIds(catalog, origin, save.StockSources).ToHashSet(StringComparer.Ordinal);
        var next = save.CreateProfile(save.StockSources.Where(s => selected.Contains(s.Id)).Select(s => s.Id));
        // Remember absent bags/depots so they are included again when present in a later save.
        next.ImportedSave!.StockSourceIds = selected.Order(StringComparer.Ordinal).ToList();
        next.Id = current.Id; next.Name = current.Name; next.Revision = current.Revision; next.SavedAt = current.SavedAt;
        foreach (var fact in current.Recruitment) if (!next.Recruitment.ContainsKey(fact.Key)) next.Recruitment[fact.Key] = fact.Value;
        var previous = ProfileStore.Clone(current);
        foreach (var job in previous.Jobs.Where(j => j.Status != JobStatus.Planned))
        {
            var active = next.Jobs.SingleOrDefault(j => j.Key == job.Key);
            if (active != null)
            {
                active.Consumed = job.Consumed; active.StartedAt = job.StartedAt; active.StartMoneyMultiplier = job.StartMoneyMultiplier;
            }
            else if (GameRules.Satisfied(catalog.Upgrade(job.UpgradeId), job.Apartment, next) == true)
            {
                job.Status = JobStatus.Completed; job.ReadyForPayment = false; job.RemainingSeconds = 0;
                next.Jobs.Add(job);
            }
            // Work absent from both the active jobs and installed levels is no longer current.
        }
        var goals = previous.Goals.Where(g => next.Jobs.Any(j => j.Key == Planner.Key(g.Apartment, g.UpgradeId) && j.Status == JobStatus.UnderConstruction)
            || GameRules.Satisfied(catalog.Upgrade(g.UpgradeId), g.Apartment, next) != true).ToArray();
        Planner.Confirm(catalog, next, goals);
        ProfileStore.Validate(next);
        return next;
    }

    static IEnumerable<string> SourceIds(Catalog catalog, SaveProvenance origin, IReadOnlyList<SaveStockSource> available)
    {
        if (origin.StockSourceIds != null) return origin.StockSourceIds;
        // Compatibility with profiles imported before stable source IDs were saved.
        var names = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Carried inventory + equipped items"] = "inventory", ["Backpack contents"] = "Backpack",
            ["Rucksack contents"] = "Rucksack", ["CoolerBag contents"] = "CoolerBag"
        };
        foreach (var contractor in catalog.Contractors) names[contractor.Name + " storage snapshot"] = "contractor:" + contractor.Name.Replace(" ", "");
        foreach (var source in available) names[source.Name] = source.Id;
        return origin.StockSources.Select(name => names.TryGetValue(name, out var id) ? id
            : name.EndsWith(" storage snapshot", StringComparison.Ordinal) ? "contractor:" + name[..^17]
            : throw new InvalidDataException("Cannot identify the previously selected stock source: " + name));
    }
}
