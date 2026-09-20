using Legend2Tool.WPF.Models.Authentication;
using Legend2Tool.WPF.Services;
using Serilog;
using System.Text;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class CredentialStoreTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public void SaveAndLoad_ValidCredentials_RoundTripsEncryptedValues()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "credentials.dat");
        try
        {
            var store = new CredentialStore(Logger, path);

            store.Save("test-user", "test-password");
            SavedCredentials? credentials = store.Load();

            Assert.NotNull(credentials);
            Assert.Equal("test-user", credentials.Username);
            Assert.Equal("test-password", credentials.Password);
            string persistedText = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain("test-user", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("test-password", persistedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Clear_SavedCredentials_RemovesCredentialFile()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "credentials.dat");
        try
        {
            var store = new CredentialStore(Logger, path);
            store.Save("test-user", "test-password");

            store.Clear();

            Assert.False(File.Exists(path));
            Assert.Null(store.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptedCredentialFile_ReturnsNull()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "credentials.dat");
        try
        {
            File.WriteAllText(path, "not-encrypted-data");
            var store = new CredentialStore(Logger, path);

            SavedCredentials? credentials = store.Load();

            Assert.Null(credentials);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Legend2Tool.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }
}
