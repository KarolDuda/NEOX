using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.Services;
using Neox.App.ViewModels;
using Neox.Core.AutoTune;

namespace Neox.App.Pages;

public sealed partial class AutoTunePage : Page
{
    public AutoTuneViewModel ViewModel { get; } = App.Get<AutoTuneViewModel>();

    public AutoTunePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Fx.SlideIn(Hero, 22, 0);
            Fx.SlideIn(PlanCard, 22, 90);
            Fx.Breathe(RunGlow);
        };
    }

    private void OnCompleted(AutoTuneReport r)
    {
        Fx.SlideIn(ReportCard, 24, 0, 500);
        Fx.Pop(Check, 150);
        Fx.Ripple(Wave, 0, 1000, 2.2f);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Completed += OnCompleted;
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Completed -= OnCompleted;
        Fx.StopBreathe(RunGlow);
    }
}
