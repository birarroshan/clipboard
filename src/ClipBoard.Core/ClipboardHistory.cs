using System.Text.Json.Serialization;

namespace ClipBoard.Core;

public sealed record ClipboardEntry(Guid Id, string Text, DateTimeOffset CapturedAt, bool IsPinned = false)
{
    [JsonIgnore]
    public string Preview => Text.Length > 240 ? Text[..240] + "..." : Text;
    [JsonIgnore]
    public string Kind => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out var address)
        && address.Scheme is "https" or "http" ? "LINK" : "TEXT";
    [JsonIgnore]
    public string Timestamp => CapturedAt.ToLocalTime().ToString("MMM d, h:mm tt");
    [JsonIgnore]
    public string Details => $"{Timestamp}   |   {Text.Length:N0} characters";
}

public sealed class ClipboardHistory(TimeProvider? clock = null)
{
    public const int MaxEntries = 500;
    public const int MaxPins = 100;
    public const int MaxTextLength = 131_072;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly List<ClipboardEntry> entries = [];

    public int Count => entries.Count;
    public int PinnedCount => entries.Count(entry => entry.IsPinned);

    public bool Add(string? text)
    {
        Prune();
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            return false;

        var existing = entries.Find(entry => entry.Text == text);
        if (existing is not null)
            entries.Remove(existing);

        entries.Add(new ClipboardEntry(existing?.Id ?? Guid.NewGuid(), text,
            clock.GetUtcNow(), existing?.IsPinned ?? false));
        Prune();
        return true;
    }

    public IReadOnlyList<ClipboardEntry> Query(string? search = null, bool pinnedOnly = false)
    {
        var term = search?.Trim() ?? "";
        return entries
            .Where(entry => (!pinnedOnly || entry.IsPinned)
                && entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.CapturedAt)
            .ToArray();
    }

    public bool TogglePin(Guid id)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || (!entries[index].IsPinned && PinnedCount >= MaxPins))
            return false;

        entries[index] = entries[index] with { IsPinned = !entries[index].IsPinned };
        Prune();
        return true;
    }

    public bool Remove(Guid id) => entries.RemoveAll(entry => entry.Id == id) > 0;

    public void Clear() => entries.Clear();

    public void Restore(IEnumerable<ClipboardEntry> saved)
    {
        entries.Clear();
        entries.AddRange(saved
            .Where(entry => entry is not null && entry.Id != Guid.Empty
                && !string.IsNullOrWhiteSpace(entry.Text) && entry.Text.Length <= MaxTextLength)
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.CapturedAt)
            .DistinctBy(entry => entry.Text, StringComparer.Ordinal)
            .DistinctBy(entry => entry.Id)
            .Take(MaxEntries));

        var pinCount = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].IsPinned && ++pinCount > MaxPins)
                entries[index] = entries[index] with { IsPinned = false };
        }
        Prune();
    }

    public bool Prune()
    {
        var cutoff = clock.GetUtcNow() - Retention;
        var removed = entries.RemoveAll(entry => !entry.IsPinned && entry.CapturedAt < cutoff);
        var overflow = entries.OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.CapturedAt).Skip(MaxEntries).Select(entry => entry.Id).ToHashSet();
        removed += entries.RemoveAll(entry => overflow.Contains(entry.Id));
        return removed > 0;
    }
}