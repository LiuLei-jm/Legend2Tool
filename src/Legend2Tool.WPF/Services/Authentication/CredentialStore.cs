using Legend2Tool.WPF.Models.Authentication;
using Serilog;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Legend2Tool.WPF.Services.Authentication
{
    public sealed class CredentialStore : ICredentialStore
    {
        private static readonly byte[] AdditionalEntropy =
            Encoding.UTF8.GetBytes("Legend2Tool.WPF.Credentials.v1");
        private readonly ILogger _logger;
        private readonly string _credentialPath;
        private readonly object _syncRoot = new();

        public CredentialStore(ILogger logger)
            : this(
                logger,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Legend2Tool",
                    "credentials.dat"
                )
            )
        {
        }

        internal CredentialStore(ILogger logger, string credentialPath)
        {
            _logger = logger;
            _credentialPath = Path.GetFullPath(credentialPath);
        }

        public SavedCredentials? Load()
        {
            lock (_syncRoot)
            {
                if (!File.Exists(_credentialPath))
                {
                    return null;
                }

                byte[]? decryptedBytes = null;
                try
                {
                    byte[] encryptedBytes = File.ReadAllBytes(_credentialPath);
                    decryptedBytes = ProtectedData.Unprotect(
                        encryptedBytes,
                        AdditionalEntropy,
                        DataProtectionScope.CurrentUser
                    );
                    SavedCredentials? credentials =
                        JsonSerializer.Deserialize<SavedCredentials>(decryptedBytes);
                    return credentials is not null
                        && !string.IsNullOrWhiteSpace(credentials.Username)
                        && !string.IsNullOrEmpty(credentials.Password)
                            ? credentials
                            : null;
                }
                catch (Exception ex) when (
                    ex is IOException
                    or UnauthorizedAccessException
                    or CryptographicException
                    or JsonException
                )
                {
                    _logger.Warning(ex, "读取已保存的登录凭据失败");
                    return null;
                }
                finally
                {
                    if (decryptedBytes is not null)
                    {
                        CryptographicOperations.ZeroMemory(decryptedBytes);
                    }
                }
            }
        }

        public void Save(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("用户名不能为空。", nameof(username));
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("密码不能为空。", nameof(password));
            }

            lock (_syncRoot)
            {
                string? directory = Path.GetDirectoryName(_credentialPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("登录凭据保存路径无效。");
                }

                Directory.CreateDirectory(directory);
                string tempPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(_credentialPath)}.{Guid.NewGuid():N}.tmp"
                );
                byte[] plainBytes = JsonSerializer.SerializeToUtf8Bytes(
                    new SavedCredentials(username, password)
                );
                try
                {
                    byte[] encryptedBytes = ProtectedData.Protect(
                        plainBytes,
                        AdditionalEntropy,
                        DataProtectionScope.CurrentUser
                    );
                    File.WriteAllBytes(tempPath, encryptedBytes);
                    File.Move(tempPath, _credentialPath, overwrite: true);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                if (File.Exists(_credentialPath))
                {
                    File.Delete(_credentialPath);
                }
            }
        }
    }
}
