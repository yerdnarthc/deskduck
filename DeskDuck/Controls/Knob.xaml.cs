using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
// WinForms implicit usings collide on these names; WPF wins in this file.
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Application = System.Windows.Application;

namespace DeskDuck.Controls;

/// <summary>
/// FL-style rotary knob: drag vertically to change, wheel for steps,
/// arrow keys when focused, double-click to reset to default.
/// Value always snaps to Step so bound integer settings never jitter.
/// </summary>
public partial class Knob : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Knob),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Knob),
            new PropertyMetadata(100.0));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(Knob),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(Knob),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty DefaultValueProperty =
        DependencyProperty.Register(nameof(DefaultValue), typeof(double), typeof(Knob),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(Knob),
            new PropertyMetadata("", OnLabelChanged));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(Knob),
            new PropertyMetadata("", OnLabelChanged));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, Snap(value)); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    private bool _dragging;
    private double _dragStartY;
    private double _dragStartValue;

    public Knob()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshText();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (Knob)d;
        // Re-snap (e.g. a bound int pushing back a rounded value is a no-op).
        double snapped = knob.Snap((double)e.NewValue);
        if (snapped != (double)e.NewValue)
        {
            knob.SetCurrentValue(ValueProperty, snapped);
            return;
        }
        knob.RefreshText();
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Knob knob && knob.ValueText is not null)
            knob.RefreshText();
    }

    private double Snap(double value)
    {
        double step = Step <= 0 ? 1 : Step;
        double clamped = Math.Clamp(value, Minimum, Maximum);
        return Math.Round((clamped - Minimum) / step) * step + Minimum;
    }

    private void RefreshText()
    {
        ValueText.Text = string.IsNullOrEmpty(Unit) ? $"{Value:0}" : $"{Value:0} {Unit}";
        CaptionText.Text = Caption;
        AutomationProperties.SetName(this, string.IsNullOrEmpty(Unit)
            ? $"{Caption}: {Value:0}"
            : $"{Caption}: {Value:0} {Unit}");
    }

    // ---- pointer interaction: vertical drag, Shift = fine ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _dragging = true;
        Dial.IsDragging = true;
        _dragStartY = e.GetPosition(this).Y;
        _dragStartValue = Value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        double range = Maximum - Minimum;
        if (range <= 0) return;
        // Full range per 200px; Shift stretches to 800px for fine control.
        double pixels = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 800 : 200;
        double delta = (_dragStartY - e.GetPosition(this).Y) * range / pixels;
        SetCurrentValue(ValueProperty, Snap(_dragStartValue + delta));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        Dial.IsDragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        Dial.IsDragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double step = (Step <= 0 ? 1 : Step) * 2;
        SetCurrentValue(ValueProperty, Snap(Value + Math.Sign(e.Delta) * step));
        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        SetCurrentValue(ValueProperty, Snap(DefaultValue));
        e.Handled = true;
    }

    // ---- keyboard: arrows / PgUp/PgDn / Home / End ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Step <= 0 ? 1 : Step;
        double? target = e.Key switch
        {
            Key.Up or Key.Right => Value + step,
            Key.Down or Key.Left => Value - step,
            Key.PageUp => Value + (Maximum - Minimum) / 10,
            Key.PageDown => Value - (Maximum - Minimum) / 10,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (target.HasValue)
        {
            SetCurrentValue(ValueProperty, Snap(target.Value));
            e.Handled = true;
        }
    }
}

/// <summary>
/// Draws the dial: track arc, filled value arc, tick marks, needle.
/// Redraws automatically when any bound DP changes (AffectsRender).
/// Angles are clockwise from 12 o'clock: min -135°, max +135° (FL-style).
/// </summary>
public sealed class DialDisplay : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(DialDisplay),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(DialDisplay),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(DialDisplay),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.Register(nameof(IsDragging), typeof(bool), typeof(DialDisplay),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsDragging { get => (bool)GetValue(IsDraggingProperty); set => SetValue(IsDraggingProperty, value); }

    private const double MinAngle = -135;
    private const double MaxAngle = 135;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 9;
        double range = Maximum - Minimum;
        double fraction = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        double currentAngle = MinAngle + fraction * (MaxAngle - MinAngle);
        bool enabled = IsEnabled;

        // Theme-aware: pull token brushes so the dial follows Styles.xaml.
        // Hardcoded fallbacks keep the control usable without the dictionary.
        Brush trackBrush = enabled ? FindBrush("TrackBrush", Color.FromRgb(0xD4, 0xD4, 0xD4)) : FindBrush("FaintBrush", Colors.Gray);
        Brush accentBrush = enabled ? FindBrush("AccentBrush", Color.FromRgb(0xFA, 0xAC, 0x43)) : FindBrush("FaintBrush", Colors.Gray);
        Brush tickBrush = enabled ? FindBrush("FaintBrush", Color.FromRgb(0xA6, 0xA6, 0xA6)) : FindBrush("FaintBrush", Colors.Gray);
        Brush needleBrush = enabled ? FindBrush("TextBrush", Color.FromRgb(0xDF, 0xDC, 0xDC)) : FindBrush("FaintBrush", Colors.Gray);

        // Ticks: 11 positions, major every 5th.
        for (int i = 0; i <= 10; i++)
        {
            double angle = MinAngle + i * (MaxAngle - MinAngle) / 10;
            bool major = i % 5 == 0;
            var outer = PointAt(center, radius + 7, angle);
            var inner = PointAt(center, radius + (major ? 2 : 4), angle);
            dc.DrawLine(new Pen(tickBrush, major ? 1.6 : 1.0), inner, outer);
        }

        // Track (full 270° sweep) + filled value arc (thicker while dragging).
        double arcWidth = IsDragging ? 8.5 : 6;
        DrawArc(dc, center, radius, MinAngle, MaxAngle, trackBrush, 6);
        if (fraction > 0.001)
            DrawArc(dc, center, radius, MinAngle, currentAngle, accentBrush, arcWidth);

        // Needle + center cap.
        var tip = PointAt(center, radius - 9, currentAngle);
        dc.DrawLine(new Pen(needleBrush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            center, tip);
        dc.DrawEllipse(needleBrush, null, center, 4, 4);
    }

    private static Brush FindBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static void DrawArc(DrawingContext dc, Point center, double radius,
        double fromAngle, double toAngle, Brush brush, double thickness)
    {
        double sweep = toAngle - fromAngle;
        if (sweep <= 0) return;
        var figure = new PathFigure
        {
            StartPoint = PointAt(center, radius, fromAngle),
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment(
            PointAt(center, radius, toAngle),
            new Size(radius, radius), 0,
            sweep > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        }, geometry);
    }

    private static Point PointAt(Point center, double radius, double angleDeg)
    {
        double radians = angleDeg * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Sin(radians),
            center.Y - radius * Math.Cos(radians));
    }
}
