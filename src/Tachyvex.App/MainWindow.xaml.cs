using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tachyvex.App.Pages;
using Tachyvex.App.Services;
using Tachyvex.Core.Abstractions;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.Logging;
using Tachyvex.Core.State;
using Tachyvex.Windows;
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
        VersionLabel.Text = "wersja " + UpdateService.Current;
        Nav.SelectedItem = Nav.MenuItems[0];
        _ = AutoUpdateAsync();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        ContentFrame.Navigate(PageFor(tag), null, new DrillInNavigationTransitionInfo());
    }

    private static Type PageFor(string tag) => tag switch
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
    };

    /// <summary>Sprawdza wydania w tle; przy włączonej automatyce pobiera i cicho instaluje nową wersję.</summary>
    private async Task AutoUpdateAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (!App.Get<IStateStore>().State.Settings.AutoUpdate) return;
            var updates = App.Get<UpdateService>();
            var (info, _) = await updates.CheckAsync();
            if (info is null) return;

            UpdateBar.Title = $"Aktualizacja do wersji {info.Version}";
            UpdateBar.Message = "Pobieram…";
            UpdateBar.IsOpen = true;
            var progress = new Progress<double>(p => UpdateBar.Message = $"Pobieram… {p:P0}");
            var path = await updates.DownloadAsync(info, progress);

            UpdateBar.Message = "Instaluję. Program uruchomi się ponownie (potwierdź okno UAC).";
            await Task.Delay(2500);
            if (updates.LaunchInstaller(path)) Application.Current.Exit();
            else UpdateBar.Message = "Instalacja anulowana. Spróbujesz ponownie przy następnym uruchomieniu albo w Ustawieniach.";
        }
        catch (Exception ex)
        {
            App.Get<ILogger<MainWindow>>().LogWarning(ex, "Automatyczna aktualizacja nie powiodła się");
            UpdateBar.IsOpen = false;
        }
    }

    private async void OnRestartAsAdmin(object sender, RoutedEventArgs e)
    {
        var ok = await App.Get<DialogService>().ConfirmAsync("Uruchom jako administrator",
            "Program zostanie zamknięty i uruchomiony ponownie z uprawnieniami administratora (okno UAC). Zastosowane zmiany pozostaną zapisane.", "Uruchom ponownie");
        if (ok && ElevationService.RestartElevated()) Application.Current.Exit();
    }
}
