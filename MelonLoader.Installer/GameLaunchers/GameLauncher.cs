namespace MelonLoader.Installer.GameLaunchers;

public abstract class GameLauncher(string iconPath)
{
    public static GameLauncher[] Launchers { get; private set; } =
    [
        new SteamLauncher()
    ];

    public string IconPath => iconPath;

    public abstract void AddGames();
}
