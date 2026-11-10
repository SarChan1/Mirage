using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage;

public sealed partial class Preferences
{
    private string? storageDirectory;
    [JsonIgnore] public string? LoadWarning { get; private set; }
    [JsonIgnore] public bool RequiresStorageReplacement { get; private set; }
    private const long MaxFileBytes = 16 * 1024 * 1024;

    public static Preferences Load(string? directory = null)
    {
        directory ??= Paths.Data;
        var path = Path.Combine(directory, "preferences.json");
        Preferences preferences;
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaxFileBytes) throw new JsonException("Preferences exceed the supported size.");
            preferences = JsonSerializer.Deserialize<Preferences>(file) ?? throw new JsonException("Empty preferences.");
            preferences.NormalizeStoredValues();
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            preferences = new();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            preferences = new() {
                LoadWarning = "Não foi possível ler suas preferências. O arquivo original foi preservado; as mudanças ficam apenas nesta sessão.",
                RequiresStorageReplacement = true
            };
        }
        preferences.storageDirectory = directory;
        return preferences;
    }

    private void NormalizeStoredValues()
    {
        static bool GameId(string? id) => id is { Length: >= 10 and <= 22 } && id.All(char.IsAsciiDigit);
        Favorites = (Favorites ?? []).Where(GameId).Take(50_000).ToHashSet();
        Recent = (Recent ?? []).Where(GameId).Distinct().Take(30).ToList();
        ProcessPlaylist = (ProcessPlaylist ?? []).Where(item => item is not null && item.IsValid)
            .DistinctBy(item => item.EntryId).Take(PlaylistWorkspace.MaxEntries).ToList();
        SavedPlaylists = (SavedPlaylists ?? []).Where(item => item is not null && item.IsValid)
            .DistinctBy(item => item.Id).DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        NormalizePlaylistDefaults();
    }

    public void Save(bool replaceUnreadable = false)
    {
        if (RequiresStorageReplacement && !replaceUnreadable) throw new IOException(LoadWarning);
        var directory = storageDirectory ?? Paths.Data;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preferences.json");
        var temporary = path + ".tmp";
        try {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(file, this);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            RequiresStorageReplacement = false; LoadWarning = null;
        } finally {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
