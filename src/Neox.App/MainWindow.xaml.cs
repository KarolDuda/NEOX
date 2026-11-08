using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neox.App.Pages;
using Neox.App.Services;
using Neox.Core.Abstractions;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.Logging;
using Neox.Core.State;
using Neox.Windows;
using Neox.Windows.Infrastructure;

namespace Neox.App;

/// <summary>Pozycja menu bocznego.</summary>
public sealed record NavItem(string Tag, string Title, string Glyph);

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 820));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "neox.ico")); } catch (Exception) { /* ikona opcjonalna */ }

        var dialogs = App.Get<DialogService>();
        ((FrameworkElement)Content).Loaded += (_, _) => dialogs.Root = Content.XamlRoot;
        AdminBar.IsOpen = !App.Get<IElevationService>().IsElevated;
        VersionLabel.Text = "version " + UpdateService.Current;
        NavList.ItemsSource = Items;
        NavList.SelectedIndex = 0;
        ((FrameworkElement)Content).Loaded += async (_, _) => { StartAmbient(); await Task.Delay(350); MoveIndicator(); };
        Splash.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(500) };
        ((FrameworkElement)Content).Loaded += async (_, _) => await RunIntroAsync();
        _ = AutoUpdateAsync();
    }

    private static readonly NavItem[] Items =
    {
        new("dashboard", "Dashboard", "\uE80F"),
        new("autotune", "Auto-Tune", "\uE945"),
        new("gaming", "Gaming & profiles", "\uE7FC"),
        new("optimizations", "Optimizations", "\uE9E9"),
        new("network", "Network", "\uE774"),
        new("system", "System", "\uE7F4"),
        new("managers", "Startup & services", "\uE8FD"),
        new("disk", "Disk", "\uE958"),
        new("history", "History & restore", "\uE81C"),
        new("settings", "Settings", "\uE713"),
    };

    /// <summary>Wolny dryf aurory w tle i oddychająca poświata logo.</summary>
    private void StartAmbient()
    {
        try
        {
            Fx.Drift(Aurora1, 140, 90, 14000);
            Fx.Drift(Aurora2, -160, -80, 17000, 1500);
            Fx.Drift(Aurora3, 90, -110, 21000, 800);
            Fx.Breathe(LogoGlow, 0.18f, 0.5f, 3200);
        }
        catch (Exception ex) { App.Get<ILogger<MainWindow>>().LogInformation(ex, "Ambient animations skipped"); }
    }

    private bool _skipIntro;
    private void OnSplashTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _skipIntro = true;

    /// <summary>Krótkie intro (ok. 2 s, pomijalne kliknięciem), a przy pierwszym uruchomieniu propozycja Auto-Tune.</summary>
    private async Task RunIntroAsync()
    {
        try
        {
            Fx.Pop(SplashLogo, 0);
            Fx.Breathe(SplashGlow, 0.1f, 0.55f, 2200);
            Fx.Ripple(SplashWave1, 250, 1300, 2.3f);
            Fx.Ripple(SplashWave2, 600, 1300, 2.9f);
            Fx.SlideIn(SplashTitle, 16, 350, 650);
            Fx.SlideIn(SplashTag, 10, 650, 650);
            for (var i = 0; i <= 20 && !_skipIntro; i++) { SplashBar.Value = i * 5; await Task.Delay(65); }
        }
        catch (Exception ex) { App.Get<ILogger<MainWindow>>().LogInformation(ex, "Intro skipped"); }
        Splash.Opacity = 0;
        await Task.Delay(550);
        Splash.Visibility = Visibility.Collapsed;

        var store = App.Get<IStateStore>();
        if (!store.State.Settings.FirstRunDone)
        {
            store.State.Settings.FirstRunDone = true;
            await store.SaveAsync();
            var go = await App.Get<DialogService>().ConfirmAsync("Welcome to NEOX",
                "Auto-Tune scans your PC and picks safe optimizations. You will see the plan before anything changes, and you can undo everything with one button.\n\nOpen it now?", "Open Auto-Tune");
            if (go) Navigate("autotune");
        }
    }

    /// <summary>Przechodzi do strony po etykiecie (używane z kart na Panelu).</summary>
    public void Navigate(string tag)
    {
        var i = Array.FindIndex(Items, x => x.Tag == tag);
        if (i >= 0) NavList.SelectedIndex = i;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item) return;
        ContentFrame.Navigate(PageFor(item.Tag), null, new SuppressNavigationTransitionInfo());
        Fx.SlideIn(ContentFrame, 26, 0, 520);
        MoveIndicator();
    }

    private void MoveIndicator()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (NavList.ContainerFromItem(NavList.SelectedItem) is not FrameworkElement c || c.ActualHeight <= 0) return;
                var y = c.TransformToVisual(NavHost).TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y + (c.ActualHeight - NavIndicator.Height) / 2;
                NavIndicator.Opacity = 1;
                Fx.SlideTo(NavIndicator, (float)y);
            }
            catch (Exception ex) { App.Get<ILogger<MainWindow>>().LogInformation(ex, "Indicator skipped"); }
        });
    }

    private static Type PageFor(string tag) => tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "autotune" => typeof(AutoTunePage),
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

            UpdateBar.Title = $"Updating to version {info.Version}";
            UpdateBar.Message = "Downloading…";
            UpdateBar.IsOpen = true;
            var progress = new Progress<double>(p => UpdateBar.Message = $"Downloading… {p:P0}");
            var path = await updates.DownloadAsync(info, progress);

            UpdateBar.Message = "Installing. NEOX will restart (confirm the UAC prompt).";
            await Task.Delay(2500);
            if (updates.LaunchInstaller(path)) Application.Current.Exit();
            else UpdateBar.Message = "Installation cancelled. It will be retried on the next launch, or you can update from Settings.";
        }
        catch (Exception ex)
        {
            App.Get<ILogger<MainWindow>>().LogWarning(ex, "Automatic update failed");
            UpdateBar.IsOpen = false;
        }
    }

    private async void OnRestartAsAdmin(object sender, RoutedEventArgs e)
    {
        var ok = await App.Get<DialogService>().ConfirmAsync("Run as administrator",
            "NEOX will close and restart with administrator rights (UAC prompt). Changes you have applied stay saved.", "Restart");
        if (ok && ElevationService.RestartElevated()) Application.Current.Exit();
    }
}
