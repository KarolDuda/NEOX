using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class SystemPage : Page
{
    public SystemViewModel ViewModel { get; } = App.Get<SystemViewModel>();
    public SystemPage() { InitializeComponent(); }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.LoadAsync(); }
}
