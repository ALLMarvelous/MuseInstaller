namespace MelonLoader.Installer;

internal static class Config
{
    public static Uri Discord { get; private set; } = new("https://discord.gg/PmJgAnnNXy");
    public static Uri Website { get; private set; } = new("https://mdmc.moe");
    public static string MdmcInstallerApi { get; private set; } = "https://api.mdmc.moe/v2/installer";
    public static string InstallerLatestReleaseApi { get; private set; } = "https://api.github.com/repos/LavaGang/MelonLoader.Installer/releases/latest";
    public static string CacheDir { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MelonLoader Installer");
    public static string LocalZipCache { get; private set; } = Path.Combine(CacheDir, "Local Build");
    public static string GameListPath { get; private set; } = Path.Combine(CacheDir, "games.txt");

    public static string[] LoadGameList()
    {
        if (!File.Exists(GameListPath))
            return [];

        return File.ReadAllLines(GameListPath);
    }

    public static void SaveGameList(IEnumerable<string> gamePaths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GameListPath)!);
        File.WriteAllLines(GameListPath, gamePaths);
    }
}
