using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using PCCleaner.Services;
using PCCleaner.Services.Localization;

namespace PCCleaner.Controls;

public enum NumberFormat
{
    Integer,
    Bytes,
}

/// <summary>
/// A TextBlock that counts up/down to <see cref="Value"/> (ease-out, ~0.7 s) and formats it as a localized number or
/// file size. Jumps straight to the value under reduced motion. Don't bind <c>Text</c>; bind <c>Value</c>.
/// </summary>
public sealed class AnimatedNumber : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(NumberFormat), typeof(AnimatedNumber), new PropertyMetadata(NumberFormat.Integer, (d, _) => ((AnimatedNumber)d).Render()));

    private static readonly DependencyProperty DisplayedValueProperty = DependencyProperty.Register(
        "DisplayedValue", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0d, (d, _) => ((AnimatedNumber)d).Render()));

    public AnimatedNumber()
    {
        Loaded += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            // Count up from zero each time the page appears.
            if (MotionSettings.Instance.IsReducedMotion || Value <= 0) Render();
            else AnimateTo(0, Value);
        };
        Unloaded += (_, _) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        Render();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public NumberFormat Format
    {
        get => (NumberFormat)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    private double DisplayedValue => (double)GetValue(DisplayedValueProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var number = (AnimatedNumber)d;
        var target = (double)e.NewValue;
        if (MotionSettings.Instance.IsReducedMotion || !number.IsLoaded)
        {
            number.BeginAnimation(DisplayedValueProperty, null);
            number.SetValue(DisplayedValueProperty, target);
            return;
        }

        number.AnimateTo(number.DisplayedValue, target);
    }

    private void AnimateTo(double from, double to) =>
        BeginAnimation(DisplayedValueProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

    private void OnLanguageChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var value = Math.Max(0, DisplayedValue);
        Text = Format == NumberFormat.Bytes
            ? ByteSize.Format(Math.Round(value))
            : Math.Round(value).ToString("N0", CultureInfo.CurrentCulture);
    }
}
