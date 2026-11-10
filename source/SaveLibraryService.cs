using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mirage;

public sealed record InstalledSaveGame(string GameId, string Name, string Initial, string? IconUrl, string? HeroUrl, string? InstallFolder, int LocationCount, int FileCount, long SizeBytes, DateTimeOffset? LastModified, bool Installed = true);
public sealed record SaveLibraryResult(InstalledSaveGame[] Games, int InstalledCount, int WithSavesCount, int OrphanCount, int FileCount, long SizeBytes, DateTimeOffset ScannedAt, bool Limited);
public sealed record SaveExportResult(bool Selected, string? Path, int Games, int Files, long SizeBytes, string Format);

public sealed class SaveLibraryService
{
    private readonly SaveFinder finder;
    private readonly Func<IEnumerable<Game>, Dictionary<string, string[]>> detect;
    private SaveLibraryResult? lastScan;
    private readonly Dictionary<string, SaveSearchResult> lastSearches = [];
    private string? lastExport;
    public SaveLibraryService(SaveFinder finder, Func<IEnumerable<Game>, Dictionary<string, string[]>>? detect = null) { this.finder = finder; this.detect = detect ?? SaveEnvironment.DetectInstalls; }

    public async Task<SaveLibraryResult> ScanAsync(IEnumerable<Game> source)
    {
        var games = source.ToArray();
        var installs = await Task.Run(() => detect(games));
        var results = new List<InstalledSaveGame>();
        lastSearches.Clear();
        var started = System.Diagnostics.Stopwatch.StartNew(); bool limited = false;
        foreach (var game in games.Where(game => installs.ContainsKey(game.Id)).OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).Take(120)) {
            if (started.Elapsed > TimeSpan.FromSeconds(45)) { limited = true; break; }
            var search = await finder.FindAsync(game, installs[game.Id][0]);
            lastSearches[game.Id] = search;
            var locations = search.Locations.Where(location => location.Kind != "registry").ToArray();
            results.Add(new(game.Id, game.Name, game.Initial, game.IconUrl, game.HeroUrl, installs[game.Id][0], locations.Length, locations.Sum(location => location.FileCount), locations.Sum(location => location.SizeBytes), locations.Select(location => location.LastModified).Where(date => date is not null).Max(), true));
        }
        if (installs.Count > results.Count) limited = true;
        var orphans = await finder.FindOrphansAsync(games, installs.Keys.ToHashSet(StringComparer.Ordinal));
        foreach (var orphan in orphans.Matches) {
            var locations = orphan.Search.Locations.Where(location => location.Kind != "registry").ToArray();
            lastSearches[orphan.Game.Id] = orphan.Search;
            results.Add(new(orphan.Game.Id, orphan.Game.Name, orphan.Game.Initial, orphan.Game.IconUrl, orphan.Game.HeroUrl, null, locations.Length, locations.Sum(location => location.FileCount), locations.Sum(location => location.SizeBytes), locations.Select(location => location.LastModified).Where(date => date is not null).Max(), false));
        }
        limited |= orphans.Limited;
        lastScan = new(results.ToArray(), installs.Count, results.Count(game => game.LocationCount > 0), orphans.Matches.Length, results.Sum(game => game.FileCount), results.Sum(game => game.SizeBytes), DateTimeOffset.UtcNow, limited);
        return lastScan;
    }

    public async Task<SaveExportResult> ExportAsync(IEnumerable<Game> source, string destination, bool zip)
    {
        destination = SaveFinder.ValidateFolder(destination);
        var scan = lastScan ?? await ScanAsync(source);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string name = "Mirage-Saves-" + stamp;
        string output = Unique(System.IO.Path.Combine(destination, name + (zip ? ".zip" : "")));
        int files = 0, games = 0; long bytes = 0;
        var manifest = new List<object>();
        if (zip) {
            using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
            foreach (var summary in scan.Games.Where(game => game.LocationCount > 0)) {
                var found = lastSearches[summary.GameId];
                var copied = AddToZip(archive, SafeName(summary.Name), found.Locations, ref files, ref bytes);
                if (copied > 0) { games++; manifest.Add(new { game = summary.Name, locations = copied }); }
            }
            AddManifest(archive, manifest, files, bytes);
        } else {
            Directory.CreateDirectory(output);
            foreach (var summary in scan.Games.Where(game => game.LocationCount > 0)) {
                var found = lastSearches[summary.GameId];
                int copied = CopyToFolder(System.IO.Path.Combine(output, SafeName(summary.Name)), found.Locations, ref files, ref bytes);
                if (copied > 0) { games++; manifest.Add(new { game = summary.Name, locations = copied }); }
            }
            await File.WriteAllTextAsync(System.IO.Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { createdAt = DateTimeOffset.UtcNow, games = manifest, files, sizeBytes = bytes }, new JsonSerializerOptions { WriteIndented = true }));
        }
        lastExport = output;
        return new(true, output, games, files, bytes, zip ? "zip" : "folder");
    }

    public string ResolveLastExport() => lastExport is not null && (File.Exists(lastExport) || Directory.Exists(lastExport)) ? lastExport : throw new InvalidOperationException("Nenhum backup recente está disponível.");
    private static string Unique(string path) { if (!File.Exists(path) && !Directory.Exists(path)) return path; string directory = System.IO.Path.GetDirectoryName(path)!; string stem = System.IO.Path.GetFileNameWithoutExtension(path); string extension = System.IO.Path.GetExtension(path); for (int i = 2; i < 1000; i++) { string candidate = System.IO.Path.Combine(directory, stem + "-" + i + extension); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; } throw new IOException("Não foi possível criar um nome livre para o backup."); }
    private static string SafeName(string name) { string value = string.Concat(name.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim().TrimEnd('.'); return string.IsNullOrWhiteSpace(value) ? "Game" : value[..Math.Min(100, value.Length)]; }
    private static SaveLocation[] Sources(IEnumerable<SaveLocation> locations) {
        var selected = new List<SaveLocation>();
        foreach (var location in locations.Where(location => location.Kind != "registry" && (File.Exists(location.Path) || Directory.Exists(location.Path))).OrderBy(location => location.Path.Length)) {
            if (!selected.Any(parent => Directory.Exists(parent.Path) && location.Path.StartsWith(parent.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) selected.Add(location);
        }
        return selected.ToArray();
    }
    private static IEnumerable<(string Path, string Relative)> Files(SaveLocation source, string prefix) {
        if (File.Exists(source.Path)) { yield return (source.Path, System.IO.Path.Combine(prefix, System.IO.Path.GetFileName(source.Path))); yield break; }
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true, MaxRecursionDepth = 32 };
        foreach (string file in Directory.EnumerateFiles(source.Path, "*", options)) yield return (file, System.IO.Path.Combine(prefix, System.IO.Path.GetRelativePath(source.Path, file)));
    }
    private static int AddToZip(ZipArchive archive, string game, IEnumerable<SaveLocation> locations, ref int fileCount, ref long bytes) { int locationCount = 0; foreach (var source in Sources(locations)) { locationCount++; string prefix = System.IO.Path.Combine(game, locationCount.ToString("00") + "-" + SafeName(System.IO.Path.GetFileName(source.Path))); foreach (var file in Files(source, prefix)) { var info = new FileInfo(file.Path); archive.CreateEntryFromFile(file.Path, file.Relative.Replace('\\', '/'), CompressionLevel.Optimal); fileCount++; bytes += info.Length; } } return locationCount; }
    private static int CopyToFolder(string gameFolder, IEnumerable<SaveLocation> locations, ref int fileCount, ref long bytes) { int locationCount = 0; foreach (var source in Sources(locations)) { locationCount++; string prefix = System.IO.Path.Combine(gameFolder, locationCount.ToString("00") + "-" + SafeName(System.IO.Path.GetFileName(source.Path))); foreach (var file in Files(source, prefix)) { string target = System.IO.Path.Combine(prefix, System.IO.Path.GetRelativePath(prefix, file.Relative)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!); File.Copy(file.Path, target, false); var info = new FileInfo(file.Path); fileCount++; bytes += info.Length; } } return locationCount; }
    private static void AddManifest(ZipArchive archive, List<object> games, int files, long bytes) { var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal); using var stream = entry.Open(); JsonSerializer.Serialize(stream, new { createdAt = DateTimeOffset.UtcNow, games, files, sizeBytes = bytes }, new JsonSerializerOptions { WriteIndented = true }); }
}
