using System.IO;
using System.Text.Json;
using ClipBoard.Core;
using Xunit;

namespace ClipBoard.Tests;

public sealed class ShortcutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ClipBoard.Shortcut.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsToWinVWithAltBFallback()
    {
        var settings = new ShortcutSettingsStore(Path.Combine(directory, "settings.json")).Load();
        Assert.Equal("Alt+B", settings.Shortcut.Label);
        Assert.True(settings.Shortcut.IsValid);
        Assert.True(settings.OverrideWinV);
    }

    [Fact]
    public void SavedWinVOptOutIsPreserved()
    {
        var store = new ShortcutSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new ShortcutSettings { OverrideWinV = false });
        Assert.False(store.Load().OverrideWinV);
    }

    [Theory]
    [InlineData(0, 0x42)]
    [InlineData(8, 0x56)]
    [InlineData(1, 0x7B)]
    [InlineData(1, 0)]
    [InlineData(2, 0x2E)]
    public void RejectsBareWindowsAndUnsupportedKeys(uint modifiers, uint key)
    {
        var settings = new ShortcutSettings { Shortcut = new ShortcutGesture(modifiers, key) };
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Theory]
    [InlineData(1, 0x42, "Alt+B")]
    [InlineData(6, 0x56, "Ctrl+Shift+V")]
    [InlineData(3, 0x70, "Ctrl+Alt+F1")]
    [InlineData(7, 0x39, "Ctrl+Alt+Shift+9")]
    public void FormatsValidShortcuts(uint modifiers, uint key, string label)
    {
        var gesture = new ShortcutGesture(modifiers, key);
        Assert.True(gesture.IsValid);
        Assert.Equal(label, gesture.Label);
    }

    [Fact]
    public void SettingsPersistAndCanBeReplaced()
    {
        var store = new ShortcutSettingsStore(Path.Combine(directory, "settings.json"));
        var settings = new ShortcutSettings { Shortcut = new ShortcutGesture(6, 0x56), OverrideWinV = true };
        store.Save(settings);
        Assert.Equal(settings, store.Load());
        store.Save(new ShortcutSettings());
        Assert.Equal(new ShortcutSettings(), store.Load());
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void InvalidSettingsDoNotOverwritePreviousChoice()
    {
        var store = new ShortcutSettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new ShortcutSettings());
        Assert.Throws<InvalidDataException>(() => store.Save(new ShortcutSettings { Shortcut = new(0, 0x42) }));
        Assert.Equal("Alt+B", store.Load().Shortcut.Label);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Shortcut\":null}")]
    [InlineData("{\"Shortcut\":{\"Modifiers\":8,\"VirtualKey\":86}}")]
    public void CorruptSettingsAreReportedAndPreserved(string content)
    {
        var store = new ShortcutSettingsStore(Path.Combine(directory, "settings.json"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.FilePath, content);
        var error = Record.Exception(() => store.Load());
        Assert.True(error is JsonException or InvalidDataException);
        Assert.Equal(content, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void WinVActivatesOnceAndConsumesItsReleaseEvenAfterWindowsKeyReleased()
    {
        var filter = new WinVKeyFilter();
        Assert.True(filter.Process(0x56, true, true, false, out var activate));
        Assert.True(activate);
        Assert.True(filter.Process(0x56, true, true, false, out activate));
        Assert.False(activate);
        Assert.True(filter.Process(0x56, false, false, false, out activate));
        Assert.False(activate);
        Assert.False(filter.Process(0x56, true, false, false, out _));
        Assert.True(filter.Process(0x56, true, true, false, out activate));
        Assert.True(activate);
    }

    [Theory]
    [InlineData(0x56, false, false)]
    [InlineData(0x56, true, true)]
    [InlineData(0x43, true, false)]
    [InlineData(0x5B, true, false)]
    public void OrdinaryTypingModifiedWinVAndOtherWindowsShortcutsPassThrough(uint key, bool windowsDown, bool otherModifier)
    {
        var filter = new WinVKeyFilter();
        Assert.False(filter.Process(key, true, windowsDown, otherModifier, out var activate));
        Assert.False(activate);
        Assert.False(filter.Process(key, false, windowsDown, otherModifier, out _));
    }

    [Fact]
    public void ResetReleasesSuppressedState()
    {
        var filter = new WinVKeyFilter();
        filter.Process(0x56, true, true, false, out _);
        filter.Reset();
        Assert.False(filter.Process(0x56, false, false, false, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}