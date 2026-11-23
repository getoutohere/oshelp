using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OSHelp.Core;

/// <summary>One startup's shared validation session. Unchanged files reuse persisted SHA-256 values.</summary>
public sealed class GameFileValidation
{
    public sealed record Entry(long Length, long WriteTicks, long CreationTicks, string Hash);
    public sealed class Cache
    {
        public int FormatVersion { get; set; } = 1;
        public string DataRoot { get; set; } = "";
        public string SteamBuild { get; set; } = "";
        public Dictionary<string, Entry> Files { get; set; } = [];
    }
    readonly string dataRoot, cachePath;
    readonly bool force;
    readonly Cache cache;
    readonly HashSet<string> verified = new(StringComparer.OrdinalIgnoreCase);
    bool dirty;
    public int FilesHashed { get; private set; }
    public long BytesHashed { get; private set; }
    public int ReusedHashes { get; private set; }

    public GameFileValidation(string gameRoot, string dataDirectory, bool force = false)
    {
        dataRoot = Path.GetFullPath(Path.Combine(gameRoot, "Obenseuer_Data"));
        cachePath = Path.Combine(dataDirectory, "game-file-hashes.json");
        this.force = force;
        var manifest = Path.Combine(gameRoot, "..", "..", "appmanifest_951240.acf");
        var build = File.Exists(manifest) ? Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"([^\"]+)\"").Groups[1].Value : "";
        Cache? saved = null;
        try { if (File.Exists(cachePath)) saved = Json.Read<Cache>(cachePath); }
        catch (Exception e) when (e is JsonException or IOException) { /* A damaged optimization cache can be rebuilt. */ }
        cache = saved is { FormatVersion: 1, Files: not null }
            && string.Equals(saved.DataRoot, dataRoot, StringComparison.OrdinalIgnoreCase) && saved.SteamBuild == build
            ? saved : new Cache { DataRoot = dataRoot, SteamBuild = build };
        cache.Files = new(cache.Files, StringComparer.OrdinalIgnoreCase);
    }
    static Entry Stamp(string path, string hash = "")
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Game input is missing: " + path, path);
        return new(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, hash);
    }
    static bool SameFile(Entry left, Entry right) => left.Length == right.Length && left.WriteTicks == right.WriteTicks && left.CreationTicks == right.CreationTicks;

    public async Task<string> HashAsync(string relativeFile, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(Path.Combine(dataRoot, relativeFile));
        if (!path.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Game input escapes the data directory.");
        var key = Path.GetRelativePath(dataRoot, path).Replace('\\', '/');
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = Stamp(path);
            if ((!force || verified.Contains(key)) && cache.Files.TryGetValue(key, out var entry) && entry != null && SameFile(entry, before)
                && entry.Hash is { Length: 64 } && entry.Hash.All(Uri.IsHexDigit))
            {
                ReusedHashes++;
                return entry.Hash;
            }
            // Prevent writes/replacement during hashing; asynchronous I/O keeps the UI responsive.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
            var after = Stamp(path, hash);
            FilesHashed++; BytesHashed += stream.Length;
            if (!SameFile(before, after)) continue;
            cache.Files[key] = after; verified.Add(key); dirty = true;
            return hash;
        }
        throw new IOException("Game input changed during validation: " + relativeFile);
    }
    public void Save()
    {
        if (!dirty) return;
        AtomicFile.Write(cachePath, JsonSerializer.Serialize(cache, Json.Options));
        dirty = false;
    }
}
