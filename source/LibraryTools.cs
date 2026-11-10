using System;
using System.Collections.Generic;
using System.Linq;

namespace Mirage;

public static class LibraryTools
{
    public const int MaxSelection = 100;
    private static string NameKey(Game game)
    {
        string name = GameSearch.Normalize(game.Name);
        string key = new(name.SkipWhile(c => !char.IsLetterOrDigit(c)).ToArray());
        return key.Length > 0 ? key : name;
    }
    public static IEnumerable<Game> Sort(IEnumerable<Game> games, string order) => order switch {
        "default" => games,
        "nameAsc" => games.OrderBy(NameKey, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal),
        "nameDesc" => games.OrderByDescending(NameKey, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal),
        _ => throw new ArgumentException("Ordenação da biblioteca inválida.")
    };
    // Validate every entry before changing the queue, keeping bulk actions atomic.
    public static List<ProcessPlaylistItem> PrepareAddition(IEnumerable<Game> catalog, IReadOnlyList<string> ids, int existingCount, int minutes, bool showWindow, string? selectedId = null, int selectedVariant = 0)
    {
        if (ids.Count is < 1 or > MaxSelection) throw new ArgumentException("Selecione de 1 a 100 jogos por vez.");
        if (ids.Any(id => id is null || id.Length is < 10 or > 22 || !id.All(char.IsAsciiDigit)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw new ArgumentException("A seleção contém identificadores inválidos ou repetidos.");
        if (minutes is < 1 or > 1440) throw new ArgumentException("Escolha um tempo inteiro de 1 a 1440 minutos.");
        if (existingCount + ids.Count > PlaylistWorkspace.MaxEntries) throw new InvalidOperationException("A seleção ultrapassa o limite de 500 jogos da sequência.");
        var wanted = ids.ToHashSet(StringComparer.Ordinal);
        var games = catalog.Where(game => wanted.Contains(game.Id)).DistinctBy(game => game.Id).ToDictionary(game => game.Id);
        var result = new List<ProcessPlaylistItem>();
        foreach (string id in ids) {
            if (!games.TryGetValue(id, out var game)) throw new ArgumentException("Um dos jogos selecionados não está mais no catálogo. Atualize a busca e tente novamente.");
            var choices = ProcessSession.Options(game);
            if (choices.Count == 0) throw new ArgumentException($"{game.Name} não possui um processo compatível. Remova-o da seleção.");
            int variant = id == selectedId ? Math.Clamp(selectedVariant, 0, choices.Count - 1) : 0;
            result.Add(new ProcessPlaylistItem { GameId = game.Id, GameName = game.Name, Executable = choices[variant].Name, DurationMinutes = minutes, ShowWindow = showWindow });
        }
        return result;
    }
}
