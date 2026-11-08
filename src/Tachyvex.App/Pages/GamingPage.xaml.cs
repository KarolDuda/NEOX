using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tachyvex.App.ViewModels;
using Tachyvex.Core.State;
using Tachyvex.Windows;
using global::Windows.Storage.Pickers;

namespace Tachyvex.App.Pages;

public sealed partial class GamingPage : Page
{
    public GamingViewModel ViewModel { get; } = App.Get<GamingViewModel>();
    public GamingPage() { InitializeComponent(); }

    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); ViewModel.Attach(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { base.OnNavigatedFrom(e); ViewModel.Detach(); }

    private static void Init(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Profile p }) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = string.Concat(p.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) is { Length: > 0 } n ? n : "profil" };
            picker.FileTypeChoices.Add("NEOX profile (JSON)", new List<string> { ".json" });
            Init(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, ViewModel.ExportJson(p));
            ViewModel.Message = $"Exported profile “{p.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ViewModel.Message = "Could not save the file: " + ex.Message; }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            Init(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            if (new FileInfo(file.Path).Length > Tachyvex.Core.Profiles.ProfileTransfer.MaxBytes) { ViewModel.Message = "The file is too large to be a profile."; return; }
            await ViewModel.ImportJsonAsync(await File.ReadAllTextAsync(file.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ViewModel.Message = "Could not read the file: " + ex.Message; }
    }

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
