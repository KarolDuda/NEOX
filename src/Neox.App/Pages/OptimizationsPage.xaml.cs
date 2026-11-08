using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class OptimizationsPage : Page
{
    public OptimizationsViewModel ViewModel { get; } = App.Get<OptimizationsViewModel>();
    public OptimizationsPage() { InitializeComponent();
        Loaded += (_, _) => Neox.App.Services.Fx.Cascade(this); }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.RefreshStatesAsync(); }
}
