using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;
using Tachyvex.Core.State;

namespace Tachyvex.App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = App.Get<DashboardViewModel>();

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void OnApplyProfile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile p }) await ViewModel.ApplyProfileCommand.ExecuteAsync(p);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); await ViewModel.OnShownAsync(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { base.OnNavigatedFrom(e); ViewModel.Dispose(); }
}
