using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.Services;
using Tachyvex.App.ViewModels;
using Tachyvex.Core.Models;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;

namespace Tachyvex.App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = App.Get<DashboardViewModel>();

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        foreach (var c in new FrameworkElement[] { CardCpu, CardRam, CardGpu, CardHealth, CardHw }) Fx.Hover(c, 1.01f);
        Loaded += (_, _) =>
        {
            var i = 0;
            foreach (var e in new UIElement[] { Hero, CardCpu, CardRam, CardGpu, CardHealth, CardHw }) Fx.SlideIn(e, 22, 70 * i++);
            Fx.Breathe(BoostGlow);
        };
    }

    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile p }) await ViewModel.ApplyProfileCommand.ExecuteAsync(p);
    }

    private void OnProfileApplied(ProfileApplyReport rep)
    {
        OverlayTitle.Text = rep.Failed == 0 ? $"Applied {rep.Applied} optimizations" : $"Applied {rep.Applied}, failed: {rep.Failed}";
        OverlayText.Text = (rep.Restart != RestartRequirement.None ? "Restart your PC so all changes take effect. " : "")
                           + "You can undo every change in the History tab.";
        Overlay.Visibility = Visibility.Visible;
        Fx.SlideIn(Overlay, 0, 0, 250);
        Fx.Pop(BigCheck, 120);
        Fx.Ripple(Wave1, 0, 1100, 1.6f);
        Fx.Ripple(Wave2, 350, 1100, 1.9f);
    }

    private DispatcherTimer? _countTimer;

    /// <summary>Licznik 0 → wynik z łagodnym wyhamowaniem.</summary>
    private void OnHealthComputed(int target)
    {
        _countTimer?.Stop();
        var start = DateTime.UtcNow;
        const double ms = 1300;
        _countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _countTimer.Tick += (_, _) =>
        {
            var t = Math.Min(1.0, (DateTime.UtcNow - start).TotalMilliseconds / ms);
            var eased = 1 - Math.Pow(1 - t, 3);
            var v = target * eased;
            ScoreRing.Value = v;
            ScoreText.Text = ((int)Math.Round(v)).ToString();
            if (t >= 1.0) _countTimer?.Stop();
        };
        _countTimer.Start();
    }

    private async void OnFix(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HealthRow row }) await ViewModel.FixCommand.ExecuteAsync(row);
    }

    private void OnGoAutoTune(object sender, RoutedEventArgs e) => App.MainWindow?.Navigate("autotune");

    private void OnCloseOverlay(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ProfileApplied += OnProfileApplied;
        ViewModel.HealthComputed += OnHealthComputed;
        await ViewModel.OnShownAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.ProfileApplied -= OnProfileApplied;
        ViewModel.HealthComputed -= OnHealthComputed;
        _countTimer?.Stop();
        Fx.StopBreathe(BoostGlow);
        ViewModel.Dispose();
    }
}
