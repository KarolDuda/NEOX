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
        foreach (var c in new FrameworkElement[] { CardCpu, CardRam, CardGpu, CardHw }) Fx.Hover(c, 1.01f);
        Loaded += (_, _) =>
        {
            var i = 0;
            foreach (var e in new UIElement[] { Hero, CardCpu, CardRam, CardGpu, CardHw }) Fx.SlideIn(e, 22, 70 * i++);
            Fx.Breathe(BoostGlow);
        };
    }

    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile p }) await ViewModel.ApplyProfileCommand.ExecuteAsync(p);
    }

    private void OnProfileApplied(ProfileApplyReport rep)
    {
        OverlayTitle.Text = rep.Failed == 0 ? $"Zastosowano {rep.Applied} optymalizacji" : $"Zastosowano {rep.Applied}, błędów: {rep.Failed}";
        OverlayText.Text = (rep.Restart != RestartRequirement.None ? "Aby wszystkie zmiany zadziałały, uruchom ponownie komputer. " : "")
                           + "Każdą zmianę możesz cofnąć w zakładce Historia.";
        Overlay.Visibility = Visibility.Visible;
        Fx.SlideIn(Overlay, 0, 0, 250);
        Fx.Pop(BigCheck, 120);
        Fx.Ripple(Wave1, 0, 1100, 1.6f);
        Fx.Ripple(Wave2, 350, 1100, 1.9f);
    }

    private void OnCloseOverlay(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ProfileApplied += OnProfileApplied;
        await ViewModel.OnShownAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.ProfileApplied -= OnProfileApplied;
        Fx.StopBreathe(BoostGlow);
        ViewModel.Dispose();
    }
}
