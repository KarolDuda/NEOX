using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tachyvex.App.ViewModels;
using Tachyvex.Core.State;
using Tachyvex.Windows;
using global::Windows.Storage.Pickers;

namespace Tachyvex.App.Pages;

public sealed partial class GamingPage : Page
{
    public GamingViewModel ViewModel { get; } = App.Get<GamingViewModel>();
    public GamingPage() { InitializeComponent(); }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile p }) await ViewModel.ApplyProfileCommand.ExecuteAsync(p);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile p }) await ViewModel.DeleteProfileCommand.ExecuteAsync(p);
    }

    private void OnUseGame(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: DetectedGame g }) ViewModel.UseGameCommand.Execute(g);
    }

    private async void OnPickExe(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.GameExePath = file.Path;
    }
}
