using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Wadd.UI.Helpers;

/// <summary>
/// Attached property and controller that adds mouse drag-to-scroll functionality
/// to any Avalonia ScrollViewer (supporting both horizontal and vertical scrolling with inertia).
/// </summary>
public static class MouseDragScrollHelper
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsEnabled",
            typeof(MouseDragScrollHelper),
            defaultValue: false);

    public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly ConditionalWeakTable<ScrollViewer, MouseDragScrollController> Controllers = new();

    static MouseDragScrollHelper()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (!Controllers.TryGetValue(scrollViewer, out _))
            {
                var controller = new MouseDragScrollController(scrollViewer);
                Controllers.Add(scrollViewer, controller);
            }
        }
        else
        {
            if (Controllers.TryGetValue(scrollViewer, out var controller))
            {
                controller.Dispose();
                Controllers.Remove(scrollViewer);
            }
        }
    }
}

/// <summary>
/// Manages mouse drag interaction, pointer capture, and kinetic momentum scrolling for a single ScrollViewer.
/// </summary>
internal sealed class MouseDragScrollController : IDisposable
{
    private readonly ScrollViewer _scrollViewer;

    private Point _startPointerPosition;
    private Vector _startScrollOffset;
    private bool _isTracking;
    private bool _isDragging;
    private IPointer? _capturedPointer;

    private Point _lastMovePosition;
    private DateTime _lastMoveTime;
    private Vector _velocity;
    private DispatcherTimer? _inertiaTimer;

    private const double DragThreshold = 4.0;
    private const double DragThresholdSquared = DragThreshold * DragThreshold;

    public MouseDragScrollController(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;

        _scrollViewer.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _scrollViewer.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _scrollViewer.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _scrollViewer.AddHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);

        _scrollViewer.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only mouse and pen devices (touch has built-in gesture scrolling)
        if (e.Pointer.Type != PointerType.Mouse && e.Pointer.Type != PointerType.Pen)
        {
            return;
        }

        var point = e.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        StopInertia();

        var sourceVisual = e.Source as Visual;
        if (ShouldIgnoreControl(sourceVisual))
        {
            return;
        }

        // If there's an inner active scrollable ScrollViewer closer to the source, let it handle the drag
        if (HasInnerScrollViewer(sourceVisual))
        {
            return;
        }

