using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class OptimizationsPage : Page
{
    public OptimizationsViewModel ViewModel { get; } = App.Get<OptimizationsViewModel>();
    public OptimizationsPage() { InitializeComponent(); }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.RefreshStatesAsync(); }
}
