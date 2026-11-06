using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class NetworkPage : Page
{
    public NetworkViewModel ViewModel { get; } = App.Get<NetworkViewModel>();
    public NetworkPage() { InitializeComponent(); }

}
