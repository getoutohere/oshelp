using System.Diagnostics;
using System.Text.Json;
using OSHelp.Core;

internal static class StartupBenchmark
{
    public static async Task Run(string repository, string gameRoot)
    {
        // Only catalog/hash caches are read/written. This never opens or changes profiles or carts.
        var data = Path.Combine(repository, "local/userdata");
        async Task<(Catalog Catalog, VendorCatalog Vendors, double Seconds, int Files, long Bytes)> Load(bool full)
        {
            var timer = Stopwatch.StartNew();
            var validation = new GameFileValidation(gameRoot, data, full);
            var catalog = await CatalogImport.LoadAsync(repository, gameRoot, data, false, validation: validation);
            var vendors = await VendorImport.LoadAsync(repository, gameRoot, data, catalog, null, default);
            timer.Stop();
            Console.WriteLine($"{(full ? "Full SHA-256" : "Cached startup")}: {timer.Elapsed.TotalSeconds:F3}s; {validation.FilesHashed} files / {validation.BytesHashed:N0} bytes hashed");
            return (catalog, vendors, timer.Elapsed.TotalSeconds, validation.FilesHashed, validation.BytesHashed);
        }
        var full = await Load(true);
        var warm = await Load(false);
        if (warm.Bytes != 0 || full.Bytes == 0 || full.Catalog.Fingerprint != warm.Catalog.Fingerprint
            || JsonSerializer.Serialize(full.Vendors, Json.Options) != JsonSerializer.Serialize(warm.Vendors, Json.Options)
            || JsonSerializer.Serialize(full.Catalog, Json.Options) != JsonSerializer.Serialize(warm.Catalog, Json.Options))
            throw new Exception("Cached startup must read zero game content bytes and return identical catalog/vendor data.");
        var report = new { timestamp = DateTimeOffset.Now, fullSeconds = full.Seconds, cachedSeconds = warm.Seconds,
            fullFilesHashed = full.Files, fullBytesHashed = full.Bytes, cachedBytesHashed = warm.Bytes,
            speedup = full.Seconds / warm.Seconds, fingerprint = warm.Catalog.Fingerprint, identicalData = true };
        AtomicFile.Write(Path.Combine(repository, "local/startup-benchmark.json"), JsonSerializer.Serialize(report, Json.Options));
    }
}
