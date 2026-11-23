namespace OSHelp.Core;

public enum JobStatus { Planned, UnderConstruction, Completed }
public sealed class ApartmentState
{
    public int Number { get; set; }
    public Dictionary<string, int?> Levels { get; set; } = [];
    public string? Occupant { get; set; }
    public bool OnlyFurniture { get; set; }
    public bool? Examined { get; set; }
    public bool? CanRenovate { get; set; }
    public int? PrimaryCleared { get; set; }
    public int? SecondaryCleared { get; set; }
    public bool SavedUnderConstruction { get; set; }
    public bool MovingIn { get; set; }
    public Dictionary<string, string> CurrentUpgradeIds { get; set; } = [];
}
public sealed class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UpgradeId { get; set; } = "";
    public int Apartment { get; set; }
}
public sealed class Job
{
    public string Key { get; set; } = "";
    public string UpgradeId { get; set; } = "";
    public int Apartment { get; set; }
    public JobStatus Status { get; set; }
    public Dictionary<string, long> Consumed { get; set; } = [];
    public float? StartMoneyMultiplier { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool ReadyForPayment { get; set; }
    public int? RemainingSeconds { get; set; }
    public string Contractor { get; set; } = "";
}
public sealed class Profile
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My playthrough";
    public string CatalogFingerprint { get; set; } = "";
    public int Revision { get; set; }
    public float ResourceMultiplier { get; set; } = 1;
    public float MoneyMultiplier { get; set; } = 1;
    public float SpeedMultiplier { get; set; } = 1;
    public float? RentalIncomeMultiplier { get; set; }
    public int? RentPaymentMode { get; set; }
    public bool IgnoreCosts { get; set; }
    public Dictionary<string, int?> BuildingLevels { get; set; } = [];
    public Dictionary<string, string> BuildingCurrentUpgradeIds { get; set; } = [];
    public List<ApartmentState> Apartments { get; set; } = [];
    public Dictionary<string, bool?> Recruitment { get; set; } = [];
    public bool? HasRentalPermission { get; set; }
    public Dictionary<string, long> Stock { get; set; } = [];
    public List<Goal> Goals { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; }
    public SaveProvenance? ImportedSave { get; set; }
    public static Profile Create(Catalog catalog, string name)
    {
        return new() { Name = name, CatalogFingerprint = catalog.Fingerprint, RentalIncomeMultiplier = 1, RentPaymentMode = 0,
            BuildingLevels = Categories.All.Where(x => x.Building).ToDictionary(x => x.Key, _ => (int?)null),
            Apartments = catalog.Apartments.Select(a => new ApartmentState { Number = a.Number, Levels = Categories.All.Where(c => !c.Building).ToDictionary(c => c.Key, _ => (int?)null) }).ToList()
        };
    }
    public ApartmentState Apartment(int number) => Apartments.Single(x => x.Number == number);
    public int? Level(string key, int apartment) => Categories.Get(key).Building ? BuildingLevels.GetValueOrDefault(key) : Apartment(apartment).Levels.GetValueOrDefault(key);
    public void SetLevel(string key, int apartment, int? value)
    {
        if (Categories.Get(key).Building) { BuildingLevels[key] = value; BuildingCurrentUpgradeIds.Remove(key); }
        else { Apartment(apartment).Levels[key] = value; Apartment(apartment).CurrentUpgradeIds.Remove(key); }
    }
}
public sealed record RequirementResult(string Name, string Required, string Recorded, bool? Met, string Category = "");
public sealed record CartLine(string ItemKey, long Required, long Have, long Remaining, IReadOnlyList<string> Contributors);
public sealed record PlanPreview(IReadOnlyList<Job> Jobs, IReadOnlyList<string> Notes);

