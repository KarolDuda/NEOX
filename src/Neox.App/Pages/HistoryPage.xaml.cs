using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = App.Get<HistoryViewModel>();
    public HistoryPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); ViewModel.Reload(); }
}
