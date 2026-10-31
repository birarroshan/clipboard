using ClipBoard.Core;
using Xunit;

namespace ClipBoard.Tests;

public sealed class ClipboardHistoryTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("hello", 1)]
    [InlineData("WORLD", 2)]
    [InlineData("  hello  ", 1)]
    [InlineData("missing", 0)]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    public void SearchMatchesCaseInsensitiveSubstrings(string? term, int expected)
    {
        var history = new ClipboardHistory();
        history.Add("Hello world");
        history.Add("WORLD news");
        history.Add("Other text");
        Assert.Equal(expected, history.Query(term).Count);
    }

    [Fact]
    public void UnicodeAndMultilineTextArePreserved()
    {
        const string text = "  caf\u00e9 \u4e16\u754c\r\nsecond line  ";
        var history = new ClipboardHistory();
        history.Add(text);
        Assert.Equal(text, Assert.Single(history.Query("\u4e16\u754c")).Text);
    }

    [Fact]
    public void RecopyMovesExistingItemToTopWithoutLosingItsPinOrId()
    {
        var clock = new TestClock();
        var history = new ClipboardHistory(clock);
        history.Add("first");
        var original = Assert.Single(history.Query());
        history.TogglePin(original.Id);
        clock.Now += TimeSpan.FromMinutes(1);
        history.Add("second");
        clock.Now += TimeSpan.FromMinutes(1);
        history.Add("first");
        Assert.Equal(2, history.Count);
        Assert.Equal(original.Id, history.Query()[0].Id);
        Assert.True(history.Query()[0].IsPinned);
        Assert.Equal(clock.Now, history.Query()[0].CapturedAt);
    }

    [Fact]
    public void PinsSortFirstAndCanBeFilteredAndUnpinned()
    {
        var clock = new TestClock();
        var history = new ClipboardHistory(clock);
        history.Add("older");
        var older = history.Query()[0];
        history.TogglePin(older.Id);
        clock.Now += TimeSpan.FromMinutes(1);
        history.Add("newer");
        Assert.Equal(older.Id, history.Query()[0].Id);
        Assert.Single(history.Query(pinnedOnly: true));
        Assert.Empty(history.Query("newer", pinnedOnly: true));
        history.TogglePin(older.Id);
        Assert.Equal("newer", history.Query()[0].Text);
    }

    [Fact]
    public void ExpiryRemovesOldUnpinnedEntriesButKeepsPins()
    {
        var clock = new TestClock();
        var history = new ClipboardHistory(clock);
        history.Add("keep");
        history.TogglePin(history.Query()[0].Id);
        history.Add("expire");
        clock.Now += TimeSpan.FromDays(31);
        Assert.True(history.Prune());
        Assert.Equal("keep", Assert.Single(history.Query()).Text);
    }

    [Fact]
    public void CapacityEvictsOldestUnpinnedEntry()
    {
        var clock = new TestClock();
        var history = new ClipboardHistory(clock);
        history.Add("pinned");
        history.TogglePin(history.Query()[0].Id);
        for (var index = 0; index < ClipboardHistory.MaxEntries; index++)
        {
            clock.Now += TimeSpan.FromSeconds(1);
            history.Add($"entry {index}");
        }
        Assert.Equal(ClipboardHistory.MaxEntries, history.Count);
        Assert.Equal("pinned", history.Query()[0].Text);
        Assert.DoesNotContain(history.Query(), entry => entry.Text == "entry 0");
    }

    [Fact]
    public void PinLimitIsEnforced()
    {
        var history = new ClipboardHistory();
        for (var index = 0; index <= ClipboardHistory.MaxPins; index++)
            history.Add($"entry {index}");
        foreach (var entry in history.Query().Take(ClipboardHistory.MaxPins))
            Assert.True(history.TogglePin(entry.Id));
        Assert.False(history.TogglePin(history.Query().Last().Id));
        Assert.Equal(ClipboardHistory.MaxPins, history.PinnedCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public void EmptyTextIsIgnored(string? text)
    {
        var history = new ClipboardHistory();
        Assert.False(history.Add(text));
        Assert.Empty(history.Query());
    }

    [Fact]
    public void OversizedTextIsIgnored()
    {
        var history = new ClipboardHistory();
        Assert.False(history.Add(new string('a', ClipboardHistory.MaxTextLength + 1)));
    }

    [Fact]
    public void RestoreSanitizesDuplicatesAndExpiredEntries()
    {
        var clock = new TestClock();
        var history = new ClipboardHistory(clock);
        history.Restore([
            new(Guid.NewGuid(), "same", clock.Now),
            new(Guid.NewGuid(), "same", clock.Now - TimeSpan.FromMinutes(1)),
            new(Guid.NewGuid(), "old", clock.Now - TimeSpan.FromDays(31)),
            new(Guid.NewGuid(), "pinned", clock.Now - TimeSpan.FromDays(60), true),
            new(Guid.NewGuid(), " ", clock.Now),
            new(Guid.Empty, "invalid", clock.Now)
        ]);
        Assert.Equal(2, history.Count);
        Assert.Equal("pinned", history.Query()[0].Text);
    }

    [Fact]
    public void RemoveAndClearDeleteEntries()
    {
        var history = new ClipboardHistory();
        history.Add("one");
        history.Add("two");
        Assert.True(history.Remove(history.Query()[0].Id));
        Assert.Single(history.Query());
        Assert.False(history.Remove(Guid.NewGuid()));
        history.Clear();
        Assert.Empty(history.Query());
    }
}