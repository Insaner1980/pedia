using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Pedia.Controls;

public sealed partial class PaneSplitter : UserControl
{
    private bool _dragging;
    private uint _pointerId;
    private double _startX;
    private double _startWidth;

    public PaneSplitter()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }

    public Grid? TargetGrid
    {
        get => (Grid?)GetValue(TargetGridProperty);
        set => SetValue(TargetGridProperty, value);
    }

    public static readonly DependencyProperty TargetGridProperty = DependencyProperty.Register(
        nameof(TargetGrid), typeof(Grid), typeof(PaneSplitter), new PropertyMetadata(null));

    public int TargetColumn
    {
        get => (int)GetValue(TargetColumnProperty);
        set => SetValue(TargetColumnProperty, value);
    }

    public static readonly DependencyProperty TargetColumnProperty = DependencyProperty.Register(
        nameof(TargetColumn), typeof(int), typeof(PaneSplitter), new PropertyMetadata(0));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(PaneSplitter), new PropertyMetadata(220d));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(PaneSplitter), new PropertyMetadata(760d));

    public double AdjacentMinimum
    {
        get => (double)GetValue(AdjacentMinimumProperty);
        set => SetValue(AdjacentMinimumProperty, value);
    }

    public static readonly DependencyProperty AdjacentMinimumProperty = DependencyProperty.Register(
        nameof(AdjacentMinimum), typeof(double), typeof(PaneSplitter), new PropertyMetadata(450d));

    public event EventHandler<double>? WidthChanged;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        Line.Background = (Brush)Application.Current.Resources["PediaFocusBrush"];

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            Line.Background = (Brush)Application.Current.Resources["PediaSubtleDividerBrush"];
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (TargetGrid is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(TargetGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _pointerId = point.PointerId;
        _startX = point.Position.X;
        _startWidth = TargetGrid.ColumnDefinitions[TargetColumn].ActualWidth;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || TargetGrid is null || e.Pointer.PointerId != _pointerId)
        {
            return;
        }

        var x = e.GetCurrentPoint(TargetGrid).Position.X;
        SetWidth(_startWidth + x - _startX);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging && e.Pointer.PointerId == _pointerId)
        {
            ReleasePointerCapture(e.Pointer);
            StopDragging();
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => StopDragging();

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var delta = e.Key switch
        {
            VirtualKey.Left => -16,
            VirtualKey.Right => 16,
            _ => 0
        };
        if (delta == 0 || TargetGrid is null)
        {
            return;
        }

        SetWidth(TargetGrid.ColumnDefinitions[TargetColumn].ActualWidth + delta);
        e.Handled = true;
    }

    private void SetWidth(double requestedWidth)
    {
        if (TargetGrid is null)
        {
            return;
        }

        var splitterWidth = TargetGrid.ColumnDefinitions
            .Where((_, index) => index != TargetColumn && index != TargetGrid.ColumnDefinitions.Count - 1)
            .Where(column => column.Width.IsAbsolute && column.ActualWidth <= 16)
            .Sum(column => column.ActualWidth);
        var layoutMaximum = Math.Max(Minimum, TargetGrid.ActualWidth - splitterWidth - AdjacentMinimum);
        var width = Math.Clamp(requestedWidth, Minimum, Math.Min(Maximum, layoutMaximum));
        TargetGrid.ColumnDefinitions[TargetColumn].Width = new GridLength(width);
        WidthChanged?.Invoke(this, width);
    }

    private void StopDragging()
    {
        _dragging = false;
        Line.Background = (Brush)Application.Current.Resources["PediaSubtleDividerBrush"];
    }
}
