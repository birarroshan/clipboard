using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClipBoard.Core;

[SupportedOSPlatform("windows")]
public sealed class EncryptedHistoryStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);

    public IReadOnlyList<ClipboardEntry> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<ClipboardEntry[]>(plaintext)
                ?? throw new InvalidDataException("The history file is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Save(IReadOnlyList<ClipboardEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entries);
        byte[] ciphertext;
        try
        {
            ciphertext = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var temporaryPath = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(ciphertext);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
                File.Replace(temporaryPath, FilePath, null);
            else
                File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}