using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage;

public sealed record InstalledSaveGame(string GameId, string Name, string Initial, string? IconUrl, string? HeroUrl, string? InstallFolder, int LocationCount, int FileCount, long SizeBytes, DateTimeOffset? LastModified, bool Installed = true, string[]? Locations = null);
public sealed record SaveChange(string Id, string GameId, string GameName, string Type, DateTimeOffset DetectedAt, string? PreviousPath = null, string? CurrentPath = null);
public sealed record SaveLibraryResult(InstalledSaveGame[] Games, int InstalledCount, int WithSavesCount, int OrphanCount, int FileCount, long SizeBytes, DateTimeOffset ScannedAt, bool Limited, SaveChange[]? Changes = null, bool FromCache = false);
public sealed record SaveExportResult(bool Selected, string? Path, int Games, int Files, long SizeBytes, string Format);

public sealed class SaveLibraryService
{
    private sealed class CacheEnvelope
    {
        public int SchemaVersion { get; set; } = 1;
        public SaveLibraryResult? Result { get; set; }
        public Dictionary<string, SaveLocation[]> Searches { get; set; } = [];
        public SaveChange[] History { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SaveFinder finder;
    private readonly Func<IEnumerable<Game>, Dictionary<string, string[]>> detect;
    private readonly string? cachePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private SaveLibraryResult? lastScan;
    private SaveChange[] history = [];
    private readonly Dictionary<string, SaveSearchResult> lastSearches = [];
    private string? lastExport;

    public SaveLibraryService(SaveFinder finder, Func<IEnumerable<Game>, Dictionary<string, string[]>>? detect = null, string? cachePath = null)
    {
        this.finder = finder;
        this.detect = detect ?? SaveEnvironment.DetectInstalls;
        this.cachePath = cachePath;
    }

    public SaveLibraryResult? LoadCached()
    {
        if (lastScan is not null) return lastScan with { FromCache = true, Changes = history };
        if (cachePath is null || !File.Exists(cachePath)) return null;
        try {
            var info = new FileInfo(cachePath);
            if (info.Length is <= 0 or > 5_000_000) return null;
            var cache = JsonSerializer.Deserialize<CacheEnvelope>(File.ReadAllText(cachePath), Json);
            if (cache?.SchemaVersion != 1 || cache.Result is null) return null;
            history = (cache.History ?? []).Take(60).ToArray();
            lastSearches.Clear();
            foreach (var (gameId, locations) in cache.Searches ?? []) {
                var safe = locations.Where(SafeCachedLocation).Take(160).ToArray();
                if (safe.Length > 0) lastSearches[gameId] = SearchFromCache(cache.Result.Games.FirstOrDefault(game => game.GameId == gameId), safe);
            }
            lastScan = cache.Result with { Changes = history, FromCache = true };
            return lastScan;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    public async Task<SaveLibraryResult> ValidateCachedAsync(IEnumerable<Game> source)
    {
        await gate.WaitAsync();
        try {
            var previous = LoadCached();
            if (previous is null) return await ScanCoreAsync(source);
            var games = source.ToDictionary(game => game.Id);
            var installs = await Task.Run(() => detect(games.Values));
            var refreshed = new List<InstalledSaveGame>();
            var refreshedSearches = new Dictionary<string, SaveSearchResult>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool limited = false;
            foreach (var old in previous.Games) {
                if (watch.Elapsed > TimeSpan.FromSeconds(15)) { limited = true; refreshed.Add(old); if (lastSearches.TryGetValue(old.GameId, out var retained)) refreshedSearches[old.GameId] = retained; continue; }
                if (!games.TryGetValue(old.GameId, out var game)) continue;
                var known = lastSearches.GetValueOrDefault(old.GameId)?.Locations ?? [];
                var locations = await finder.RefreshKnownAsync(known);
                bool installed = installs.TryGetValue(old.GameId, out var folders);
                SaveSearchResult? recovered = null;
                if (locations.Length < known.Count(location => location.Kind != "registry")) {
                    recovered = await finder.FindAsync(game, installed ? folders![0] : null);
                    locations = recovered.Locations.Where(location => location.Kind != "registry").ToArray();
                    limited |= recovered.Limited;
                }
                if (!installed && locations.Length == 0) continue;
                string? installFolder = installed ? folders![0] : null;
                refreshed.Add(Summary(game, installFolder, locations, installed));
                refreshedSearches[old.GameId] = recovered ?? SearchFromCache(refreshed[^1], locations);
            }
            lastSearches.Clear(); foreach (var item in refreshedSearches) lastSearches[item.Key] = item.Value;
            return Complete(previous, refreshed, installs.Count, limited, DateTimeOffset.UtcNow);
        } finally { gate.Release(); }
    }

    public async Task<SaveLibraryResult> ScanAsync(IEnumerable<Game> source)
    {
        await gate.WaitAsync();
        try { return await ScanCoreAsync(source); }
        finally { gate.Release(); }
    }

    private async Task<SaveLibraryResult> ScanCoreAsync(IEnumerable<Game> source)
    {
        var previous = lastScan ?? LoadCached();
        var games = source.ToArray();
        var installs = await Task.Run(() => detect(games));
        var results = new List<InstalledSaveGame>();
        lastSearches.Clear();
        var started = System.Diagnostics.Stopwatch.StartNew(); bool limited = false;
        foreach (var game in games.Where(game => installs.ContainsKey(game.Id)).OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).Take(120)) {
            if (started.Elapsed > TimeSpan.FromSeconds(45)) { limited = true; break; }
            var search = await finder.FindAsync(game, installs[game.Id][0]);
            lastSearches[game.Id] = search;
            results.Add(Summary(game, installs[game.Id][0], search.Locations, true));
        }
        if (installs.Count > results.Count) limited = true;
        var orphans = await finder.FindOrphansAsync(games, installs.Keys.ToHashSet(StringComparer.Ordinal));
        foreach (var orphan in orphans.Matches) {
            lastSearches[orphan.Game.Id] = orphan.Search;
            results.Add(Summary(orphan.Game, null, orphan.Search.Locations, false));
        }
        limited |= orphans.Limited;
        return Complete(previous, results, installs.Count, limited, DateTimeOffset.UtcNow);
    }

    private SaveLibraryResult Complete(SaveLibraryResult? previous, List<InstalledSaveGame> games, int installedCount, bool limited, DateTimeOffset now)
    {
        var current = new SaveLibraryResult(games.ToArray(), installedCount, games.Count(game => game.LocationCount > 0), games.Count(game => !game.Installed), games.Sum(game => game.FileCount), games.Sum(game => game.SizeBytes), now, limited);
        SaveChange[] detected = previous is null ? [] : Compare(previous.Games, current.Games, now);
        history = detected.Concat(history).DistinctBy(change => change.Id).Take(60).ToArray();
        lastScan = current with { Changes = history, FromCache = false };
        Persist();
        return lastScan;
    }

    private static InstalledSaveGame Summary(Game game, string? installFolder, IEnumerable<SaveLocation> source, bool installed)
    {
        var locations = source.Where(location => location.Kind != "registry").ToArray();
        return new(game.Id, game.Name, game.Initial, game.IconUrl, game.HeroUrl, installFolder, locations.Length, locations.Sum(location => location.FileCount), locations.Sum(location => location.SizeBytes), locations.Select(location => location.LastModified).Where(date => date is not null).Max(), installed, locations.Select(location => location.Path).ToArray());
    }

    private static SaveChange[] Compare(IEnumerable<InstalledSaveGame> before, IEnumerable<InstalledSaveGame> after, DateTimeOffset now)
    {
        var old = before.ToDictionary(game => game.GameId); var current = after.ToDictionary(game => game.GameId);
        var changes = new List<SaveChange>();
        foreach (var game in current.Values) {
            if (!old.TryGetValue(game.GameId, out var previous)) { changes.Add(Change(game, "added", now, null, game.Locations?.FirstOrDefault())); continue; }
            if (previous.Installed != game.Installed) changes.Add(Change(game, game.Installed ? "installed" : "uninstalled", now, previous.InstallFolder, game.InstallFolder));
            var previousPaths = (previous.Locations ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentPaths = (game.Locations ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = previousPaths.Except(currentPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            var added = currentPaths.Except(previousPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            if (removed.Length > 0 && added.Length > 0) changes.Add(Change(game, "moved", now, removed[0], added[0]));
            else if (removed.Length > 0) changes.Add(Change(game, "removed", now, removed[0], null));
            else if (added.Length > 0) changes.Add(Change(game, "added", now, null, added[0]));
            else if (game.FileCount != previous.FileCount || game.SizeBytes != previous.SizeBytes || game.LastModified != previous.LastModified) changes.Add(Change(game, "modified", now));
        }
        foreach (var game in old.Values.Where(game => !current.ContainsKey(game.GameId))) changes.Add(Change(game, "removed", now, game.Locations?.FirstOrDefault(), null));
        return changes.Take(30).ToArray();
    }

    private static SaveChange Change(InstalledSaveGame game, string type, DateTimeOffset now, string? previous = null, string? current = null)
    {
        string key = $"{game.GameId}|{type}|{previous}|{current}|{now:yyyyMMddHHmmss}";
        return new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..20].ToLowerInvariant(), game.GameId, game.Name, type, now, previous, current);
    }

    private void Persist()
    {
        if (cachePath is null || lastScan is null) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string temporary = cachePath + ".tmp";
            var envelope = new CacheEnvelope { Result = lastScan with { FromCache = false, Changes = [] }, Searches = lastSearches.ToDictionary(item => item.Key, item => item.Value.Locations), History = history };
            File.WriteAllText(temporary, JsonSerializer.Serialize(envelope, Json));
            File.Move(temporary, cachePath, true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    private static bool SafeCachedLocation(SaveLocation location) => location.Path.Length <= 500 && Path.IsPathFullyQualified(location.Path) && !location.Path.StartsWith(@"\\", StringComparison.Ordinal) && location.Kind is "file" or "folder" or "registry";
    private static SaveSearchResult SearchFromCache(InstalledSaveGame? game, SaveLocation[] locations) => new(game?.Name ?? "", null, 0, "", game?.Installed ?? false, game?.InstallFolder, locations, 0, 0, false);

    public async Task<SaveExportResult> ExportAsync(IEnumerable<Game> source, string destination, bool zip)
    {
        destination = SaveFinder.ValidateFolder(destination);
        var scan = lastScan ?? LoadCached() ?? await ScanAsync(source);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string name = "Mirage-Saves-" + stamp;
        string output = Unique(System.IO.Path.Combine(destination, name + (zip ? ".zip" : "")));
        int files = 0, games = 0; long bytes = 0;
        var manifest = new List<object>();
        if (zip) {
            using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
            foreach (var summary in scan.Games.Where(game => game.LocationCount > 0 && lastSearches.ContainsKey(game.GameId))) {
                string game = SafeName(summary.Name); var sources = Sources(lastSearches[summary.GameId].Locations);
                var copied = AddToZip(archive, game, sources, ref files, ref bytes);
                if (copied > 0) { games++; manifest.Add(ManifestGame(summary, game, sources)); }
            }
            AddManifest(archive, manifest, files, bytes);
        } else {
            Directory.CreateDirectory(output);
            foreach (var summary in scan.Games.Where(game => game.LocationCount > 0 && lastSearches.ContainsKey(game.GameId))) {
                string game = SafeName(summary.Name); var sources = Sources(lastSearches[summary.GameId].Locations);
                int copied = CopyToFolder(System.IO.Path.Combine(output, game), game, sources, ref files, ref bytes);
                if (copied > 0) { games++; manifest.Add(ManifestGame(summary, game, sources)); }
            }
            await File.WriteAllTextAsync(System.IO.Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { schemaVersion = 2, createdAt = DateTimeOffset.UtcNow, games = manifest, files, sizeBytes = bytes }, new JsonSerializerOptions { WriteIndented = true }));
        }
        lastExport = output;
        return new(true, output, games, files, bytes, zip ? "zip" : "folder");
    }

    public string ResolveLastExport() => lastExport is not null && (File.Exists(lastExport) || Directory.Exists(lastExport)) ? lastExport : throw new InvalidOperationException("Nenhum backup recente está disponível.");
    public SaveLibraryResult? Current => lastScan ?? LoadCached();
    public SaveLocation[] KnownLocations(string gameId) => lastSearches.GetValueOrDefault(gameId)?.Locations.Where(location => location.Kind != "registry").ToArray() ?? [];
    private static string Unique(string path) { if (!File.Exists(path) && !Directory.Exists(path)) return path; string directory = System.IO.Path.GetDirectoryName(path)!; string stem = System.IO.Path.GetFileNameWithoutExtension(path); string extension = System.IO.Path.GetExtension(path); for (int i = 2; i < 1000; i++) { string candidate = System.IO.Path.Combine(directory, stem + "-" + i + extension); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; } throw new IOException("Não foi possível criar um nome livre para o backup."); }
    private static string SafeName(string name) { string value = string.Concat(name.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.'); return string.IsNullOrWhiteSpace(value) ? "Game" : value[..Math.Min(100, value.Length)]; }
    private static SaveLocation[] Sources(IEnumerable<SaveLocation> locations) { var selected = new List<SaveLocation>(); foreach (var location in locations.Where(location => location.Kind != "registry" && (File.Exists(location.Path) || Directory.Exists(location.Path))).OrderBy(location => location.Path.Length)) { if (!selected.Any(parent => Directory.Exists(parent.Path) && location.Path.StartsWith(parent.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) selected.Add(location); } return selected.ToArray(); }
    private static IEnumerable<(string Path, string Relative)> Files(SaveLocation source, string prefix) { if (File.Exists(source.Path)) { yield return (source.Path, System.IO.Path.Combine(prefix, System.IO.Path.GetFileName(source.Path))); yield break; } var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true, MaxRecursionDepth = 32 }; foreach (string file in Directory.EnumerateFiles(source.Path, "*", options)) yield return (file, System.IO.Path.Combine(prefix, System.IO.Path.GetRelativePath(source.Path, file))); }
    private static string BackupPrefix(string game, int index, SaveLocation source) => System.IO.Path.Combine(game, index.ToString("00") + "-" + SafeName(System.IO.Path.GetFileName(source.Path)));
    private static object ManifestGame(InstalledSaveGame summary, string game, SaveLocation[] sources) => new { gameId = summary.GameId, name = summary.Name, installed = summary.Installed, locations = sources.Select((source, index) => new { originalPath = source.Path, kind = source.Kind, backupPath = (source.Kind == "file" ? System.IO.Path.Combine(BackupPrefix(game, index + 1, source), System.IO.Path.GetFileName(source.Path)) : BackupPrefix(game, index + 1, source)).Replace('\\', '/') }).ToArray() };
    private static int AddToZip(ZipArchive archive, string game, IEnumerable<SaveLocation> locations, ref int fileCount, ref long bytes) { int locationCount = 0; foreach (var source in locations) { locationCount++; string prefix = BackupPrefix(game, locationCount, source); foreach (var file in Files(source, prefix)) { var info = new FileInfo(file.Path); archive.CreateEntryFromFile(file.Path, file.Relative.Replace('\\', '/'), CompressionLevel.Optimal); fileCount++; bytes += info.Length; } } return locationCount; }
    private static int CopyToFolder(string gameFolder, string game, IEnumerable<SaveLocation> locations, ref int fileCount, ref long bytes) { int locationCount = 0; foreach (var source in locations) { locationCount++; string prefix = System.IO.Path.Combine(gameFolder, System.IO.Path.GetFileName(BackupPrefix(game, locationCount, source))); foreach (var file in Files(source, prefix)) { string target = System.IO.Path.Combine(prefix, System.IO.Path.GetRelativePath(prefix, file.Relative)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.Copy(file.Path, target, false); var info = new FileInfo(file.Path); fileCount++; bytes += info.Length; } } return locationCount; }
    private static void AddManifest(ZipArchive archive, List<object> games, int files, long bytes) { var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal); using var stream = entry.Open(); JsonSerializer.Serialize(stream, new { schemaVersion = 2, createdAt = DateTimeOffset.UtcNow, games, files, sizeBytes = bytes }, new JsonSerializerOptions { WriteIndented = true }); }
}
