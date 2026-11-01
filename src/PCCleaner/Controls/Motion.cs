using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PCCleaner.Controls;

/// <summary>
/// Whether decorative animation should run. Off when Windows' "Animation effects" setting is off
/// (SystemParameters.ClientAreaAnimation) or when the user enables "Reduce animations" in Settings.
/// </summary>
public sealed partial class MotionSettings : ObservableObject
{
    private MotionSettings()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) OnPropertyChanged(nameof(IsReducedMotion));
        };
    }

    public static MotionSettings Instance { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReducedMotion))]
    public partial bool UserPrefersReducedMotion { get; set; }

    public bool IsReducedMotion => UserPrefersReducedMotion || !SystemParameters.ClientAreaAnimation;

    /// <summary>The given duration, or zero when motion is reduced.</summary>
    public static Duration For(double milliseconds) =>
        Instance.IsReducedMotion ? new Duration(TimeSpan.Zero) : new Duration(TimeSpan.FromMilliseconds(milliseconds));
}

/// <summary>
/// Lightweight attached animations: <c>Motion.Entrance</c> (fade + short slide-up when loaded, optionally staggered with
/// <c>Motion.Delay</c>) and <c>Motion.Interactive</c> (hover lift and press scale). All are skipped under reduced motion.
/// </summary>
public static class Motion
{
    private const double EntranceOffset = 12;

    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEntranceChanged));

    public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
        "Delay", typeof(int), typeof(Motion), new PropertyMetadata(0));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached(
        "Interactive", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnInteractiveChanged));

    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);

    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);

    /// <summary>Entrance start delay in milliseconds (for staggering cards).</summary>
    public static int GetDelay(DependencyObject d) => (int)d.GetValue(DelayProperty);

    public static void SetDelay(DependencyObject d, int value) => d.SetValue(DelayProperty, value);

    public static bool GetInteractive(DependencyObject d) => (bool)d.GetValue(InteractiveProperty);

    public static void SetInteractive(DependencyObject d, bool value) => d.SetValue(InteractiveProperty, value);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= PlayEntrance;
        if ((bool)e.NewValue) element.Loaded += PlayEntrance;
    }

    private static void PlayEntrance(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var (_, translate) = EnsureTransforms(element);

        if (MotionSettings.Instance.IsReducedMotion)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            element.Opacity = 1;
            translate.Y = 0;
            return;
        }

        // Key frames (not BeginTime) so the element stays hidden during its stagger delay instead of flashing in.
        var delay = TimeSpan.FromMilliseconds(GetDelay(element));
        var end = delay + TimeSpan.FromMilliseconds(320);
        element.BeginAnimation(UIElement.OpacityProperty, Frames(0, 1, delay, end));
        translate.BeginAnimation(TranslateTransform.YProperty, Frames(EntranceOffset, 0, delay, end));
    }

    private static DoubleAnimationUsingKeyFrames Frames(double from, double to, TimeSpan start, TimeSpan end) => new()
    {
        FillBehavior = FillBehavior.Stop,
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(start)),
            new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(end), EaseOut),
        },
    };

    private static void OnInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.MouseEnter -= OnMouseEnter;
        element.MouseLeave -= OnMouseLeave;
        element.PreviewMouseLeftButtonDown -= OnPressed;
        element.PreviewMouseLeftButtonUp -= OnReleased;
        if (!(bool)e.NewValue) return;

        element.MouseEnter += OnMouseEnter;
        element.MouseLeave += OnMouseLeave;
        element.PreviewMouseLeftButtonDown += OnPressed;
        element.PreviewMouseLeftButtonUp += OnReleased;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e) => Lift((FrameworkElement)sender, -2);

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Lift((FrameworkElement)sender, 0);
        Scale((FrameworkElement)sender, 1);
    }

    private static void OnPressed(object sender, MouseButtonEventArgs e) => Scale((FrameworkElement)sender, 0.98);

    private static void OnReleased(object sender, MouseButtonEventArgs e) => Scale((FrameworkElement)sender, 1);

    private static void Lift(FrameworkElement element, double y)
    {
        if (!element.IsEnabled) y = 0;
        var (_, translate) = EnsureTransforms(element);
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, MotionSettings.For(160)) { EasingFunction = EaseOut });
    }

    private static void Scale(FrameworkElement element, double value)
    {
        var (scale, _) = EnsureTransforms(element);
        var animation = new DoubleAnimation(value, MotionSettings.For(110)) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>Gives the element a (Scale, Translate) render transform owned by these behaviors, reusing it if present.</summary>
    private static (ScaleTransform Scale, TranslateTransform Translate) EnsureTransforms(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] })
            return (s, t);

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        return (scale, translate);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
