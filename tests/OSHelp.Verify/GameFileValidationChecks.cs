using System.Text.Json;
using OSHelp.Core;

internal static class GameFileValidationChecks
{
    public static async Task Run(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "validation/steamapps/common/Game");
        var data = Path.Combine(root, "Obenseuer_Data"); Directory.CreateDirectory(data);
        var output = Path.Combine(scratch, "validation/cache");
        var manifest = Path.GetFullPath(Path.Combine(root, "../../appmanifest_951240.acf"));
        File.WriteAllText(manifest, "\"buildid\" \"123\"");
        var file = Path.Combine(data, "level0"); File.WriteAllText(file, "original");
        var initial = new GameFileValidation(root, output);
        var hash = await initial.HashAsync("level0"); initial.Save();
        check(hash == CatalogImport.Hash(file) && initial.FilesHashed == 1, "first verification hashes actual file bytes");
        check(await initial.HashAsync("level0") == hash && initial.FilesHashed == 1, "shared session avoids duplicate hashing");
        var warm = new GameFileValidation(root, output);
        check(await warm.HashAsync("level0") == hash && warm.BytesHashed == 0, "new process/session reuses persisted hash without reading game contents");
        File.WriteAllText(Path.Combine(data, "level1"), "unchanged sibling");
        await warm.HashAsync("level1"); warm.Save();
        warm = new GameFileValidation(root, output);
        File.WriteAllText(file, "modified"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        check(await warm.HashAsync("level0") != hash && warm.FilesHashed == 1, "same-length edits invalidate by timestamp"); warm.Save();
        await warm.HashAsync("level1");
        check(warm.FilesHashed == 1, "changed input does not cause unchanged siblings to be read again");
        File.AppendAllText(file, "more");
        check(await warm.HashAsync("level0") == CatalogImport.Hash(file) && warm.FilesHashed == 2, "changed length invalidates even within same session"); warm.Save();
        var force = new GameFileValidation(root, output, true);
        await force.HashAsync("level0"); await force.HashAsync("level0"); force.Save();
        check(force.FilesHashed == 1, "explicit full verification bypasses persisted metadata once per unique input");
        var stamp = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "same size!!!"); File.SetLastWriteTimeUtc(file, stamp);
        var fullCheck = new GameFileValidation(root, output, true);
        check(await fullCheck.HashAsync("level0") == CatalogImport.Hash(file) && fullCheck.FilesHashed == 1, "full verification reads content even when timestamps are preserved");
        File.WriteAllText(manifest, "\"buildid\" \"124\"");
        var updated = new GameFileValidation(root, output); await updated.HashAsync("level0"); updated.Save();
        check(updated.FilesHashed == 1, "Steam build change invalidates persisted hashes");
        var otherRoot = Path.Combine(scratch, "validation/otherGame"); Directory.CreateDirectory(Path.Combine(otherRoot, "Obenseuer_Data"));
        File.Copy(file, Path.Combine(otherRoot, "Obenseuer_Data/level0"));
        var other = new GameFileValidation(otherRoot, output); await other.HashAsync("level0"); other.Save();
        check(other.FilesHashed == 1, "cache cannot be reused for a different installation");
        var cacheFile = Path.Combine(output, "game-file-hashes.json"); File.WriteAllText(cacheFile, "broken JSON");
        var recovered = new GameFileValidation(root, output); await recovered.HashAsync("level0"); recovered.Save();
        check(recovered.FilesHashed == 1, "corrupt optimization cache rebuilds from game files");
        var cache = Json.Read<GameFileValidation.Cache>(cacheFile); cache.Files["level0"] = cache.Files["level0"] with { Hash = "invalid" };
        File.WriteAllText(cacheFile, JsonSerializer.Serialize(cache, Json.Options));
        var malformed = new GameFileValidation(root, output); await malformed.HashAsync("level0");
        check(malformed.FilesHashed == 1, "malformed cached digest is rehashed");
        try { await malformed.HashAsync("level0", new CancellationToken(true)); check(false, "cancellation required"); }
        catch (OperationCanceledException) { check(true, "cached path honors cancellation"); }
        try { await malformed.HashAsync("../outside"); check(false, "path containment required"); }
        catch (InvalidDataException) { check(true, "game input paths stay inside game data"); }
        File.Delete(file);
        try { await recovered.HashAsync("level0"); check(false, "missing file must fail"); }
        catch (FileNotFoundException) { check(true, "deleted game input cannot reuse cached evidence"); }
    }
}
