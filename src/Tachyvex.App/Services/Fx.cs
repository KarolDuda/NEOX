using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Tachyvex.App.Services;

/// <summary>Lekkie animacje oparte na Composition (GPU, bez pętli odświeżania w kodzie).</summary>
public static class Fx
{
    private static Visual V(UIElement e) => ElementCompositionPreview.GetElementVisual(e);

    private static void Center(UIElement e)
    {
        var v = V(e);
        v.CenterPoint = new Vector3(e.ActualSize.X / 2f, e.ActualSize.Y / 2f, 0f);
    }

    /// <summary>Delikatne powiększenie po najechaniu myszą.</summary>
    public static void Hover(FrameworkElement e, float scale = 1.01f)
    {
        e.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        e.SizeChanged += (_, a) => e.CenterPoint = new Vector3((float)a.NewSize.Width / 2f, (float)a.NewSize.Height / 2f, 0f);
        e.PointerEntered += (_, _) => e.Scale = new Vector3(scale, scale, 1f);
        e.PointerExited += (_, _) => e.Scale = Vector3.One;
    }

    /// <summary>Wyskok z lekkim odbiciem (np. znaczek zastosowania).</summary>
    public static void Pop(UIElement e, int delayMs = 0)
    {
        Center(e);
        var v = V(e); var c = v.Compositor;
        var s = c.CreateVector3KeyFrameAnimation();
        s.InsertKeyFrame(0f, new Vector3(0.2f, 0.2f, 1f));
        s.InsertKeyFrame(0.6f, new Vector3(1.25f, 1.25f, 1f));
        s.InsertKeyFrame(1f, Vector3.One);
        s.Duration = TimeSpan.FromMilliseconds(520);
        s.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        var o = c.CreateScalarKeyFrameAnimation();
        o.InsertKeyFrame(0f, 0f); o.InsertKeyFrame(0.3f, 1f); o.InsertKeyFrame(1f, 1f);
        o.Duration = TimeSpan.FromMilliseconds(520);
        o.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Scale", s);
        v.StartAnimation("Opacity", o);
    }

    /// <summary>Błysk obramowania: 0 → 1 → 0.</summary>
    public static void Flash(UIElement e, int durationMs = 1100)
    {
        var v = V(e);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 0f); a.InsertKeyFrame(0.2f, 1f); a.InsertKeyFrame(1f, 0f);
        a.Duration = TimeSpan.FromMilliseconds(durationMs);
        v.StartAnimation("Opacity", a);
    }

    /// <summary>Rozchodzący się pierścień (fala po włączeniu).</summary>
    public static void Ripple(UIElement e, int delayMs = 0, int durationMs = 900, float to = 2.6f)
    {
        Center(e);
        var v = V(e); var c = v.Compositor;
        var s = c.CreateVector3KeyFrameAnimation();
        s.InsertKeyFrame(0f, new Vector3(0.4f, 0.4f, 1f));
        s.InsertKeyFrame(1f, new Vector3(to, to, 1f), c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.8f), new Vector2(0.3f, 1f)));
        s.Duration = TimeSpan.FromMilliseconds(durationMs);
        s.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        var o = c.CreateScalarKeyFrameAnimation();
        o.InsertKeyFrame(0f, 0.85f); o.InsertKeyFrame(1f, 0f);
        o.Duration = TimeSpan.FromMilliseconds(durationMs);
        o.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Scale", s);
        v.StartAnimation("Opacity", o);
    }

    /// <summary>Płynne wejście: przesunięcie z dołu + pojawienie się.</summary>
    public static void SlideIn(UIElement e, float fromY = 18f, int delayMs = 0, int durationMs = 450)
    {
        var v = V(e); var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var off = c.CreateVector3KeyFrameAnimation();
        off.InsertKeyFrame(0f, new Vector3(0f, fromY, 0f)); off.InsertKeyFrame(1f, Vector3.Zero, ease);
        off.Duration = TimeSpan.FromMilliseconds(durationMs); off.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        var op = c.CreateScalarKeyFrameAnimation();
        op.InsertKeyFrame(0f, 0f); op.InsertKeyFrame(1f, 1f);
        op.Duration = TimeSpan.FromMilliseconds(durationMs); op.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        v.StartAnimation("Offset", off);
        v.StartAnimation("Opacity", op);
    }

    /// <summary>Delikatne „oddychanie” światła (pętla); zatrzymaj przez StopBreathe.</summary>
    public static void Breathe(UIElement e, float min = 0.25f, float max = 0.7f, int periodMs = 2600)
    {
        var v = V(e);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, min); a.InsertKeyFrame(0.5f, max); a.InsertKeyFrame(1f, min);
        a.Duration = TimeSpan.FromMilliseconds(periodMs);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", a);
    }

    /// <summary>Powolny dryf (pętla) elementu w tle, np. poświaty aurory.</summary>
    public static void Drift(UIElement e, float dx, float dy, int periodMs, int delayMs = 0)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        var v = V(e); var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, Vector3.Zero);
        a.InsertKeyFrame(0.5f, new Vector3(dx, dy, 0f), ease);
        a.InsertKeyFrame(1f, Vector3.Zero, ease);
        a.Duration = TimeSpan.FromMilliseconds(periodMs);
        a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation", a);
    }

    /// <summary>Przesuwa element płynnie (np. wskaźnik zaznaczenia w menu) do pozycji Y.</summary>
    public static void SlideTo(FrameworkElement e, float y, int durationMs = 320)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        var v = V(e); var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, new Vector3(0f, y, 0f), c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.2f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(durationMs);
        v.StartAnimation("Translation", a);
    }

    public static void StopBreathe(UIElement e) => V(e).StopAnimation("Opacity");
}