public static class GameRules
{
    // Verified against DifficultyInfo.GetMultiplierValue and GetRenovationResourceCost.
    public static float Multiplier(int value) => value switch { 0 => 1, 1 => 0, >= 2 and <= 4 => (value - 1) * 0.25f, >= 5 and <= 16 => value * 0.25f, _ => throw new InvalidDataException("Nonfinite or unknown multiplier is not supported") };
    public static int MaterialCost(int amount, float multiplier)
    {
        if (amount < 0 || !float.IsFinite(multiplier) || multiplier < 0 || multiplier > 4) throw new ArgumentOutOfRangeException(nameof(multiplier));
        float scaled = (float)amount * multiplier;
        if (scaled >= int.MaxValue) throw new OverflowException("Material amount exceeds the game integer range");
        return Math.Max(1, (int)scaled);
    }
    public static int Price(int amount, float multiplier) => checked((int)((float)amount * multiplier));
    public static int Hours(int amount, float speed) => speed > 0 && float.IsFinite(speed) ? checked((int)((float)amount / speed)) : throw new ArgumentOutOfRangeException(nameof(speed), "Construction speed must be positive");
    public static bool? Satisfied(Upgrade upgrade, int apartment, Profile profile)
    {
        var levels = upgrade.Effects.Select(x => (Recorded: profile.Level(x.Category, apartment), x.Tier)).ToArray();
        if (levels.Any(x => x.Recorded.HasValue && x.Recorded.Value < x.Tier)) return false;
        return levels.All(x => x.Tier == 0 || x.Recorded.HasValue) ? true : null;
    }
    public static IReadOnlyList<RequirementResult> Evaluate(Catalog catalog, Profile profile, Tenant tenant, int apartment)
    {
        var rows = new List<RequirementResult>(); var state = profile.Apartment(apartment); var type = catalog.ApartmentType(apartment);
        rows.Add(new("Rental target", "Rental apartment", apartment == 1 ? "Player apartment" : !type.HasRentalUi ? "Shop (cannot be rented to tenants)" : $"Apartment {apartment}", apartment != 1 && type.HasRentalUi));
        rows.Add(new("Apartment size", string.Join(" / ", tenant.Sizes.Select(SizeName)), SizeName(type.Size), tenant.Sizes.Contains(type.Size)));
        rows.Add(new("Occupancy", "Vacant or furniture only", state.Occupant == null ? "Not recorded" : state.Occupant.Length == 0 ? "Vacant" : catalog.Tenants.FirstOrDefault(x => x.Id == state.Occupant)?.Name ?? state.Occupant, state.OnlyFurniture ? true : state.Occupant == null ? null : state.Occupant.Length == 0));
        // Apartment requirements first, then the building order in CheckRequirements.
        string[] requirementOrder = ["power", "water", "heating", "bathroom", "walls", "balcony", "kitchen", "elevator", "stairs", "basement", "sauna", "facade", "courtyard", "mine"];
        foreach (var key in requirementOrder)
        {
            var r = tenant.Requirements.Single(r => r.Category == key);
            var level = profile.Level(r.Category, apartment); var c = Categories.Get(r.Category);
            rows.Add(new(c.Name, catalog.TierName(r.Category, r.Minimum), level.HasValue ? catalog.TierName(r.Category, level.Value) : "Not recorded", r.Minimum == 0 ? true : level.HasValue ? level.Value >= r.Minimum : null, r.Category));
        }
        rows.Add(new("Landlord permission", catalog.PermissionItem, Fact(profile.HasRentalPermission), profile.HasRentalPermission));
        rows.Add(new("Recruitment dialogue reached", "Confirm the offer is available in your game", Fact(profile.Recruitment.GetValueOrDefault(tenant.Id)), profile.Recruitment.GetValueOrDefault(tenant.Id)));
        var alreadyRented = profile.Apartments.Any(a => a.Occupant == tenant.Id && !a.OnlyFurniture);
        rows.Add(new("Tenant not already renting", "No other rental", alreadyRented ? "Already assigned in profile" : "No recorded assignment", alreadyRented ? false : profile.Apartments.Any(a => a.Occupant == null) ? null : true));
        rows.Add(new("Apartment availability", "Existing upgrades, or examined + 90%/80% cleared", $"Examined: {Fact(state.Examined)}; cleared: {state.PrimaryCleared?.ToString() ?? "?"}% / {state.SecondaryCleared?.ToString() ?? "?"}%", Available(catalog, profile, apartment)));
        return rows;
    }
    public static bool? Available(Catalog catalog, Profile profile, int apartment)
    {
        var a = profile.Apartment(apartment);
        var resident = catalog.Tenants.FirstOrDefault(t => t.Id == a.Occupant);
        if (resident != null && !a.OnlyFurniture)
        {
            if (resident.DialogueActor.Length > 0 && a.CanRenovate != true) return a.CanRenovate == false ? false : null;
            if (resident.DialogueActor.Length == 0 && resident.NoUpgrade) return false;
        }
        bool upgraded = a.SavedUnderConstruction || a.Levels.Values.Any(v => v > 0) || profile.Jobs.Any(j => j.Apartment == apartment && j.Status == JobStatus.UnderConstruction);
        if (upgraded && !a.OnlyFurniture) return true;
        if (a.Examined == true && a.PrimaryCleared >= 90 && a.SecondaryCleared >= 80) return true;
        bool clearingFails = a.Examined == false || a.PrimaryCleared < 90 || a.SecondaryCleared < 80;
        if (clearingFails && (a.OnlyFurniture || a.Levels.Values.All(v => v.HasValue))) return false;
        return null;
    }
    public static string SizeName(int size) => size switch { 0 => "Small", 1 => "Medium", 2 => "Large", 3 => "Shop", _ => throw new InvalidDataException("Unknown apartment size") };
    static string Fact(bool? value) => value == true ? "Confirmed" : value == false ? "Not yet" : "Not recorded";
}

