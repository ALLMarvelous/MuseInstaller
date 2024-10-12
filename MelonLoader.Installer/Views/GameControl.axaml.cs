using Avalonia.Controls;
using Avalonia.Interactivity;
using MelonLoader.Installer.ViewModels;

namespace MelonLoader.Installer.Views;

public partial class GameControl : UserControl
{
    public GameModel? Model => (GameModel?)DataContext;

    public GameControl()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (Model == null)
        {
            IconsPanel.IsVisible = false;
            return;
        }

        var mlInstalled = Model.MLVersion != null;

        IconsPanel.IsVisible = mlInstalled || Model.GameSource != GameSource.Manual;
        MLIcon.IsVisible = mlInstalled;
        SteamIcon.IsVisible = Model.GameSource == GameSource.Steam;
        EgsIcon.IsVisible = Model.GameSource == GameSource.EGS;
    }

    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        if (Model == null)
            return;

        // For some reason DataContext becomes null if it updates, so we have to keep our own ref
        var model = Model;

        if (!model.ValidateGame())
            return;

        MainWindow.Instance.ShowDetailsView(model);
    }
}