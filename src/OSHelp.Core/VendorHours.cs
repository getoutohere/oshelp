using System.Globalization;

namespace OSHelp.Core;

public sealed record HourWindow(int Start, int End);
public sealed class VendorHours
{
    public List<List<HourWindow>> Days { get; set; } = Enumerable.Range(0, 7).Select(_ => new List<HourWindow>()).ToList();
    public static readonly string[] DayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    public static VendorHours AllDay() => new() { Days = Enumerable.Range(0, 7).Select(_ => new List<HourWindow> { new(0, 1440) }).ToList() };
    public bool Contains(int day, int minute) => Days[day].Any(w => minute >= w.Start && minute < w.End);
    public static VendorHours Combine(IEnumerable<VendorHours> schedules, bool intersection)
    {
        var all = schedules.ToArray(); var result = new VendorHours();
        for (int day = 0; day < 7; day++)
        {
            int start = -1;
            for (int minute = 0; minute <= 1440; minute++)
            {
                bool open = minute < 1440 && (intersection ? all.All(s => s.Contains(day, minute)) : all.Any(s => s.Contains(day, minute)));
                if (open && start < 0) start = minute;
                if (!open && start >= 0) { result.Days[day].Add(new(start, minute)); start = -1; }
            }
        }
        return result;
    }
    public VendorHours Invert() => Combine([this], true).Complement();
    VendorHours Complement()
    {
        var result = new VendorHours();
        for (int d = 0; d < 7; d++)
        {
            int start = 0;
            foreach (var w in Days[d]) { if (w.Start > start) result.Days[d].Add(new(start, w.Start)); start = w.End; }
            if (start < 1440) result.Days[d].Add(new(start, 1440));
        }
        return result;
    }
    public string Format(int day) => Days[day].Count == 0 ? "Closed" : Days[day].Count == 1 && Days[day][0] == new HourWindow(0, 1440) ? "24 hours" : string.Join(", ", Days[day].Select(w => Clock(w.Start) + "–" + Clock(w.End)));
    public static string Clock(int minute) => (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", CultureInfo.InvariantCulture);
    public void Validate()
    {
        if (Days.Count != 7) throw new InvalidDataException("A vendor schedule must have seven days.");
        foreach (var day in Days) { int previous = 0; foreach (var w in day) { if (w.Start < previous || w.End <= w.Start || w.End > 1440) throw new InvalidDataException("Invalid vendor opening interval."); previous = w.End; } }
    }
}

public sealed class VendorEntrance
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Scene { get; set; } = "";
    public VendorHours? Hours { get; set; }
    public bool InitiallyLocked { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
}
public sealed class Vendor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public string Scene { get; set; } = "";
    public string Location { get; set; } = "";
    public VendorHours? Trading { get; set; }
    public List<VendorEntrance> Entrances { get; set; } = [];
    public List<VendorEntrance> InternalAccess { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public VendorHours? EntranceHours
    {
        get
        {
            var entrance = Entrances.Count == 0 ? VendorHours.AllDay() : Entrances.All(e => e.Hours == null) ? null : VendorHours.Combine(Entrances.Where(e => e.Hours != null).Select(e => e.Hours!), false);
            return entrance != null && InternalAccess.All(e => e.Hours != null) ? VendorHours.Combine(InternalAccess.Select(e => e.Hours!).Prepend(entrance), true) : null;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public VendorHours? Accessible => Trading is { } trade && EntranceHours is { } door ? VendorHours.Combine([trade, door], true) : null;
    public string Status(int day, int minute)
    {
        if (Trading != null && !Trading.Contains(day, minute)) return "Trader closed";
        if (EntranceHours is { } door && !door.Contains(day, minute)) return Entrances.Any(e => e.Hours == null) ? "Scheduled entrance closed" : "Entrance closed";
        if (Trading == null || EntranceHours == null) return "Check access conditions";
        return Notes.Concat(Entrances.Concat(InternalAccess).SelectMany(e => e.Notes)).Any(n => n.StartsWith("Requires", StringComparison.Ordinal) || n.StartsWith("Inactive", StringComparison.Ordinal)) ? "Within hours · conditional" : "Within access hours";
    }
}
public sealed class VendorCatalog
{
    public const int CurrentFormatVersion = 4;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public int FilesScanned { get; set; }
    public List<InputHash> Inputs { get; set; } = [];
    public List<Vendor> Vendors { get; set; } = [];
    public List<VendorEntrance> TimedDoors { get; set; } = [];
    public Dictionary<string, int> ComponentCounts { get; set; } = [];
}
