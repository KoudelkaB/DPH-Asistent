using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dph.Core.Isds;

public interface IIsdsCredentialStore
{
    bool HasCredentials { get; }

    IsdsCredentials? Load();

    void Save(IsdsCredentials credentials);

    void Clear();
}

/// <summary>
/// Přihlašovací údaje do datové schránky uložené v profilu uživatele.
/// Na Windows je chrání DPAPI (klíč odvozený od účtu uživatele, jiný účet soubor nerozšifruje).
/// Jinde se šifrují AES-GCM klíčem v samostatném souboru s právy 0600 – ochrana tam stojí na
/// právech k souborům v domovském adresáři, což je stejná úroveň jako u lokální databáze aplikace.
/// </summary>
public sealed class IsdsCredentialStore(string? directory = null) : IIsdsCredentialStore
{
    private readonly string _directory = directory ?? Persistence.ApplicationPaths.DataDirectory;

    // Doplňková entropie DPAPI: zašifrovaný soubor nejde rozšifrovat jiným programem téhož
    // uživatele, který by jen zavolal ProtectedData.Unprotect nad zkopírovaným souborem.
    private static readonly byte[] Entropy = "DphAsistent.Isds.v1"u8.ToArray();

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private string CredentialsPath => Path.Combine(_directory, "isds-credentials.dat");

    private string KeyPath => Path.Combine(_directory, "isds-credentials.key");

    public bool HasCredentials => File.Exists(CredentialsPath);

    public IsdsCredentials? Load()
    {
        if (!File.Exists(CredentialsPath))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(CredentialsPath)));
            var stored = JsonSerializer.Deserialize<StoredCredentials>(json);
            return string.IsNullOrWhiteSpace(stored?.Login) || string.IsNullOrEmpty(stored.Password)
                ? null
                : new IsdsCredentials(stored.Login, stored.Password);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or FormatException)
        {
            // Poškozený nebo cizí soubor (jiný uživatel/profil) – chováme se, jako by údaje nebyly
            // uložené, a necháme uživatele zadat je znovu.
            return null;
        }
    }

    public void Save(IsdsCredentials credentials)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(new StoredCredentials(credentials.Login, credentials.Password));
        WriteOwnerOnly(CredentialsPath, Protect(Encoding.UTF8.GetBytes(json)));
    }

    public void Clear()
    {
        foreach (var path in new[] { CredentialsPath, KeyPath })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Soubor drží něco jiného – heslo se stejně přepíše při dalším uložení.
            }
        }
    }

    private byte[] Protect(byte[] plaintext)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        }

        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Entropy);
        }

        return [.. nonce, .. tag, .. ciphertext];
    }

    private byte[] Unprotect(byte[] payload)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
        }

        var nonceLength = AesGcm.NonceByteSizes.MaxSize;
        var tagLength = AesGcm.TagByteSizes.MaxSize;
        if (payload.Length < nonceLength + tagLength)
        {
            throw new CryptographicException("Uložené přihlašovací údaje jsou poškozené.");
        }

        var key = File.Exists(KeyPath)
            ? File.ReadAllBytes(KeyPath)
            : throw new CryptographicException("Chybí klíč k uloženým přihlašovacím údajům.");

        var plaintext = new byte[payload.Length - nonceLength - tagLength];
        using var aes = new AesGcm(key, tagLength);
        aes.Decrypt(
            payload.AsSpan(0, nonceLength),
            payload.AsSpan(nonceLength + tagLength),
            payload.AsSpan(nonceLength, tagLength),
            plaintext,
            Entropy);
        return plaintext;
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            var existing = File.ReadAllBytes(KeyPath);
            if (existing.Length == 32)
            {
                return existing;
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        WriteOwnerOnly(KeyPath, key);
        return key;
    }

    // Práva se nastavují před zápisem obsahu, aby soubor ani na okamžik neexistoval čitelný pro
    // ostatní; File.WriteAllBytes existující soubor jen zkrátí a práva nemění.
    private static void WriteOwnerOnly(string path, byte[] content)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (!File.Exists(path))
            {
                using (File.Create(path))
                {
                }
            }

            File.SetUnixFileMode(path, OwnerOnly);
        }

        File.WriteAllBytes(path, content);
    }

    private sealed record StoredCredentials(string Login, string Password);
}
