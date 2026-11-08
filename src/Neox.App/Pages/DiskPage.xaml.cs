using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Neox.App.ViewModels;

namespace Neox.App.Pages;

public sealed partial class DiskPage : Page
{
    public DiskViewModel ViewModel { get; } = App.Get<DiskViewModel>();
    public DiskPage() { InitializeComponent(); }

}
