using Semver;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

#if WINDOWS
using System.Diagnostics;
#endif

namespace MelonLoader.Installer;

internal static class MLManager
{
    private static bool inited;
    internal static readonly string[] proxyNames = 
    [
        "version.dll",
        "winmm.dll",
        "winhttp.dll",
        "MelonBootstrap.so",
        "libversion.so",
        "libwinmm.so",
        "libwinhttp.so"
    ];

    private static MLVersion? localBuild;

    private static JsonArray suggestedMods = [];
    private static JsonArray suggestedCharts = [];

    public static List<MLVersion> Versions { get; } = [];

    static MLManager()
    {
        Program.Exiting += HandleExit;
    }

    private static void HandleExit()
    {
        if (Directory.Exists(Config.LocalZipCache))
        {
            try
            {
                Directory.Delete(Config.LocalZipCache, true);
            }
            catch { }
        }
    }

    public static async Task<bool> Init()
    {
        if (inited)
            return true;

        inited = await RefreshVersions();
        return inited;
    }

    private static Task<bool> RefreshVersions()
    {
        Versions.Clear();

        if (localBuild != null)
            Versions.Add(localBuild);

        return GetVersionsAsync(Versions);
    }

    private static async Task<bool> GetVersionsAsync(List<MLVersion> versions)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await InstallerUtils.Http.GetAsync(Config.MdmcInstallerApi).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }

        if (!resp.IsSuccessStatusCode)
            return false;

        var relStr = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var versionString = JsonNode.Parse(relStr)!["melonloader"]!.ToString();

        suggestedCharts = JsonNode.Parse(relStr)!["charts"]!.AsArray();
        suggestedMods = JsonNode.Parse(relStr)!["mods"]!.AsArray();

        if (!SemVersion.TryParse(versionString + "-ci.-1", SemVersionStyles.Any, out var semVersion))
            return false;
            
        var version = new MLVersion
        {
            Version = semVersion,
            DownloadUrlWin = $"https://github.com/LavaGang/MelonLoader/releases/download/v{versionString}/MelonLoader.x64.zip",
            DownloadUrlWinX86 = null,
            DownloadUrlLinux = null
        };

        if (version.DownloadUrlWin == null && version.DownloadUrlWinX86 == null && version.DownloadUrlLinux == null)
            return false;

        versions.Add(version);
       

        return true;
    }

    public static string? Uninstall(string gameDir, bool removeUserFiles)
    {
        if (!Directory.Exists(gameDir))
        {
            return "The provided directory does not exist.";
        }

        foreach (var proxy in proxyNames)
        {
            var proxyPath = Path.Combine(gameDir, proxy);
            if (!File.Exists(proxyPath))
                continue;

#if WINDOWS
            var versionInf = FileVersionInfo.GetVersionInfo(proxyPath);
            if (versionInf.LegalCopyright != null && versionInf.LegalCopyright.Contains("Microsoft"))
                continue;
#endif

            try
            {
                File.Delete(proxyPath);
            }
            catch
            {
                return "Failed to uninstall MelonLoader. Ensure that the game is fully closed before trying again.";
            }
        }

        var mlDir = Path.Combine(gameDir, "MelonLoader");
        if (Directory.Exists(mlDir))
        {
            try
            {
                Directory.Delete(mlDir, true);
            }
            catch
            {
                return "Failed to uninstall MelonLoader. Ensure that the game is fully closed before trying again.";
            }
        }

        var dobbyPath = Path.Combine(gameDir, "dobby.dll");
        if (File.Exists(dobbyPath))
        {
            try
            {
                File.Delete(dobbyPath);
            }
            catch
            {
                return "Failed to fully uninstall MelonLoader: Failed to remove dobby.";
            }
        }

        var noticePath = Path.Combine(gameDir, "NOTICE.txt");
        if (File.Exists(noticePath))
        {
            try
            {
                File.Delete(noticePath);
            }
            catch
            {
                return "Failed to fully uninstall MelonLoader: Failed to remove 'NOTICE.txt'.";
            }
        }

        if (removeUserFiles)
        {
            var modsDir = Path.Combine(gameDir, "Mods");
            if (Directory.Exists(modsDir))
            {
                try
                {
                    Directory.Delete(modsDir, true);
                }
                catch
                {
                    return "Failed to fully uninstall MelonLoader: Failed to remove the Mods folder.";
                }
            }

            var pluginsDir = Path.Combine(gameDir, "Plugins");
            if (Directory.Exists(pluginsDir))
            {
                try
                {
                    Directory.Delete(pluginsDir, true);
                }
                catch
                {
                    return "Failed to fully uninstall MelonLoader: Failed to remove the Plugins folder.";
                }
            }

            var userDataDir = Path.Combine(gameDir, "UserData");
            if (Directory.Exists(userDataDir))
            {
                try
                {
                    Directory.Delete(userDataDir, true);
                }
                catch
                {
                    return "Failed to fully uninstall MelonLoader: Failed to remove the UserData folder.";
                }
            }

            var userLibsDir = Path.Combine(gameDir, "UserLibs");
            if (Directory.Exists(userLibsDir))
            {
                try
                {
                    Directory.Delete(userLibsDir, true);
                }
                catch
                {
                    return "Failed to fully uninstall MelonLoader: Failed to remove the UserLibs folder.";
                }
            }

            var customAlbumsDir = Path.Combine(gameDir, "Custom_Albums");
            if (Directory.Exists(customAlbumsDir))
            {
                try
                {
                    Directory.Delete(customAlbumsDir, true);
                }
                catch
                {
                    return "Failed to fully uninstall MelonLoader: Failed to remove the Custom_Albums folder.";
                }
            }
        }

        return null;
    }

    public static async Task InstallAsync(string gameDir, bool removeUserFiles, bool includeAll, MLVersion version, InstallProgressEventHandler? onProgress, InstallFinishedEventHandler? onFinished)
    {
        var downloadUrl = version.DownloadUrlWin!;

        onProgress?.Invoke(0, "Uninstalling previous versions");

        var unErr = Uninstall(gameDir, removeUserFiles);
        if (unErr != null)
        {
            onFinished?.Invoke(unErr);
            return;
        }

        var tasks = 2;
        var currentTask = 0;

        void SetProgress(double progress, string? newStatus = null)
        {
            onProgress?.Invoke(currentTask / (double)tasks + progress / tasks, newStatus);
        }

        SetProgress(0, "Downloading MelonLoader " + version);

        using var bufferStr = new MemoryStream();
        var result = await InstallerUtils.DownloadFileAsync(downloadUrl, bufferStr, SetProgress);
        if (result != null)
        {
            onFinished?.Invoke("Failed to download MelonLoader: " + result);
            return;
        }
        bufferStr.Seek(0, SeekOrigin.Begin);

        currentTask++;

        SetProgress(0, "Installing " + version);

        var extRes = InstallerUtils.Extract(bufferStr, gameDir, SetProgress);
        if (extRes != null)
        {
            onFinished?.Invoke(extRes);
            return;
        }

        Directory.CreateDirectory(Path.Combine(gameDir, "Mods"));
        Directory.CreateDirectory(Path.Combine(gameDir, "Plugins"));
        Directory.CreateDirectory(Path.Combine(gameDir, "UserData"));
        Directory.CreateDirectory(Path.Combine(gameDir, "UserLibs"));

        if (includeAll)
        {
            // Do mods
            foreach (var mod in suggestedMods)
            {
                if (mod == null || mod["id"] == null || mod["name"] == null || mod["version"] == null)
                    continue;

                var modId = mod["id"]!.ToString();
                var modName = mod["name"]!.ToString();
                var modVersion = mod["version"]!.ToString();
                var modUrl = $"https://api.mdmc.moe/v2/mods/{modId}/download";
                var modPath = Path.Combine(gameDir, "Mods", $"{modName}.dll");

                SetProgress(0, $"Downloading mod '{modName}' v{modVersion}");

                using var modBuffer = new MemoryStream();
                var modResult = await InstallerUtils.DownloadFileAsync(modUrl, modBuffer, SetProgress);
                if (modResult != null)
                {
                    onFinished?.Invoke("Failed to download mod '" + modName + "': " + modResult);
                    return;
                }
                modBuffer.Seek(0, SeekOrigin.Begin);
                using var modFile = File.Create(modPath);
                await modBuffer.CopyToAsync(modFile);
            }

            // Do charts
            Directory.CreateDirectory(Path.Combine(gameDir, "Custom_Albums"));
            foreach (var chart in suggestedCharts)
            {
                if (chart == null || chart["id"] == null || chart["name"] == null)
                    continue;

                var chartId = chart["id"]!.ToString();
                var chartName = chart["name"]!.ToString();
                var chartUrl = $"https://api.mdmc.moe/v2/charts/{chartId}/download";
                var chartPath = Path.Combine(gameDir, "Custom_Albums", $"{chartName}.mdm");

                SetProgress(0, $"Downloading chart '{chartName}'");

                using var chartBuffer = new MemoryStream();
                var chartResult = await InstallerUtils.DownloadFileAsync(chartUrl, chartBuffer, SetProgress);
                if (chartResult != null)
                {
                    onFinished?.Invoke("Failed to download chart '" + chartName + "': " + chartResult);
                    return;
                }
                chartBuffer.Seek(0, SeekOrigin.Begin);
                using var chartFile = File.Create(chartPath);
                await chartBuffer.CopyToAsync(chartFile);
            }

            // Do custom start screen
            var startScreenUrl = "http://mdmc.moe/cdn/startscreen.zip";

            SetProgress(0, "Downloading Muse Dash start screen");

            using var startScreenBuffer = new MemoryStream();
            var startScreenResult = await InstallerUtils.DownloadFileAsync(startScreenUrl, startScreenBuffer, SetProgress);
            if (startScreenResult != null)
            {
                onFinished?.Invoke("Failed to download Muse Dash start screen: " + startScreenResult);
                return;
            }
            startScreenBuffer.Seek(0, SeekOrigin.Begin);
            
            var extStartScreenRes = InstallerUtils.Extract(startScreenBuffer, Path.Combine(gameDir, "UserData"), SetProgress);
            if (extStartScreenRes != null)
            {
                onFinished?.Invoke(extStartScreenRes);
                return;
            }
        }

        onFinished?.Invoke(null);
    }
}