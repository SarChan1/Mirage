using System;
using System.Collections.Generic;
using System.Linq;

namespace Mirage;
public sealed class ProcessPlaylist
{
    private readonly List<ProcessPlaylistItem> items;
    public int Index { get; private set; } = -1;
    public int Count => items.Count;
    public DateTimeOffset EndsAt { get; private set; }
    public ProcessPlaylistItem? Current => Index >= 0 && Index < items.Count ? items[Index] : null;
    public bool Started => Index >= 0;
    public bool Completed => Started && Index >= items.Count;

    public ProcessPlaylist(IEnumerable<ProcessPlaylistItem> source)
    {
        items = source.Select(i => i.Copy()).ToList();
        if (items.Count == 0) throw new ArgumentException("A lista está vazia.", nameof(source));
        if (items.Any(i => !i.IsValid)) throw new ArgumentException("A lista contém um item inválido.", nameof(source));
    }
    public ProcessPlaylistItem Start(DateTimeOffset now)
    {
        if (Started) throw new InvalidOperationException("A lista já foi iniciada.");
        Index = 0; SetDeadline(now); return Current!;
    }
    public bool IsDue(DateTimeOffset now) => Current is not null && now >= EndsAt;
    public ProcessPlaylistItem? Advance(DateTimeOffset now)
    {
        if (!Started) throw new InvalidOperationException("A lista ainda não foi iniciada.");
        Index++;
        if (Index >= items.Count) { EndsAt = now; return null; }
        SetDeadline(now); return Current;
    }
    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (Current is null) return TimeSpan.Zero;
        var remaining = EndsAt - now; return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
    private void SetDeadline(DateTimeOffset now) => EndsAt = now.AddMinutes(Current!.DurationMinutes);
}
