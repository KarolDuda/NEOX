using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neox.Core.Models;
using Neox.Core.State;

namespace Neox.App.Services;

/// <summary>Okna dialogowe osadzone w oknie głównym.</summary>
public sealed class DialogService
{
    public XamlRoot? Root { get; set; }

    public async Task<bool> ConfirmAsync(string title, string text, string primary = "Kontynuuj")
    {
        if (Root is null) return false;
        var dlg = new ContentDialog
        {
            XamlRoot = Root, Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, MaxHeight = 360 },
            PrimaryButtonText = primary, CloseButtonText = "Anuluj", DefaultButton = ContentDialogButton.Close,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task InfoAsync(string title, string text)
    {
        if (Root is null) return;
        var dlg = new ContentDialog
        {
            XamlRoot = Root, Title = title, CloseButtonText = "OK",
            Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, MaxHeight = 420 },
        };
        await dlg.ShowAsync();
    }

    public async Task<string?> PromptAsync(string title, string placeholder)
    {
        if (Root is null) return null;
        var box = new TextBox { PlaceholderText = placeholder };
        var dlg = new ContentDialog { XamlRoot = Root, Title = title, Content = box, PrimaryButtonText = "Zapisz", CloseButtonText = "Anuluj" };
        return await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}

/// <summary>Kontekst przekazywany do optymalizacji (ścieżka gry z ustawień).</summary>
public sealed class AppContextService
{
    private readonly IStateStore _store;
    public AppContextService(IStateStore store) => _store = store;
    public TweakContext Current => new(_store.State.Settings.GameExePath);
}
