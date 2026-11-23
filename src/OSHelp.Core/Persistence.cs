using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace OSHelp.Core;

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = Encoding.UTF8.GetBytes(content); stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public sealed record ProfileNameMatch(Guid Id, string Name, int Revision, DateTimeOffset SavedAt, string FileHash);

public sealed class ProfileStore : IDisposable
{
    readonly string directory;
    FileStream? lease;
    Guid? leasedId;
    public ProfileStore(string directory) { this.directory = directory; Directory.CreateDirectory(directory); }
    public IReadOnlyList<(Guid Id, string Name)> List() => Directory.EnumerateFiles(directory, "*.json").Select(file =>
    {
        try { var p = Json.Read<Profile>(file); return (p.Id, p.Name); }
        catch { return (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) ? id : Guid.Empty, "[Damaged] " + Path.GetFileNameWithoutExtension(file)); }
    }).Where(x => x.Item1 != Guid.Empty).OrderBy(x => x.Item2).ToArray();
    public string PathFor(Guid id) => Path.Combine(directory, id.ToString("D") + ".json");
    public IReadOnlyList<ProfileNameMatch> FindNameMatches(string name)
    {
        var matches = new List<ProfileNameMatch>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var bytes = File.ReadAllBytes(file); Profile? candidate;
            try { using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true); candidate = JsonSerializer.Deserialize<Profile>(reader.ReadToEnd(), Json.Options); }
            catch (JsonException) { continue; } // A damaged profile has no usable name; normal recovery remains available.
            if (candidate?.Name == null || !string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) || candidate.Id != id) throw new InvalidDataException("Profile filename and identity do not match: " + file);
            matches.Add(new(candidate.Id, candidate.Name, candidate.Revision, candidate.SavedAt, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return matches.OrderBy(m => m.Id).ToArray();
    }
    public static ProfileNameMatch? ChooseImportTarget(IReadOnlyList<ProfileNameMatch> matches, Guid? preferredId) =>
        matches.FirstOrDefault(m => m.Id == preferredId) ?? matches.OrderByDescending(m => m.SavedAt).ThenBy(m => m.Id).FirstOrDefault();

    public Profile SaveGameImport(Profile imported, IReadOnlyList<ProfileNameMatch>? approvedOverwrite = null, Guid? preferredId = null)
    {
        Validate(imported);
        if (imported.ImportedSave == null) throw new InvalidDataException("This profile did not come from a game save.");
        using var importLock = new FileStream(Path.Combine(directory, ".save-import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var matches = FindNameMatches(imported.Name);
        if (approvedOverwrite == null ? matches.Count > 0 : !matches.SequenceEqual(approvedOverwrite.OrderBy(m => m.Id)))
            throw new IOException("A profile with this name exists or changed since the overwrite prompt. Import again to review the current profile before replacing it.");
        var target = ChooseImportTarget(matches, preferredId);
        var replacement = Clone(imported); replacement.Name = imported.Name.Trim(); replacement.Id = target?.Id ?? Guid.NewGuid();
        replacement.Revision = checked((target?.Revision ?? 0) + 1); replacement.SavedAt = DateTimeOffset.UtcNow;
        var held = new Dictionary<Guid, FileStream>(); var moved = new List<(string Original, string Archive)>();
        try
        {
            // Hold all matching profiles before modifying anything, including older duplicates.
            foreach (var id in matches.Select(m => m.Id).Append(replacement.Id).Distinct().Order())
            {
                if (leasedId == id && lease != null) continue;
                try { held[id] = new FileStream(PathFor(id) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException e) { throw new IOException("An existing profile with this name is open in another OSHelp instance. Close it before overwriting.", e); }
            }
            if (!FindNameMatches(imported.Name).SequenceEqual(matches)) throw new IOException("The existing profile changed during import. Import again before overwriting.");
            var duplicates = matches.Where(m => m.Id != replacement.Id).ToArray();
            if (duplicates.Length > 0)
            {
                var archive = Path.Combine(directory, "replaced", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(archive);
                foreach (var duplicate in duplicates)
                {
                    var original = PathFor(duplicate.Id); var destination = Path.Combine(archive, duplicate.Id.ToString("D") + ".json");
                    File.Move(original, destination); moved.Add((original, destination));
                }
            }
            AtomicFile.Write(PathFor(replacement.Id), JsonSerializer.Serialize(replacement, Json.Options));
            // Keep the selected profile's write lease after a successful commit.
            if (held.Remove(replacement.Id, out var nextLease)) { lease?.Dispose(); lease = nextLease; leasedId = replacement.Id; }
            return replacement;
        }
        catch
        {
            foreach (var move in moved.AsEnumerable().Reverse()) if (!File.Exists(move.Original)) File.Move(move.Archive, move.Original);
            throw;
        }
        finally { foreach (var stream in held.Values) stream.Dispose(); }
    }
    public Profile Load(Guid id, bool backup = false)
    {
        var profile = Json.Read<Profile>(PathFor(id) + (backup ? ".bak" : ""));
        Validate(profile); Acquire(profile.Id); return profile;
    }
    void Acquire(Guid id)
    {
        if (leasedId == id && lease != null) return;
        FileStream next;
        try { next = new FileStream(PathFor(id) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("This profile is open in another OSHelp instance. Close that instance or use a separate profile.", e); }
        lease?.Dispose(); lease = next; leasedId = id;
    }
    public void Save(Profile profile, bool restore = false)
    {
        Validate(profile); Acquire(profile.Id);
        var file = PathFor(profile.Id);
        if (File.Exists(file) && !restore)
        {
            var prior = Json.Read<Profile>(file);
            if (prior.Revision != profile.Revision) throw new IOException("The profile has a newer revision on disk. Reload before saving.");
        }
        int revision = profile.Revision; profile.Revision++; profile.SavedAt = DateTimeOffset.UtcNow;
        try { AtomicFile.Write(file, JsonSerializer.Serialize(profile, Json.Options)); }
        catch { profile.Revision = revision; throw; }
    }
    public Profile Import(string file, Catalog catalog)
    {
        var profile = Json.Read<Profile>(file); Validate(profile);
        if (profile.CatalogFingerprint != catalog.Fingerprint) throw new InvalidDataException("The imported profile uses a different game catalog. Import it using its original installation or reconcile it explicitly.");
        profile.Id = Guid.NewGuid(); profile.Revision = 0; profile.Name += " (imported)"; Save(profile); return profile;
    }
    public void SaveSync(Profile original, Profile updated)
    {
        if (updated.Id != original.Id || updated.Revision != original.Revision || updated.Name != original.Name)
            throw new InvalidDataException("Sync must preserve the current profile identity and revision.");
        Acquire(original.Id);
        if (!File.Exists(PathFor(original.Id))) throw new IOException("This profile was deleted. Sync did not recreate it.");
        Save(updated);
    }
    public string Delete(Profile expected)
    {
        using var importLock = new FileStream(Path.Combine(directory, ".save-import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Acquire(expected.Id);
        var file = PathFor(expected.Id); var actual = Json.Read<Profile>(file);
        if (actual.Id != expected.Id || actual.Revision != expected.Revision || actual.SavedAt != expected.SavedAt)
            throw new IOException("The profile changed since it was loaded. Reload it before deleting.");
        var archive = Path.Combine(directory, "deleted", expected.Id.ToString("D") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        var backup = file + ".bak"; var archivedBackup = Path.Combine(archive, Path.GetFileName(backup));
        bool movedBackup = false;
        try
        {
            if (File.Exists(backup)) { File.Move(backup, archivedBackup); movedBackup = true; }
            File.Move(file, Path.Combine(archive, Path.GetFileName(file)));
        }
        catch { if (movedBackup) File.Move(archivedBackup, backup); throw; }
        lease?.Dispose(); lease = null; leasedId = null;
        return archive;
    }
    public static Profile Clone(Profile p) => JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p, Json.Options), Json.Options)!;
    public static void Validate(Profile p)
    {
        if (p.SchemaVersion != 1) throw new InvalidDataException("Unsupported profile version: " + p.SchemaVersion);
        if (p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name)) throw new InvalidDataException("Invalid profile identity");
        foreach (var multiplier in new[] { p.ResourceMultiplier, p.MoneyMultiplier }) if (!float.IsFinite(multiplier) || multiplier < 0 || multiplier > 4) throw new InvalidDataException("Invalid cost multiplier");
        if (!float.IsFinite(p.SpeedMultiplier) || p.SpeedMultiplier <= 0 || p.SpeedMultiplier > 4) throw new InvalidDataException("Invalid speed multiplier");
        if (p.RentalIncomeMultiplier is float income && (!float.IsFinite(income) || income < 0 || income > 4)) throw new InvalidDataException("Invalid rental income multiplier");
        if (p.RentPaymentMode is int mode && mode is < 0 or > 4) throw new InvalidDataException("Invalid rent payment schedule");
        if (p.Stock.Any(x => x.Value < 0) || p.Apartments.Select(x => x.Number).Distinct().Count() != p.Apartments.Count) throw new InvalidDataException("Invalid profile stock/apartments");
        if (p.Jobs.Select(x => x.Key).Distinct().Count() != p.Jobs.Count) throw new InvalidDataException("Duplicate scoped construction jobs");
    }
    public void Dispose() => lease?.Dispose();
}
