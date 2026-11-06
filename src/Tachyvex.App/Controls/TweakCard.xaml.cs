using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tachyvex.App.Services;
using Tachyvex.App.ViewModels;

namespace Tachyvex.App.Controls;

/// <summary>Karta jednej optymalizacji z animacją po zastosowaniu (błysk, fala, znaczek).</summary>
public sealed partial class TweakCard : UserControl
{
    public TweakItemViewModel? ViewModel { get; private set; }

    public TweakCard()
    {
        InitializeComponent();
        Fx.Hover(Card, 1.008f);
        CheckIcon.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(300) };
        IdleIcon.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(300) };
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnPropertyChanged;
            ViewModel.Applied -= OnApplied;
        }
        ViewModel = args.NewValue as TweakItemViewModel;
        Bindings.Update();
        if (ViewModel is null) return;
        ViewModel.PropertyChanged += OnPropertyChanged;
        ViewModel.Applied += OnApplied;
        RiskLabel.Text = ViewModel.RiskText;
        RiskLabel.Foreground = ViewModel.RiskBrush;
        Sync();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TweakItemViewModel.IsApplied)) Sync();
    }

    private void Sync()
    {
        if (ViewModel is null) return;
        var on = ViewModel.IsApplied;
        CheckIcon.Opacity = on ? 1 : 0;
        IdleIcon.Opacity = on ? 0 : 1;
        Ring.Stroke = on ? ThemeBrushes.Resource("GoodBrush") : ThemeBrushes.Resource("CardBorderBrush");
        PrimaryButton.Style = (Style)Application.Current.Resources[on ? "GhostButtonStyle" : "AccentButtonStyleX"];
    }

    /// <summary>Efekt tylko po akcji użytkownika, nie przy odczycie stanu.</summary>
    private void OnApplied(object? sender, EventArgs e)
    {
        Fx.Flash(Glow);
        Fx.Ripple(Wave);
        Fx.Pop(CheckIcon);
    }
}
