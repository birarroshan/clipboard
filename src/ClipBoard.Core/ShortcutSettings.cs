using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipBoard.Core;

public sealed record ShortcutGesture(uint Modifiers, uint VirtualKey)
{
    public static ShortcutGesture Default { get; } = new(1, 0x42);

    [JsonIgnore]
    public bool IsValid => Modifiers is > 0 and <= 7
        && (VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A);

    [JsonIgnore]
    public string Label => ((Modifiers & 2) != 0 ? "Ctrl+" : "")
        + ((Modifiers & 1) != 0 ? "Alt+" : "")
        + ((Modifiers & 4) != 0 ? "Shift+" : "")
        + (VirtualKey >= 0x70 ? $"F{VirtualKey - 0x6F}" : ((char)VirtualKey).ToString());
}

public sealed record ShortcutSettings
{
    public ShortcutGesture Shortcut { get; init; } = ShortcutGesture.Default;
    public bool OverrideWinV { get; init; } = true;

    public void Validate()
    {
        if (Shortcut is null || !Shortcut.IsValid)
            throw new InvalidDataException("Choose Ctrl, Alt, or Shift with a letter, digit, or F1-F11.");
    }
}

public sealed class ShortcutSettingsStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);

    public ShortcutSettings Load()
    {
        if (!File.Exists(FilePath))
            return new ShortcutSettings();
        var settings = JsonSerializer.Deserialize<ShortcutSettings>(File.ReadAllBytes(FilePath))
            ?? throw new InvalidDataException("Shortcut settings are empty.");
        settings.Validate();
        return settings;
    }

    public void Save(ShortcutSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(settings));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}