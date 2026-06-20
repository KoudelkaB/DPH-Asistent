namespace Dph.Core.Persistence;

public static class ApplicationPaths
{
    public static string DataDirectory
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(baseDir, "DphAssistant");
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "dph.sqlite");
    public static string ExportDirectory => Path.Combine(DataDirectory, "exports");
}
