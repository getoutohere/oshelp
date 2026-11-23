using System.Text.Json;
using OSHelp.Core;

internal static class VendorChecks
{
    public static void Run(string repository, Action<bool, string> check)
    {
        var data = Json.Read<VendorCatalog>(Path.Combine(repository, "docs/evidence/vendors.json"));
        check(data.FormatVersion == 2 && data.FilesScanned == 250 && data.Inputs.Count == 253, "vendor catalog covers all installed asset/scene files with hashes");
        check(data.Vendors.Count == 135 && data.Vendors.Select(v => v.Id).Distinct().Count() == 135, "all 135 actual vendor/menu placements have unique source identities");
        var rawDirectory = Path.Combine(repository, "local/vendor-research");
        if (File.Exists(Path.Combine(rawDirectory, "vendor-records.json")) && File.Exists(Path.Combine(rawDirectory, "vendor-extra-records.json")))
        {
            var rows = Json.Read<List<VendorRecord>>(Path.Combine(rawDirectory, "vendor-records.json"));
            rows.AddRange(Json.Read<List<VendorRecord>>(Path.Combine(rawDirectory, "vendor-extra-records.json")));
            rows.AddRange(Json.Read<List<VendorRecord>>(Path.Combine(rawDirectory, "vendor-labels.json")));
            var rebuilt = VendorImport.Build(rows, Json.Read<List<InputHash>>(Path.Combine(rawDirectory, "vendor-inputs.json")), data.FilesScanned);
            check(JsonSerializer.Serialize(rebuilt, Json.Options) == JsonSerializer.Serialize(data, Json.Options), "fresh vendor/door/actor/dialogue graph reconstruction matches the complete reviewed catalog");
        }
        check(data.ComponentCounts["Trade"] == 40 && data.ComponentCounts["TradeSpecial"] == 2 && data.ComponentCounts["VendingMachine"] == 68, "independent shop systems included in scan");
        check(data.Vendors.All(v => v.Name.Length > 0 && v.Location.Length > 0 && v.Evidence.Count > 0 && v.Operator != "Player" && v.Operator != "Default Owner"), "meaningful labels, provenance, and no false player/default operator");
        check(data.Vendors.Single(v => v.Id == "level111:619726").Operator == "", "self-service machine does not inherit an unrelated NPC under a shared scene root");
        check(data.TimedDoors.Any(d => d.Source == "level118:123051" && d.Scene == "Interior Bazaar Mallment"), "additional timed internal doors retained for route-specific reference");
        foreach (var v in data.Vendors)
        {
            v.Trading?.Validate(); v.EntranceHours?.Validate(); v.Accessible?.Validate();
            foreach (var door in v.Entrances.Concat(v.InternalAccess)) { door.Hours?.Validate(); check(door.Source.Length > 0 && door.Evidence.Count > 0, "entrance hours have target-specific source evidence"); }
        }
        var general = data.Vendors.Single(v => v.Id == "level77:40294");
        check(general.Operator == "Ivan Kapustanov" && general.Entrances.Count == 1 && general.Entrances[0].Source == "level112:305626", "real Bazaar entrance linked; background scenery door excluded");
        string[] expected = ["08:00–15:00", "10:00–18:00", "10:00–14:00", "08:00–17:00", "13:00–18:00", "08:00–12:00", "13:00–18:00"];
        for (int d = 0; d < 7; d++) check(general.Accessible!.Format(d) == expected[d], "General Trader exact weekly overlap, day " + d);
        check(general.Trading!.Contains(5, 810) && general.Status(5, 810) == "Entrance closed", "Saturday 13:30 regression: trading component open but shop door locked");
        check(general.Status(5, 420) == "Trader closed" && general.Status(5, 480) == "Within access hours" && general.Status(5, 719) == "Within access hours" && general.Status(5, 720) == "Entrance closed", "opening inclusive, noon closing exclusive");
        check(general.Entrances[0].Evidence.Contains("level112:305626.UnLock") && general.Entrances[0].Evidence.Contains("level112:305626.Lock"), "both lock and unlock callbacks resolve to the same actual door");
        check(data.Vendors.Single(v => v.Id == "level93:39179").Accessible!.Format(5) == "10:00–16:00", "Bakery dialogue/vending menu uses external entrance hours");
        check(data.Vendors.Single(v => v.Id == "level2:188361").Trading!.Format(5) == "12:00–20:00", "Teuvo's no-auto-close Trade receives its actor activation timetable");
        check(data.Vendors.Single(v => v.Id == "level12:41423").Accessible!.Format(5) == "10:00–16:00", "alternate Mr. Bag actor paths are unioned, not intersected into permanent closure");
        check(data.Vendors.Single(v => v.Id == "level118:116850").InternalAccess.Any(d => d.Source == "level118:127901") && data.Vendors.Single(v => v.Id == "level118:116850").Accessible!.Format(5) == "08:00–22:00", "physical internal shop door included separately from scene-change entrance");
        check(data.Vendors.Single(v => v.Id == "level101:42291").Operator == "Anneli Tuoppi" && data.Vendors.Single(v => v.Id == "level101:42328").Operator == "Jooseppi Tuoppi", "dialogue menu operator takes precedence over shared vending ownership");
        check(!data.Vendors.Any(v => v.Id == "level111:629359") && data.Vendors.Single(v => v.Id == "level111:649879").Notes.Any(n => n.StartsWith("Inactive", StringComparison.Ordinal)), "test train shop excluded and actual inactive shop annotated");
        using var source = JsonDocument.Parse("""
            {"opensOnMonday":22,"closesOnMonday":6,"opensOnTuesday":8,"closesOnTuesday":18,"opensOnWednesday":0,"closesOnWednesday":0,"opensOnThursday":0,"closesOnThursday":24,"opensOnFriday":23,"closesOnFriday":0,"opensOnSaturday":0,"closesOnSaturday":1,"opensOnSunday":12,"closesOnSunday":12}
            """);
        var overnight = VendorImport.OpeningTimes(source.RootElement);
        check(overnight.Contains(0, 0) && !overnight.Contains(0, 360) && overnight.Contains(0, 1320) && !overnight.Contains(1, 0), "overnight uses current weekday's hours exactly as game IsOpenHour does");
        check(overnight.Format(0) == "00:00–06:00, 22:00–24:00" && overnight.Format(2) == "Closed" && overnight.Format(3) == "24 hours" && overnight.Format(4) == "23:00–24:00", "split overnight, equal-hour closure, and midnight formatting");
        var inverted = overnight.Invert(); var union = VendorHours.Combine([overnight, inverted], false); var intersection = VendorHours.Combine([overnight, inverted], true);
        check(Enumerable.Range(0, 7).All(d => union.Format(d) == "24 hours" && intersection.Format(d) == "Closed"), "schedule complement and alternative/required route composition");
        var unknown = new Vendor { Trading = VendorHours.AllDay(), Entrances = [new() { InitiallyLocked = true }] };
        check(unknown.Accessible == null && unknown.Status(0, 720) == "Check access conditions", "a static locked entrance is not represented as always open or permanently timed shut");
        unknown.Entrances.Add(new() { Hours = overnight });
        check(unknown.Accessible!.Contains(0, 1320) && unknown.Status(0, 720) == "Scheduled entrance closed", "known alternative route remains useful without inventing hours for a quest/key-locked door");
        var gated = new Vendor { Trading = VendorHours.AllDay(), Entrances = [new() { Hours = VendorHours.AllDay(), Notes = ["Requires a quest relay condition."] }] };
        check(gated.Status(0, 720) == "Within hours · conditional", "entrance relay prerequisites qualify the vendor's status");
        var directory = VendorDirectory.Build(data.Vendors);
        VendorListing Shop(string source) => directory.Single(v => v.Sources.Any(s => s.Id == source));
        check(directory.SelectMany(v => v.Sources).Select(v => v.Id).Order().SequenceEqual(data.Vendors.Select(v => v.Id).Order()), "shop grouping retains each raw source exactly once");
        var combinedGeneral = Shop("level77:40294");
        var hardware = Shop("level2:188941");
        check(hardware.Name == "Hardware Stall" && hardware.Operator == "Henning Torskvær", "public storage label names Henning's hardware stall");
        check(hardware.OpeningHours!.Format(1) == "12:00–18:00" && hardware.Status(1, 720) == "Open" && hardware.Status(1, 719) == "Closed" && hardware.Status(1, 1080) == "Closed", "reported hardware stall Tuesday noon regression and exact boundaries");
        check(hardware.Sources.Single().Aliases.Contains("Hardware Stall") && hardware.Sources.Single().Name == "Henning's shop" && hardware.Sources.Single().Evidence.Any(e => e.Contains("level2:205533")), "stall name is joined by actual shared GameObject and preserves the original trade title");
        check(directory.Count(v => v.Name == "Hardware Stall") == 1 && !Shop("level2:191015").Sources.Single().Aliases.Contains("Hardware Stall") && !Shop("level2:191913").Sources.Single().Aliases.Contains("Hardware Stall"), "copied hardware storage titles do not mislabel plant and trinket traders");
        check(combinedGeneral.Sources.Count == 2 && combinedGeneral.Name == "General Trader" && combinedGeneral.OpeningHours!.Format(5) == "08:00–12:00" && combinedGeneral.Status(5, 810) == "Closed" && combinedGeneral.Status(5, 450) == "Closed", "secondary untimed furniture menu never expands General Trader opening hours");
        check(Shop("level91:37057") == Shop("level91:37064"), "Alco checkout and trade menus appear as one shop");
        check(Shop("level107:34904").Sources.Count == 3 && Shop("level107:34904") != Shop("level118:123106"), "Ankea menus collapse within a shop and retain the separate Mallment branch");
        var bar = Shop("level101:42291");
        check(bar == Shop("level101:42328") && bar.Operator.Contains("Anneli") && bar.Operator.Contains("Jooseppi"), "Bar Bizarre is one shop with both operators");
        check(Shop("level70:29201") == Shop("level70:29221"), "Welho stock alternatives appear once");
        check(Shop("level118:116850") == Shop("level118:123106") && Shop("level118:116850").OpeningHours!.Format(5) == "08:00–22:00", "grouped checkout retains the secondary furniture menu's physical door restriction");
        check(Shop("level112:294918") == Shop("level59:30514") && Shop("level59:30514").OpeningHours!.Format(5) == "08:00–20:00", "exterior shop copy does not remove storefront door restrictions");
        check(Shop("level100:51864") == Shop("level62:35486") && !Shop("level62:35486").Inactive, "inactive memory does not duplicate or disable an active vendor");
        Console.WriteLine($"Vendor directory: {directory.Count} shops/machine groups from {data.Vendors.Count} menu placements; deduplication and combined hours passed.");
        Console.WriteLine($"Vendors: {data.Vendors.Count} placements, weekly/trader/door/actor/dialogue/overnight/access checks passed.");
    }
}