public static class Planner
{
    public static string Key(int apartment, string upgrade) => apartment + "|" + upgrade;
    public static PlanPreview Preview(Catalog catalog, Profile profile, IEnumerable<Goal> goals)
    {
        var jobs = new Dictionary<string, Job>(); var visiting = new HashSet<string>(); var notes = new List<string>();
        void Add(string id, int target, bool root)
        {
            var upgrade = catalog.Upgrade(id); int scope = upgrade.Building ? 0 : target;
            if (!upgrade.Building && (target == 0 || !catalog.Applicable(upgrade, target))) throw new InvalidOperationException(upgrade.Name + " does not apply to the selected apartment.");
            if (upgrade.Building && !catalog.Applicable(upgrade, 0)) throw new InvalidOperationException("This building upgrade is not assigned to the tenement.");
            if (upgrade.Tier == 0) { if (root) throw new InvalidOperationException("A starting state is not a construction job."); return; }
            string key = Key(scope, id);
            var existing = profile.Jobs.FirstOrDefault(x => x.Key == key && x.Status == JobStatus.UnderConstruction);
            if (existing != null) { jobs.TryAdd(key, existing); return; }
            if (GameRules.Satisfied(upgrade, target, profile) == true) { if (root) notes.Add(upgrade.Name + " is already met by the recorded levels."); return; }
            if (jobs.ContainsKey(key)) return;
            if (!visiting.Add(key)) throw new InvalidDataException("Cyclic scoped upgrade prerequisites");
            if (!catalog.ContractorsFor(upgrade, scope).Any()) throw new InvalidOperationException(upgrade.Name + " has no qualified contractor in the game data. It cannot be added as constructible work.");
            if (GameRules.Satisfied(upgrade, target, profile) == null) notes.Add(catalog.ScopeName(scope) + ": confirm " + upgrade.Name + " is still needed (current level unrecorded).");
            foreach (var dependency in upgrade.Prerequisites) Add(dependency, target, false);
            visiting.Remove(key);
            jobs[key] = new() { Key = key, Apartment = scope, UpgradeId = id };
        }
        foreach (var goal in goals) Add(goal.UpgradeId, goal.Apartment, true);
        // Under-construction jobs remain history/work even when their last desired goal is removed.
        foreach (var job in profile.Jobs.Where(x => x.Status != JobStatus.Planned)) jobs.TryAdd(job.Key, job);
        return new(jobs.Values.ToArray(), notes.Distinct().ToArray());
    }
    public static void Confirm(Catalog catalog, Profile profile, IReadOnlyList<Goal> goals)
    {
        var preview = Preview(catalog, profile, goals); profile.Goals = goals.ToList(); profile.Jobs = preview.Jobs.ToList();
    }
    public static IReadOnlyList<Goal> AddGoal(Catalog catalog, Profile profile, Upgrade upgrade, int apartment)
    {
        int scope = upgrade.Building ? 0 : apartment;
        var goals = profile.Goals.Where(g => !(g.Apartment == scope && catalog.Upgrade(g.UpgradeId).Effects.Any(e => upgrade.Effects.Any(ne => ne.Category == e.Category)))).ToList();
        goals.Add(new() { Apartment = scope, UpgradeId = upgrade.Id }); return goals;
    }
    public static IReadOnlyList<CartLine> Cart(Catalog catalog, Profile profile)
    {
        var totals = new Dictionary<string, (long Total, List<string> Jobs)>();
        foreach (var job in profile.Jobs.Where(x => x.Status == JobStatus.Planned))
        {
            var upgrade = catalog.Upgrade(job.UpgradeId);
            foreach (var line in upgrade.Materials)
            {
                long amount = profile.IgnoreCosts ? 0 : GameRules.MaterialCost(line.Amount, profile.ResourceMultiplier);
                var value = totals.GetValueOrDefault(line.ItemKey, (0, []));
                value.Item1 = checked(value.Item1 + amount); value.Item2.Add($"{catalog.ScopeName(job.Apartment)} / {catalog.UpgradeLabel(upgrade)}: {amount}"); totals[line.ItemKey] = value;
            }
        }
        return totals.Select(x => new CartLine(x.Key, x.Value.Total, profile.Stock.GetValueOrDefault(x.Key), Math.Max(0, x.Value.Total - profile.Stock.GetValueOrDefault(x.Key)), x.Value.Jobs)).OrderBy(x => catalog.Items[x.ItemKey].Name).ToArray();
    }
    public static Dictionary<string, long> Bill(Catalog catalog, Profile profile, Job job)
    {
        var bill = new Dictionary<string, long>();
        foreach (var line in catalog.Upgrade(job.UpgradeId).Materials) bill[line.ItemKey] = checked(bill.GetValueOrDefault(line.ItemKey) + (profile.IgnoreCosts ? 0 : GameRules.MaterialCost(line.Amount, profile.ResourceMultiplier)));
        return bill;
    }
    public static void Start(Catalog catalog, Profile profile, Job job)
    {
        if (job.Status != JobStatus.Planned) throw new InvalidOperationException("Only planned work can be started.");
        var upgrade = catalog.Upgrade(job.UpgradeId);
        if (!upgrade.Building)
        {
            var state = profile.Apartment(job.Apartment);
            var resident = catalog.Tenants.FirstOrDefault(t => t.Id == state.Occupant);
            if (!state.OnlyFurniture && resident != null && (resident.NoUpgrade || resident.DialogueActor.Length > 0) && state.CanRenovate != true)
                throw new InvalidOperationException("This resident restricts renovations. Confirm their renovation permission in the state editor first.");
        }
        foreach (var dep in upgrade.Prerequisites) if (GameRules.Satisfied(catalog.Upgrade(dep), job.Apartment, profile) != true) throw new InvalidOperationException("Record the prerequisite as completed first: " + catalog.Upgrade(dep).Name);
        var bill = Bill(catalog, profile, job);
        if (bill.Any(x => profile.Stock.GetValueOrDefault(x.Key) < x.Value)) throw new InvalidOperationException("Record sufficient materials on hand before starting this job. This records consumption in your planner only.");
        foreach (var line in bill) profile.Stock[line.Key] = checked(profile.Stock.GetValueOrDefault(line.Key) - line.Value);
        job.Consumed = bill; job.Status = JobStatus.UnderConstruction; job.StartedAt = DateTimeOffset.UtcNow; job.StartMoneyMultiplier = profile.MoneyMultiplier;
    }
    public static void Complete(Catalog catalog, Profile profile, Job job)
    {
        if (job.Status != JobStatus.UnderConstruction) throw new InvalidOperationException("Start the job first, or record existing work in the apartment/building state editor.");
        foreach (var e in catalog.Upgrade(job.UpgradeId).Effects)
        {
            if (profile.Level(e.Category, job.Apartment) > e.Tier) continue;
            profile.SetLevel(e.Category, job.Apartment, e.Tier);
            (Categories.Get(e.Category).Building ? profile.BuildingCurrentUpgradeIds : profile.Apartment(job.Apartment).CurrentUpgradeIds)[e.Category] = job.UpgradeId;
        }
        job.Status = JobStatus.Completed; job.CompletedAt = DateTimeOffset.UtcNow;
        job.ReadyForPayment = false; job.RemainingSeconds = 0;
        if (job.Apartment != 0 && !profile.Jobs.Any(j => j.Apartment == job.Apartment && j.Status == JobStatus.UnderConstruction)) profile.Apartment(job.Apartment).SavedUnderConstruction = false;
        profile.Goals.RemoveAll(x => x.UpgradeId == job.UpgradeId && x.Apartment == job.Apartment);
        Confirm(catalog, profile, profile.Goals.ToArray());
    }
    public static void RecordUnderway(Catalog catalog, Profile profile, Job job)
    {
        if (job.Status != JobStatus.Planned) throw new InvalidOperationException("Only planned work can be recorded as already underway.");
        // The user records work that started before entering it in OSHelp. Current
        // stock is already unspent stock, so deducting another bill would be wrong.
        job.Status = JobStatus.UnderConstruction; job.StartedAt = DateTimeOffset.UtcNow;
        Confirm(catalog, profile, profile.Goals.ToArray());
    }
    public static IReadOnlyList<Goal> ForTenant(Catalog catalog, Profile profile, Tenant tenant, int apartment)
    {
        if (apartment == 1 || !catalog.ApartmentType(apartment).HasRentalUi || !tenant.Sizes.Contains(catalog.ApartmentType(apartment).Size)) throw new InvalidOperationException("Choose a rental apartment of an accepted size first. The player apartment and shop cannot be rented to tenants.");
        var goals = profile.Goals.ToList();
        foreach (var r in tenant.Requirements.Where(r => r.Minimum > 0))
        {
            var c = Categories.Get(r.Category); var level = profile.Level(c.Key, apartment);
            if (level >= r.Minimum) continue;
            var source = c.Building ? catalog.BuildingUpgrades[c.Key] : catalog.ApartmentType(apartment).Upgrades[c.Key];
            var candidates = source.Select(catalog.Upgrade).Where(u => u.Effects.Any(e => e.Category == r.Category && e.Tier >= r.Minimum) && catalog.ContractorsFor(u, c.Building ? 0 : apartment).Any()).OrderBy(u => u.Effects.Single(e => e.Category == r.Category).Tier).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("No applicable constructible upgrade meets " + c.Name);
            int scope = c.Building ? 0 : apartment;
            var existing = goals.Where(g => g.Apartment == scope).Select(g => catalog.Upgrade(g.UpgradeId)).FirstOrDefault(u => u.Effects.Any(e => e.Category == r.Category && e.Tier >= r.Minimum));
            if (existing != null) continue;
            goals.RemoveAll(g => g.Apartment == scope && catalog.Upgrade(g.UpgradeId).Effects.Any(e => e.Category == r.Category));
            goals.Add(new() { Apartment = scope, UpgradeId = candidates[0].Id });
        }
        return goals;
    }
}
