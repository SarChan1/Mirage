using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mirage;

public sealed class SavedPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<ProcessPlaylistItem> Items { get; set; } = [];
    [JsonIgnore] public bool IsValid => Guid.TryParseExact(Id, "N", out _) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 60 && Items is { Count: > 0 and <= 500 } && Items.All(item => item is not null && item.IsValid);
}

// Saved templates are snapshots. Undo affects only the editable current queue.
public sealed class PlaylistWorkspace
{
    public const int MaxEntries = 500;
    private readonly Preferences preferences;
    private readonly List<(string Label, List<ProcessPlaylistItem> Items)> history = [];
    public PlaylistWorkspace(Preferences preferences) => this.preferences = preferences;
    public bool CanUndo => history.Count > 0;
    public string? UndoLabel => history.LastOrDefault().Label;
    public void Remember(string label)
    {
        history.Add((label, preferences.ProcessPlaylist.Select(item => item.Copy()).ToList()));
        if (history.Count > 20) history.RemoveAt(0);
    }
    public void ClearHistory() => history.Clear();
    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("Nenhuma alteração para desfazer.");
        preferences.ProcessPlaylist = history[^1].Items.Select(item => item.Copy()).ToList();
        history.RemoveAt(history.Count - 1);
    }
    private string ValidateName(string value, string? except = null)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 60 || name.Any(char.IsControl)) throw new ArgumentException("Use um nome de 1 a 60 caracteres.");
        if (preferences.SavedPlaylists.Any(item => item.Id != except && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Já existe uma sequência com esse nome. Escolha outro nome.");
        return name;
    }
    public SavedPlaylist Find(string id) => preferences.SavedPlaylists.FirstOrDefault(item => item.Id == id) ?? throw new ArgumentException("Esta sequência salva não está mais disponível.");
    public SavedPlaylist Save(string name)
    {
        name = ValidateName(name);
        if (preferences.SavedPlaylists.Count >= 40) throw new InvalidOperationException("Você pode guardar até 40 sequências. Remova uma para salvar outra.");
        if (preferences.ProcessPlaylist.Count is < 1 or > MaxEntries || preferences.ProcessPlaylist.Any(item => !item.IsValid)) throw new InvalidOperationException("Adicione jogos válidos antes de salvar a sequência.");
        var saved = new SavedPlaylist { Name = name, Items = preferences.ProcessPlaylist.Select(item => item.Copy()).ToList() };
        preferences.SavedPlaylists.Insert(0, saved);
        return saved;
    }
    public void Rename(string id, string name) => Find(id).Name = ValidateName(name, id);
    public void Delete(string id) => preferences.SavedPlaylists.Remove(Find(id));
    public void Use(string id, bool append)
    {
        var saved = Find(id);
        if (!saved.IsValid) throw new InvalidOperationException("A sequência salva contém informações inválidas.");
        if (saved.Items.Count + (append ? preferences.ProcessPlaylist.Count : 0) > MaxEntries) throw new InvalidOperationException("Uma sequência pode conter até 500 jogos.");
        var items = saved.Items.Select(item => { var copy = item.Copy(); copy.EntryId = Guid.NewGuid().ToString("N"); return copy; }).ToList();
        Remember(append ? "Acrescentar sequência salva" : "Carregar sequência salva");
        preferences.ProcessPlaylist = append ? preferences.ProcessPlaylist.Concat(items).ToList() : items;
    }
    public void SetAllDurations(int minutes)
    {
        if (minutes is < 1 or > 1440) throw new ArgumentException("Escolha um tempo inteiro de 1 a 1440 minutos.");
        if (preferences.ProcessPlaylist.Count == 0) throw new InvalidOperationException("Adicione jogos antes de ajustar os tempos.");
        if (preferences.ProcessPlaylist.All(item => item.DurationMinutes == minutes)) return;
        Remember("Ajustar duração de todos");
        foreach (var item in preferences.ProcessPlaylist) item.DurationMinutes = minutes;
    }
}
