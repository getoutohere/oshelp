using System.Text.Json;
using OSHelp.Core;

internal static class UnderwayPlanningChecks
{
    public static void Run(Catalog catalog, Action<bool, string> check)
    {
        Upgrade Named(string name) => catalog.Upgrades.Single(u => u.Name == name);
        var makeshift = Named("Small-Balcony-1-Makeshift");
        var shabby = Named("Small-Balcony-2-Shabby");
        var decent = Named("Small-Balcony-3-Decent");
        var fine = Named("Small-Balcony-4-Fine");
        var power = Named("Small-Electricity-1-On");
        Profile Fresh(Upgrade active, int scope = 4)
        {
            var p = Profile.Create(catalog, "Underway regression");
            foreach (string category in p.BuildingLevels.Keys) p.BuildingLevels[category] = 0;
            foreach (var a in p.Apartments) foreach (string category in a.Levels.Keys) a.Levels[category] = 0;
            p.Stock["game:35010"] = 12;
            p.Jobs.Add(new() { Key = Planner.Key(scope, active.Id), Apartment = scope, UpgradeId = active.Id,
                Status = JobStatus.UnderConstruction, Contractor = "Rob Madsen", ReadyForPayment = true, RemainingSeconds = 0,
                StartedAt = DateTimeOffset.UtcNow, StartMoneyMultiplier = .5f, Consumed = new() { ["game:35010"] = 7 } });
            return p;
        }
        foreach (var requested in new[] { makeshift, shabby, decent })
        {
            var p = Fresh(decent); string active = JsonSerializer.Serialize(p.Jobs.Single(), Json.Options);
            var goals = Planner.AddGoal(catalog, p, requested, 4);
            var preview = Planner.Preview(catalog, p, goals);
            check(preview.Jobs.Count == 1 && preview.Jobs[0].UpgradeId == decent.Id && preview.Notes.Any(n => n.Contains("already underway")), "same or higher underway balcony covers manual planning requests with an explanation");
            Planner.Confirm(catalog, p, goals);
            check(Planner.Cart(catalog, p).Count == 0 && p.Stock["game:35010"] == 12 && JsonSerializer.Serialize(p.Jobs.Single(), Json.Options) == active, "covered balcony adds no material bill and preserves active contractor/payment/consumption metadata");
            check(p.Level("balcony", 4) == 0, "underway balcony does not become an installed balcony before completion");
        }
        var tenant = catalog.Tenants.Single(t => t.Name == "Taneli Kurju");
        var tenantProfile = Fresh(decent);
        foreach (var requirement in tenant.Requirements.Where(r => r.Category != "balcony")) tenantProfile.SetLevel(requirement.Category, 4, requirement.Minimum);
        check(Planner.ForTenant(catalog, tenantProfile, tenant, 4).Count == 0, "tenant planning uses the higher balcony already under construction instead of adding Makeshift");
        check(GameRules.Evaluate(catalog, tenantProfile, tenant, 4).Single(r => r.Category == "balcony").Met == false, "tenant eligibility still waits for actual completion");
        var later = Fresh(decent);
        Planner.Confirm(catalog, later, Planner.AddGoal(catalog, later, fine, 4));
        check(later.Jobs.Any(j => j.UpgradeId == fine.Id && j.Status == JobStatus.Planned) && later.Jobs.Any(j => j.UpgradeId == decent.Id && j.Status == JobStatus.UnderConstruction), "a higher future upgrade remains planned alongside lower work underway");
        var other = Fresh(decent);
        Planner.Confirm(catalog, other, [new() { Apartment = 10, UpgradeId = makeshift.Id }]);
        check(other.Jobs.Count == 2 && other.Jobs.Single(j => j.Apartment == 10).Status == JobStatus.Planned, "underway work never covers another apartment");
        var otherCategory = Fresh(decent);
        Planner.Confirm(catalog, otherCategory, [new() { Apartment = 4, UpgradeId = power.Id }]);
        check(otherCategory.Jobs.Any(j => j.UpgradeId == power.Id && j.Status == JobStatus.Planned), "balcony work cannot cover an unrelated category");
        var dependency = Fresh(power);
        Planner.Confirm(catalog, dependency, [new() { Apartment = 4, UpgradeId = decent.Id }]);
        check(dependency.Jobs.Count(j => j.UpgradeId == power.Id) == 1 && dependency.Jobs.Single(j => j.UpgradeId == power.Id).Status == JobStatus.UnderConstruction, "active prerequisites are reused without billing materials again");
        var shared = Fresh(Named("Courtyard-3-Decent"), 0);
        Planner.Confirm(catalog, shared, [new() { Apartment = 4, UpgradeId = Named("Courtyard-1-Makeshift").Id }]);
        check(shared.Jobs.Count == 1 && shared.Jobs.Single().Apartment == 0 && Planner.Cart(catalog, shared).Count == 0, "higher shared work covers lower building requests regardless of selected apartment");
        var legacy = Fresh(fine);
        legacy.Goals.Add(new() { Apartment = 4, UpgradeId = decent.Id });
        legacy.Jobs.Add(new() { Key = Planner.Key(4, decent.Id), Apartment = 4, UpgradeId = decent.Id });
        legacy.Jobs.Add(new() { Key = Planner.Key(4, power.Id), Apartment = 4, UpgradeId = power.Id });
        string activeBefore = JsonSerializer.Serialize(legacy.Jobs[0], Json.Options);
        check(Planner.ReconcileUnderway(catalog, legacy) && legacy.Jobs.Count == 1 && Planner.Cart(catalog, legacy).Count == 0, "legacy duplicate planned work and orphaned prerequisites are repaired");
        check(JsonSerializer.Serialize(legacy.Jobs.Single(), Json.Options) == activeBefore && legacy.Stock["game:35010"] == 12 && legacy.Goals.Count == 1, "repair preserves active state, stock, and the user's original goal");
        check(!Planner.ReconcileUnderway(catalog, legacy), "underway reconciliation is idempotent");
        var canceled = ProfileStore.Clone(legacy); canceled.Jobs.Clear();
        Planner.Confirm(catalog, canceled, canceled.Goals.ToArray());
        check(canceled.Jobs.Any(j => j.UpgradeId == decent.Id && j.Status == JobStatus.Planned), "retained desired work returns if construction is absent after save synchronization");
        var after = Fresh(decent);
        Planner.Confirm(catalog, after, [new() { Apartment = 4, UpgradeId = makeshift.Id }]);
        Planner.Complete(catalog, after, after.Jobs.Single());
        check(after.Level("balcony", 4) == 3 && Planner.Cart(catalog, after).Count == 0 && after.Jobs.All(j => j.Status == JobStatus.Completed), "completing the higher underway tier cannot resurrect a lower material bill");
        Console.WriteLine("Underway planning: front-balcony tier coverage / tenant goals / apartment and category isolation / shared work / future higher tiers / prerequisite reuse / legacy repair / completion passed.");
    }
}
