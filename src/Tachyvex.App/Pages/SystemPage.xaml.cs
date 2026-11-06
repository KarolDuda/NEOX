using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class SystemPage : Page
{
    public SystemViewModel ViewModel { get; } = App.Get<SystemViewModel>();
    public SystemPage() { InitializeComponent(); }
    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.LoadAsync(); }
}
