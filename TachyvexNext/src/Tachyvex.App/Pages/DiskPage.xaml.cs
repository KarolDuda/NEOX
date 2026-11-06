using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class DiskPage : Page
{
    public DiskViewModel ViewModel { get; } = App.Get<DiskViewModel>();
    public DiskPage() { InitializeComponent(); }

}
