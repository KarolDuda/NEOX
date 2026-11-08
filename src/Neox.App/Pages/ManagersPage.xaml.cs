using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class ManagersPage : Page
{
    public ManagersViewModel ViewModel { get; } = App.Get<ManagersViewModel>();
    public ManagersPage() { InitializeComponent(); }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.LoadAsync(); }
}
