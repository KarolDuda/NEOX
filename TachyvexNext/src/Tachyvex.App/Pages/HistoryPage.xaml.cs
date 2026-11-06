using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = App.Get<HistoryViewModel>();
    public HistoryPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); ViewModel.Reload(); }
}
