using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MelonLoader.Installer.ViewModels;
using System.ComponentModel;

namespace MelonLoader.Installer.Views;

public partial class DetailsView : UserControl
{
    public DetailsViewModel? Model => (DetailsViewModel?)DataContext;

    public DetailsView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        if (Model == null)
            return;

        Model.Game.PropertyChanged -= PropertyChangedHandler;
    }

    private void PropertyChangedHandler(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == "MLVersion")
        {
            UpdateVersionInfo();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (Model == null)
            return;
        
#if LINUX
        if (Model.Game.IsLinux)
        {
            LdLibPathVar.Text = $"LD_LIBRARY_PATH=\"{Model.Game.Dir}:$LD_LIBRARY_PATH\"";
            SteamLaunchOptions.Text = $"{LdLibPathVar.Text} {LdPreloadVar.Text} %command%";
        }
        
        ShowLinuxInstructions.IsVisible = Model.Game.MLInstalled;
#endif

        Model.Game.PropertyChanged += PropertyChangedHandler;

        UpdateVersionList();

        if (!MLManager.Init())
        {
            Model.Offline = true;
            DialogBox.ShowError("Failed to fetch MelonLoader releases. Ensure you're online.");
        }
    }

    private void NightlyToggleHandler(object sender, RoutedEventArgs args)
    {
        UpdateVersionList();
    }

    public void UpdateVersionList()
    {
        if (Model == null)
            return;

        var en = MLManager.Versions.Where(x => (Model.Game.IsLinux ? x.DownloadUrlLinux : (Model.Game.Is32Bit ? x.DownloadUrlWinX86 : x.DownloadUrlWin)) != null);
        if (NightlyCheck.IsChecked != true)
            en = en.Where(x => !x.Version.IsPrerelease || x.IsLocalPath);

        VersionCombobox.ItemsSource = en;
        VersionCombobox.SelectedIndex = 0;
    }

    private void BackClickHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;
        
        if (Model.LinuxInstructions)
        {
            Model.LinuxInstructions = false;
            return;
        }
        
        if (Model.Installing)
            return;

        MainWindow.Instance.ShowMainView();
    }

    private void VersionSelectHandler(object? sender, SelectionChangedEventArgs args)
    {
        UpdateVersionInfo();
    }

    public void UpdateVersionInfo()
    {
        if (Model == null || VersionCombobox.SelectedItem == null)
            return;

        MelonIcon.Opacity = Model.Game.MLInstalled ? 1 : 0.3;

        if (Model.Game.MLVersion == null)
        {
            InstallButton.Content = "Install";
            return;
        }

        var comp = ((MLVersion)VersionCombobox.SelectedItem).Version.CompareSortOrderTo(Model.Game.MLVersion);

        InstallButton.Content = comp switch
        {
            < 0 => "Downgrade",
            0 => "Reinstall",
            > 0 => "Upgrade"
        };
    }

    private void InstallHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.ValidateGame())
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        Model.Installing = true;
        ShowLinuxInstructions.IsVisible = false;

        _ = MLManager.InstallAsync(Path.GetDirectoryName(Model.Game.Path)!, Model.Game.MLInstalled && !KeepFilesCheck.IsChecked!.Value,
            (MLVersion)VersionCombobox.SelectedItem!, Model.Game.IsLinux, Model.Game.Is32Bit,
            (progress, newStatus) => Dispatcher.UIThread.Post(() => OnInstallProgress(progress, newStatus)),
            (errorMessage) => Dispatcher.UIThread.Post(() => OnInstallFinished(errorMessage)));
    }

    private void OnInstallProgress(double progress, string? newStatus)
    {
        if (newStatus != null)
            InstallStatus.Text = newStatus;

        Progress.Value = progress * 100;
        MelonIcon.Opacity = progress * 0.7 + 0.3;
    }

    private void OnInstallFinished(string? errorMessage)
    {
        if (Model == null)
            return;

        var wasReinstall = Model.Game.MLInstalled;
        Model.Game.ValidateGame();
        
#if LINUX
        ShowLinuxInstructions.IsVisible = Model.Game.MLInstalled;
#endif

        Model.Installing = false;

        if (errorMessage != null)
        {
            DialogBox.ShowError(errorMessage);
            return;
        }

        DialogBox.ShowNotice("Success!", $"Successfully {(Model.Game.MLInstalled ? (wasReinstall ? "reinstalled" : "installed") : "uninstalled")} MelonLoader!");
    }

    private void OpenDirHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;

        TopLevel.GetTopLevel(this)!.Launcher.LaunchDirectoryInfoAsync(new(Model.Game.Dir));
    }

    private void UninstallHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null || !Model.Game.ValidateGame())
        {
            MainWindow.Instance.ShowMainView();
            return;
        }

        if (!Model.Game.MLInstalled)
            return;

        var error = MLManager.Uninstall(Model.Game.Dir, !KeepFilesCheck.IsChecked!.Value);
        
        OnInstallFinished(error);
    }

    private async void SelectZipHandler(object sender, TappedEventArgs args)
    {
        if (Model == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Select a zipped MelonLoader version...",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new("ZIP Archive")
                {
                    Patterns = [ "*.zip" ]
                }
            ]
        });

        if (files.Count is 0 or > 1)
            return;

        var path = files[0].Path.LocalPath;

        Model.Installing = true;
        ShowLinuxInstructions.IsVisible = false;

        _ = Task.Run(() => MLManager.SetLocalZip(path,
            (progress, newStatus) => Dispatcher.UIThread.Post(() => OnInstallProgress(progress, newStatus)),
            (errorMessage) => Dispatcher.UIThread.Post(() =>
            {
                if (errorMessage == null)
                {
                    var ver = MLManager.Versions[0];
                    if ((Model.Game.IsLinux ? ver.DownloadUrlLinux : (Model.Game.Is32Bit ? ver.DownloadUrlWinX86 : ver.DownloadUrlWin)) == null)
                    {
                        DialogBox.ShowError($"The selected version does not support the architechture of the current game: {(Model.Game.IsLinux ? "linux" : "win")}-{(Model.Game.Is32Bit ? "x86" : "x64")}");
                    }
                }

                OnInstallFinished(errorMessage);
                UpdateVersionList();
            })));
    }

    private void ShowLinuxInstructionsHandler(object sender, TappedEventArgs args)
    {
        if (Model == null)
            return;
        
        Model.LinuxInstructions = true;
    }
}