        _isTracking = true;
        _isDragging = false;
        _startPointerPosition = e.GetPosition(_scrollViewer);
        _startScrollOffset = _scrollViewer.Offset;
        _lastMovePosition = _startPointerPosition;
        _lastMoveTime = DateTime.UtcNow;
        _velocity = Vector.Zero;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isTracking) return;

        // If the inner ScrollViewer already captured and handled the move, do not intercept
        if (e.Handled && !_isDragging) return;

        var point = e.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            CancelDrag();
            return;
        }

        var currentPoint = e.GetPosition(_scrollViewer);
        var delta = currentPoint - _startPointerPosition;

        bool canScrollH = _scrollViewer.Extent.Width > _scrollViewer.Viewport.Width;
        bool canScrollV = _scrollViewer.Extent.Height > _scrollViewer.Viewport.Height;

        if (!canScrollH && !canScrollV)
        {
            return;
        }

        if (!_isDragging)
        {
            double distSq = delta.X * delta.X + delta.Y * delta.Y;
            if (distSq < DragThresholdSquared)
            {
                return;
            }

            _isDragging = true;
            _capturedPointer = e.Pointer;
            e.Pointer.Capture(_scrollViewer);
            _scrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        if (_isDragging)
        {
            double maxOffsetX = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
            double maxOffsetY = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

            double targetX = _scrollViewer.Offset.X;
            double targetY = _scrollViewer.Offset.Y;

            if (canScrollH)
            {
                targetX = Math.Clamp(_startScrollOffset.X - delta.X, 0, maxOffsetX);
            }

            if (canScrollV)
            {
                targetY = Math.Clamp(_startScrollOffset.Y - delta.Y, 0, maxOffsetY);
            }

            _scrollViewer.Offset = new Vector(targetX, targetY);

            var now = DateTime.UtcNow;
            var dt = (now - _lastMoveTime).TotalSeconds;
            if (dt > 0.005)
            {
                var stepDelta = currentPoint - _lastMovePosition;
                var instantVelocity = new Vector(stepDelta.X / dt, stepDelta.Y / dt);
                _velocity = _velocity * 0.25 + instantVelocity * 0.75;
                _lastMovePosition = currentPoint;
                _lastMoveTime = now;
            }

            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isTracking) return;

        bool wasDragging = _isDragging;
        _isTracking = false;
        _isDragging = false;
        _scrollViewer.Cursor = null;

        if (_capturedPointer != null)
        {
            _capturedPointer.Capture(null);
            _capturedPointer = null;
        }

        if (wasDragging)
        {
            e.Handled = true;

            // If stationary before release, decay velocity
            if ((DateTime.UtcNow - _lastMoveTime).TotalMilliseconds > 120)
            {
                _velocity = Vector.Zero;
            }

            StartInertia();
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CancelDrag();
    }

    private void CancelDrag()
    {
        _isTracking = false;
        _isDragging = false;
        _scrollViewer.Cursor = null;

        if (_capturedPointer != null)
        {
            _capturedPointer.Capture(null);
            _capturedPointer = null;
        }

        StopInertia();
    }

    private void StartInertia()
    {
        const double maxVelocity = 2500.0;
        double vx = Math.Clamp(_velocity.X, -maxVelocity, maxVelocity);
        double vy = Math.Clamp(_velocity.Y, -maxVelocity, maxVelocity);
        _velocity = new Vector(vx, vy);

        if (_velocity.Length < 40)
        {
            _velocity = Vector.Zero;
            return;
        }

        _inertiaTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _inertiaTimer.Tick += (s, e) =>
        {
            const double dt = 0.016;
            const double friction = 0.92;

            bool canScrollH = _scrollViewer.Extent.Width > _scrollViewer.Viewport.Width;
            bool canScrollV = _scrollViewer.Extent.Height > _scrollViewer.Viewport.Height;

            double maxOffsetX = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
            double maxOffsetY = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

            double currentX = _scrollViewer.Offset.X;
            double currentY = _scrollViewer.Offset.Y;

            double nextX = canScrollH ? Math.Clamp(currentX - (_velocity.X * dt), 0, maxOffsetX) : currentX;
            double nextY = canScrollV ? Math.Clamp(currentY - (_velocity.Y * dt), 0, maxOffsetY) : currentY;

            _scrollViewer.Offset = new Vector(nextX, nextY);
            _velocity *= friction;

            bool hitHBoundary = !canScrollH || nextX <= 0 || nextX >= maxOffsetX;
            bool hitVBoundary = !canScrollV || nextY <= 0 || nextY >= maxOffsetY;

            if (_velocity.Length < 15 || (hitHBoundary && hitVBoundary))
            {
                StopInertia();
            }
        };

        _inertiaTimer.Start();
    }

    private void StopInertia()
    {
        if (_inertiaTimer != null)
        {
            _inertiaTimer.Stop();
            _inertiaTimer = null;
        }
        _velocity = Vector.Zero;
    }

    private bool ShouldIgnoreControl(Visual? source)
    {
        for (var current = source; current != null && current != _scrollViewer; current = current.GetVisualParent())
        {
            if (current is ScrollBar or TextBox or AutoCompleteBox or Slider or GridSplitter)
            {
                return true;
            }
            if (current.GetType().Name == "ClockTimePicker")
            {
                return true;
            }
        }
        return false;
    }

    private bool HasInnerScrollViewer(Visual? source)
    {
        for (var current = source; current != null && current != _scrollViewer; current = current.GetVisualParent())
        {
            if (current is ScrollViewer sv && MouseDragScrollHelper.GetIsEnabled(sv))
            {
                if (sv.Extent.Width > sv.Viewport.Width || sv.Extent.Height > sv.Viewport.Height)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        CancelDrag();
        _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _scrollViewer.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _scrollViewer.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _scrollViewer.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
        _scrollViewer.DetachedFromVisualTree -= OnDetachedFromVisualTree;
    }
}
