using Dph.Core.Isds;

namespace Dph.Core.Tests;

public sealed class IsdsCredentialStoreTests
{
    [Fact]
    public void Round_trips_credentials()
    {
        var directory = NewDirectory();
        var store = new IsdsCredentialStore(directory);
        Assert.False(store.HasCredentials);
        Assert.Null(store.Load());

        store.Save(new IsdsCredentials("uzivatel", "heslo s háčky ěščř"));

        Assert.True(store.HasCredentials);
        var loaded = new IsdsCredentialStore(directory).Load();
        Assert.Equal("uzivatel", loaded!.Login);
        Assert.Equal("heslo s háčky ěščř", loaded.Password);
    }

    [Fact]
    public void Password_is_not_stored_in_clear_text()
    {
        var directory = NewDirectory();
        new IsdsCredentialStore(directory).Save(new IsdsCredentials("uzivatel", "TajneHeslo123"));

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            Assert.DoesNotContain("TajneHeslo123", File.ReadAllText(file));
        }
    }

    [Fact]
    public void Overwrites_previous_credentials()
    {
        var directory = NewDirectory();
        var store = new IsdsCredentialStore(directory);
        store.Save(new IsdsCredentials("stary", "stare"));
        store.Save(new IsdsCredentials("novy", "nove"));

        Assert.Equal(new IsdsCredentials("novy", "nove"), store.Load());
    }

    [Fact]
    public void Clear_removes_the_stored_credentials()
    {
        var directory = NewDirectory();
        var store = new IsdsCredentialStore(directory);
        store.Save(new IsdsCredentials("uzivatel", "heslo"));

        store.Clear();

        Assert.False(store.HasCredentials);
        Assert.Null(store.Load());
        // Po smazání musí jít údaje zadat znovu (klíč se vytvoří nový).
        store.Save(new IsdsCredentials("uzivatel", "heslo2"));
        Assert.Equal("heslo2", store.Load()!.Password);
    }

    [Fact]
    public void Damaged_file_reads_as_no_credentials_instead_of_throwing()
    {
        var directory = NewDirectory();
        var store = new IsdsCredentialStore(directory);
        store.Save(new IsdsCredentials("uzivatel", "heslo"));
        File.WriteAllBytes(Path.Combine(directory, "isds-credentials.dat"), [0, 1, 2, 3, 4]);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Missing_key_reads_as_no_credentials()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Na Windows chrání údaje DPAPI, samostatný soubor s klíčem nevzniká.
        }

        var directory = NewDirectory();
        var store = new IsdsCredentialStore(directory);
        store.Save(new IsdsCredentials("uzivatel", "heslo"));
        File.Delete(Path.Combine(directory, "isds-credentials.key"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void Files_are_readable_only_by_the_owner()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Práva ve stylu POSIX Windows nemá; tam údaje chrání DPAPI.
        }

        var directory = NewDirectory();
        new IsdsCredentialStore(directory).Save(new IsdsCredentials("uzivatel", "heslo"));

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dph-isds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
