using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage;
public static class Paths
{
    public static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mirage");
}
public sealed class Game
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Aliases { get; set; } = [];
    public string? Icon { get; set; }
    public string? Cover { get; set; }
    public string? SteamId { get; set; }
    public GameExecutable[] Executables { get; set; } = [];
    [JsonIgnore] public string Initial => Name.Length > 0 ? StringInfo.GetNextTextElement(Name) : "?";
    [JsonIgnore] public string? IconUrl => Icon is not null ? $"https://cdn.discordapp.com/app-icons/{Id}/{Icon}.png?size=128" : null;
    [JsonIgnore] public string? HeroUrl => SteamId is not null ? $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{SteamId}/header.jpg" : Cover is not null ? $"https://cdn.discordapp.com/app-assets/{Id}/store/{Cover}.jpg?size=512" : IconUrl;
    [JsonIgnore] public string SearchKey { get; set; } = "";
    [JsonIgnore] internal List<GameExecutable>? CachedExecutables { get; set; }
    [JsonIgnore] public bool SupportsPlaylist => CachedExecutables?.Count > 0 || (Executables ?? []).Any(ProcessSession.IsSupportedOption);
}
public sealed class GameExecutable
{
    public string Name { get; set; } = "";
    public string Os { get; set; } = "";
    public bool IsLauncher { get; set; }
}
public sealed partial class Preferences
{
    public HashSet<string> Favorites { get; set; } = [];
    public List<string> Recent { get; set; } = [];
    public bool MinimizeToTray { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool ProcessWindow { get; set; } = true;
    public List<ProcessPlaylistItem> ProcessPlaylist { get; set; } = [];
    public List<SavedPlaylist> SavedPlaylists { get; set; } = [];
    public int PlaylistDefaultMinutes { get; set; } = 16;
    public int PlaylistDefaultsVersion { get; set; }
    public void NormalizePlaylistDefaults()
    {
        if (PlaylistDefaultsVersion < 1 && PlaylistDefaultMinutes == 15) PlaylistDefaultMinutes = 16;
        PlaylistDefaultMinutes = Math.Clamp(PlaylistDefaultMinutes, 1, 1440);
        PlaylistDefaultsVersion = 1;
    }
}
public sealed class ProcessPlaylistItem
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");
    public string GameId { get; set; } = "";
    public string GameName { get; set; } = "";
    public string Executable { get; set; } = "";
    public int DurationMinutes { get; set; } = 16;
    public bool ShowWindow { get; set; } = true;
    [JsonIgnore] public bool IsValid => Guid.TryParseExact(EntryId, "N", out _) && GameId is { Length: >= 10 and <= 22 } && GameId.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(GameName) && !string.IsNullOrWhiteSpace(Executable) && DurationMinutes is >= 1 and <= 1440;
    public ProcessPlaylistItem Copy() => new() { EntryId = EntryId, GameId = GameId, GameName = GameName, Executable = Executable, DurationMinutes = DurationMinutes, ShowWindow = ShowWindow };
}
public static class GameSearch
{
    public static string Normalize(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(char.ToLowerInvariant(c));
        return b.ToString().Normalize(NormalizationForm.FormC);
    }
    public static void Prepare(IEnumerable<Game> games)
    {
        foreach (var g in games) {
            g.SearchKey = Normalize(g.Name + " " + string.Join(" ", g.Aliases ?? []));
            g.Executables = (g.Executables ?? []).Where(ProcessSession.IsSupportedOption).DistinctBy(executable => executable.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var executable in g.Executables) executable.Os = "win32";
            g.CachedExecutables = null;
        }
    }
    public static IEnumerable<Game> Find(IEnumerable<Game> games, string query)
    {
        var q = Normalize(query.Trim());
        if (q.Length == 0) return games;
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return games.Where(g => tokens.All(t => g.SearchKey.Contains(t, StringComparison.Ordinal)))
            .OrderBy(g => Normalize(g.Name) == q ? 0 : Normalize(g.Name).StartsWith(q, StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
