using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class NetworkPage : Page
{
    public NetworkViewModel ViewModel { get; } = App.Get<NetworkViewModel>();
    public NetworkPage() { InitializeComponent(); }

}
