using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tachyvex.App.Pages;
using Tachyvex.App.Services;
using Tachyvex.Core.Abstractions;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 820));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "tachyvex.ico")); } catch (Exception) { /* ikona opcjonalna */ }

        var dialogs = App.Get<DialogService>();
        ((FrameworkElement)Content).Loaded += (_, _) => dialogs.Root = Content.XamlRoot;
        AdminBar.IsOpen = !App.Get<IElevationService>().IsElevated;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        ContentFrame.Navigate(tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "gaming" => typeof(GamingPage),
            "optimizations" => typeof(OptimizationsPage),
            "network" => typeof(NetworkPage),
            "system" => typeof(SystemPage),
            "managers" => typeof(ManagersPage),
            "disk" => typeof(DiskPage),
            "history" => typeof(HistoryPage),
            _ => typeof(SettingsPage),
        });
    }

    private async void OnRestartAsAdmin(object sender, RoutedEventArgs e)
    {
        var ok = await App.Get<DialogService>().ConfirmAsync("Uruchom jako administrator",
            "Program zostanie zamknięty i uruchomiony ponownie z uprawnieniami administratora (okno UAC). Zastosowane zmiany pozostaną zapisane.", "Uruchom ponownie");
        if (ok && ElevationService.RestartElevated()) Application.Current.Exit();
    }
}
