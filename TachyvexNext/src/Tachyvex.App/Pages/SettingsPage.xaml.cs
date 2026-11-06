using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.Get<SettingsViewModel>();
    public SettingsPage() { InitializeComponent(); }

}
