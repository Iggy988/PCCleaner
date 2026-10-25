using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCCleaner.Controls;

/// <summary>A 270° circular gauge (0-100) that animates between values and colors itself by severity.</summary>
public partial class ArcGauge : UserControl
{
    private const double StartAngle = 135;
    private const double TotalSweep = 270;

    private static readonly (double Threshold, Color Color)[] Severity =
    [
        (85, Color.FromRgb(0xEF, 0x44, 0x44)),
        (60, Color.FromRgb(0xF5, 0x9E, 0x0B)),
        (0, Color.FromRgb(0x22, 0xC5, 0x5E)),
    ];

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ArcGauge), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ArcGauge), new PropertyMetadata(string.Empty, (d, e) => ((ArcGauge)d).LabelText.Text = (string)e.NewValue));

    private static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        "AnimatedValue", typeof(double), typeof(ArcGauge), new PropertyMetadata(0d, (d, _) => ((ArcGauge)d).UpdateArc()));

    public ArcGauge()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateArc();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private double AnimatedValue => (double)GetValue(AnimatedValueProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (ArcGauge)d;
        var animation = new DoubleAnimation(Math.Clamp((double)e.NewValue, 0, 100), TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        gauge.BeginAnimation(AnimatedValueProperty, animation);
    }

    private void UpdateArc()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var thickness = ValuePath.StrokeThickness;
        var radius = (size - thickness) / 2 - 4;
        var center = size / 2;
        var value = AnimatedValue;

        TrackPath.Data = CreateArc(center, radius, StartAngle, TotalSweep);

        var sweep = TotalSweep * value / 100;
        ValuePath.Data = sweep < 0.5 ? null : CreateArc(center, radius, StartAngle, sweep);

        var color = Severity.First(s => value >= s.Threshold).Color;
        if (ValuePath.Stroke is not SolidColorBrush brush || brush.Color != color)
        {
            ValuePath.Stroke = new SolidColorBrush(color);
            Glow.Color = color;
        }

        ValueText.Text = $"{value:0}%";
    }

    private static PathGeometry CreateArc(double center, double radius, double startDegrees, double sweepDegrees)
    {
        Point PointAt(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        }

        var figure = new PathFigure { StartPoint = PointAt(startDegrees), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(PointAt(startDegrees + sweepDegrees), new Size(radius, radius), 0,
            sweepDegrees > 180, SweepDirection.Clockwise, isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
