using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClipBoard.Core;
using Xunit;

namespace ClipBoard.Tests;

public sealed class EncryptedHistoryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ClipBoard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingHistoryLoadsAsEmpty()
    {
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void HistoryRoundTripsWithoutWritingPlaintext()
    {
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        var entry = new ClipboardEntry(Guid.NewGuid(), "private test phrase \u4e16\u754c", DateTimeOffset.UtcNow, true);
        store.Save([entry]);
        Assert.Equal(entry, Assert.Single(store.Load()));
        Assert.DoesNotContain("private test phrase", Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void ReplacementAndClearPersistCorrectly()
    {
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        store.Save([new(Guid.NewGuid(), "first", DateTimeOffset.UtcNow)]);
        store.Save([new(Guid.NewGuid(), "second", DateTimeOffset.UtcNow)]);
        Assert.Equal("second", Assert.Single(store.Load()).Text);
        store.Save([]);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void CorruptHistoryFailsWithoutOverwritingIt()
    {
        var store = new EncryptedHistoryStore(Path.Combine(directory, "history.dat"));
        Directory.CreateDirectory(directory);
        byte[] corrupt = [1, 2, 3, 4];
        File.WriteAllBytes(store.FilePath, corrupt);
        Assert.Throws<CryptographicException>(() => store.Load());
        Assert.Equal(corrupt, File.ReadAllBytes(store.FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}