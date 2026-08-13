using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Wadd.UI.Controls;

public partial class ClockTimePicker : UserControl
{
    private enum ClockMode { Hours, Minutes }

    private ClockMode _currentMode = ClockMode.Hours;
    private bool _isDragging;

    public static readonly StyledProperty<TimeSpan?> SelectedTimeProperty =
        AvaloniaProperty.Register<ClockTimePicker, TimeSpan?>(
            nameof(SelectedTime),
            defaultBindingMode: BindingMode.TwoWay);

    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public ClockTimePicker()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateUI();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedTimeProperty)
        {
            UpdateUI();
        }
    }

    private void OnHourHeaderClicked(object? sender, RoutedEventArgs e)
    {
        _currentMode = ClockMode.Hours;
        UpdateUI();
    }

    private void OnMinuteHeaderClicked(object? sender, RoutedEventArgs e)
    {
        _currentMode = ClockMode.Minutes;
        UpdateUI();
    }

    private void UpdateUI()
    {
        var time = SelectedTime ?? new TimeSpan(12, 0, 0);

        // Update trigger display text
        if (TimeDisplayTextBlock != null)
        {
            TimeDisplayTextBlock.Text = SelectedTime.HasValue
                ? $"{time.Hours:D2}:{time.Minutes:D2}"
                : "12:00";
        }

        var primaryBrush = GetThemeBrush("AppPrimaryBrush", Brushes.Teal);
        var textSecondaryBrush = GetThemeBrush("AppTextSecondaryBrush", Brushes.Gray);

        // Update header texts
        if (HourHeaderTextBlock != null)
        {
            HourHeaderTextBlock.Text = $"{time.Hours:D2}";
            HourHeaderTextBlock.Foreground = _currentMode == ClockMode.Hours
                ? primaryBrush
                : textSecondaryBrush;
        }

        if (MinuteHeaderTextBlock != null)
        {
            MinuteHeaderTextBlock.Text = $"{time.Minutes:D2}";
            MinuteHeaderTextBlock.Foreground = _currentMode == ClockMode.Minutes
                ? primaryBrush
                : textSecondaryBrush;
        }

        // Render clock canvas
        RenderClockDial(time);
    }

    private IBrush GetThemeBrush(string key, IBrush fallback)
    {
        if (this.TryFindResource(key, out var resource) && resource is IBrush brush)
        {
            return brush;
        }
        if (Application.Current?.TryFindResource(key, out var appRes) == true && appRes is IBrush appBrush)
        {
            return appBrush;
        }
        return fallback;
    }

    private void RenderClockDial(TimeSpan time)
    {
        if (ClockCanvas == null) return;

        ClockCanvas.Children.Clear();

        double cx = 100;
        double cy = 100;
        var primaryBrush = GetThemeBrush("AppPrimaryBrush", Brushes.Teal);
        var textPrimaryBrush = GetThemeBrush("AppTextPrimaryBrush", Brushes.White);

        double rHand;
        double handAngleDeg;
        int activeHour = time.Hours;
        int activeMinute = time.Minutes;

        if (_currentMode == ClockMode.Hours)
        {
            bool isOuter = activeHour == 0 || activeHour >= 13;
            rHand = isOuter ? 75 : 48;
            int idx = isOuter ? (activeHour == 0 ? 0 : activeHour - 12) : (activeHour == 12 ? 0 : activeHour);
            handAngleDeg = idx * 30.0 - 90.0;
        }
        else
        {
            rHand = 75;
            handAngleDeg = activeMinute * 6.0 - 90.0;
        }

        double radHand = handAngleDeg * Math.PI / 180.0;
        double hx = cx + rHand * Math.Cos(radHand);
        double hy = cy + rHand * Math.Sin(radHand);

        // 1. Draw hand line
        var line = new Line
        {
            StartPoint = new Point(cx, cy),
            EndPoint = new Point(hx, hy),
            Stroke = primaryBrush,
            StrokeThickness = 2
        };
        ClockCanvas.Children.Add(line);

        // 2. Draw center dot
        var centerDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = primaryBrush
        };
        Canvas.SetLeft(centerDot, cx - 3);
        Canvas.SetTop(centerDot, cy - 3);
        ClockCanvas.Children.Add(centerDot);

        // 3. Draw selection bubble circle
        double bubbleRadius = 14;
        var bubble = new Ellipse
        {
            Width = bubbleRadius * 2,
            Height = bubbleRadius * 2,
            Fill = primaryBrush
        };
        Canvas.SetLeft(bubble, hx - bubbleRadius);
        Canvas.SetTop(bubble, hy - bubbleRadius);
        ClockCanvas.Children.Add(bubble);

        // 4. Render numbers
        if (_currentMode == ClockMode.Hours)
        {
            // Outer ring (00, 13..23)
            for (int i = 0; i < 12; i++)
            {
                int val = i == 0 ? 0 : i + 12;
                double angle = i * 30.0 - 90.0;
                double rad = angle * Math.PI / 180.0;
                double x = cx + 75 * Math.Cos(rad);
                double y = cy + 75 * Math.Sin(rad);
                bool isSelected = (val == activeHour);

                AddDialLabel(val.ToString("D2"), x, y, isSelected, textPrimaryBrush);
            }

            // Inner ring (12, 1..11)
            for (int i = 0; i < 12; i++)
            {
                int val = i == 0 ? 12 : i;
                double angle = i * 30.0 - 90.0;
                double rad = angle * Math.PI / 180.0;
                double x = cx + 48 * Math.Cos(rad);
                double y = cy + 48 * Math.Sin(rad);
                bool isSelected = (val == activeHour);

                AddDialLabel(val.ToString(), x, y, isSelected, textPrimaryBrush);
            }
        }
        else
        {
            // Minutes ring (00, 05, 10..55)
            for (int i = 0; i < 12; i++)
            {
                int val = i * 5;
                double angle = i * 30.0 - 90.0;
                double rad = angle * Math.PI / 180.0;
                double x = cx + 75 * Math.Cos(rad);
                double y = cy + 75 * Math.Sin(rad);
                bool isSelected = (val == activeMinute);

                AddDialLabel(val.ToString("D2"), x, y, isSelected, textPrimaryBrush);
            }

            // If minute is not multiple of 5 (e.g. 14), draw extra small indicator dot at (hx, hy)
            if (activeMinute % 5 != 0)
            {
                var smallIndicator = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = Brushes.White
                };
                Canvas.SetLeft(smallIndicator, hx - 2);
                Canvas.SetTop(smallIndicator, hy - 2);
                ClockCanvas.Children.Add(smallIndicator);
            }
        }
    }

    private void AddDialLabel(string text, double x, double y, bool isSelected, IBrush textPrimaryBrush)
    {
        double boxSize = 28;
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = isSelected ? FontWeight.Bold : FontWeight.Normal,
            Foreground = isSelected ? Brushes.White : textPrimaryBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var container = new Border
        {
            Width = boxSize,
            Height = boxSize,
            Child = tb
        };

        Canvas.SetLeft(container, x - boxSize / 2.0);
        Canvas.SetTop(container, y - boxSize / 2.0);
        ClockCanvas.Children.Add(container);
    }

    private void OnClockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDragging = true;
        e.Pointer.Capture(ClockContainer);
        ProcessPointerPosition(e.GetPosition(ClockContainer));
    }

    private void OnClockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            ProcessPointerPosition(e.GetPosition(ClockContainer));
        }
    }

    private void OnClockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            ProcessPointerPosition(e.GetPosition(ClockContainer));

            // Auto-advance from Hours mode to Minutes mode on release
            if (_currentMode == ClockMode.Hours)
            {
                _currentMode = ClockMode.Minutes;
                UpdateUI();
            }
        }
    }

    private void ProcessPointerPosition(Point pt)
    {
        double cx = 100;
        double cy = 100;
        double dx = pt.X - cx;
        double dy = pt.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        double rad = Math.Atan2(dy, dx);
        double deg = (rad * 180.0 / Math.PI + 90.0 + 360.0) % 360.0;

        var currentTime = SelectedTime ?? new TimeSpan(12, 0, 0);

        if (_currentMode == ClockMode.Hours)
        {
            int idx = (int)Math.Round(deg / 30.0) % 12;
            int newHour;

            if (dist < 64)
            {
                // Inner ring (12, 1..11)
                newHour = idx == 0 ? 12 : idx;
            }
            else
            {
                // Outer ring (00, 13..23)
                newHour = idx == 0 ? 0 : idx + 12;
            }

            SelectedTime = new TimeSpan(newHour, currentTime.Minutes, 0);
        }
        else
        {
            int newMinute = (int)Math.Round(deg / 6.0) % 60;
            SelectedTime = new TimeSpan(currentTime.Hours, newMinute, 0);
        }
    }
}
