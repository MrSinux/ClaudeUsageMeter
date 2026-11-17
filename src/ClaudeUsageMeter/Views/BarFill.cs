using System.Windows;
using System.Windows.Media.Animation;
using ClaudeUsageMeter.Native;

namespace ClaudeUsageMeter.Views;

/// <summary>
/// Attached behaviour for a bar's fill element: set <c>BarFill.Fraction</c> (0..1) and the element's
/// width animates to that fraction of its parent's width. Width (not a scale transform) keeps the
/// rounded ends round at every value.
/// </summary>
public static class BarFill
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.RegisterAttached(
        "Fraction", typeof(double), typeof(BarFill), new PropertyMetadata(double.NaN, OnFractionChanged));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached(
        "Shown", typeof(double), typeof(BarFill), new PropertyMetadata(0.0, OnShownChanged));

    public static double GetFraction(DependencyObject d) => (double)d.GetValue(FractionProperty);
    public static void SetFraction(DependencyObject d, double value) => d.SetValue(FractionProperty, value);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        var to = Math.Clamp((double)e.NewValue, 0, 1);
        if (double.IsNaN((double)e.NewValue)) return;

        if (fe.Parent is FrameworkElement parent && fe.Tag is null)
        {
            fe.Tag = "hooked";
            parent.SizeChanged += (_, _) => Layout(fe);
        }

        if (WindowEffects.ReducedMotion || !fe.IsLoaded)
        {
            fe.BeginAnimation(ShownProperty, null);
            fe.SetValue(ShownProperty, to);
            if (!fe.IsLoaded) fe.Loaded += (_, _) => Layout(fe);
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fe.BeginAnimation(ShownProperty, anim);
    }

    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe) Layout(fe);
    }

    private static void Layout(FrameworkElement fe)
    {
        if (fe.Parent is not FrameworkElement parent) return;
        var shown = (double)fe.GetValue(ShownProperty);
        var full = parent.ActualWidth;
        // Keep a visible nub for small non-zero values so 1% doesn't look like 0%.
        var w = shown <= 0 ? 0 : Math.Max(fe.MinWidth, full * shown);
        fe.Width = Math.Min(full, w);
    }
}
