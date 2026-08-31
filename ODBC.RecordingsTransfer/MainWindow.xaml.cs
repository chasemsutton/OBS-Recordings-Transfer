using System;
using System.Windows;
using System.Windows.Media.Imaging;
using ODBC.RecordingsTransfer.ViewModels;

namespace ODBC.RecordingsTransfer;

public partial class MainWindow : Window
{
    private const double ExpandedHeight = 760;
    private const double ExpandedWidth = 1040;
    private const double ExpandedMinHeight = 520;
    private const double ExpandedMinWidth = 720;
    // Window min width for compact mode (includes side margins). Column min stays 0 so boxes shrink into the margin inset.
    private const double CompactMinWidth = 360;
    // Title + controls + destination + queue padding/header + 4 items (~120) + equal side/bottom margin buffer.
    private const double CompactMinHeight = 400;

    private readonly MainViewModel _viewModel;
    // Session-only remembered sizes (not persisted). Compact defaults to minimum.
    private double _expandedHeight = ExpandedHeight;
    private double _expandedWidth = ExpandedWidth;
    private double _compactHeight = CompactMinHeight;
    private double _compactWidth = CompactMinWidth;
    private WindowState _expandedWindowState = WindowState.Normal;
    private bool _isCompact;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.RequestClose += () => Dispatcher.Invoke(Close);
        _viewModel.SettingsPanelVisibilityChanged += ApplyLayoutForSettingsPanel;
        ApplyLayoutForSettingsPanel(_viewModel.ShowSettingsPanel);
        if (_viewModel.StartMinimized)
            WindowState = WindowState.Minimized;
    }

    private void TrySetWindowIcon()
    {
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/transfer_icon.ico", UriKind.Absolute));
        }
        catch
        {
            // Icon is optional; keep the app usable if the resource is missing.
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsRunning || _viewModel.IsContinuousMode)
        {
            var result = System.Windows.MessageBox.Show(
                _viewModel.IsContinuousMode
                    ? "Continuous transfer mode is active. Close anyway?"
                    : "A transfer is still running. Close anyway?",
                "ODBC Recordings Transfer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _viewModel.PrepareForClose();
    }

    private void ApplyLayoutForSettingsPanel(bool showSettings)
    {
        if (showSettings)
            ApplyExpandedLayout();
        else
            ApplyCompactLayout();
    }

    private void ApplyCompactLayout()
    {
        if (!_isCompact)
            CaptureExpandedLayout();

        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        SizeToContent = SizeToContent.Manual;

        CompactColumn.Width = new GridLength(1, GridUnitType.Star);
        CompactColumn.MinWidth = 0;
        SettingsColumn.Width = new GridLength(0);
        SettingsColumn.MinWidth = 0;
        MainBodyRow.Height = new GridLength(1, GridUnitType.Star);
        MainBodyRow.MinHeight = 0;
        ActivityLogRow.Height = new GridLength(0);
        ActivityLogRow.MinHeight = 0;

        MinWidth = CompactMinWidth;
        MinHeight = CompactMinHeight;

        Width = Math.Max(_compactWidth, CompactMinWidth);
        Height = Math.Max(_compactHeight, CompactMinHeight);

        _isCompact = true;
    }

    private void ApplyExpandedLayout()
    {
        if (_isCompact)
            CaptureCompactLayout();

        SizeToContent = SizeToContent.Manual;

        CompactColumn.Width = new GridLength(1, GridUnitType.Star);
        CompactColumn.MinWidth = 0;
        SettingsColumn.Width = new GridLength(1.4, GridUnitType.Star);
        SettingsColumn.MinWidth = 280;
        MainBodyRow.Height = new GridLength(1, GridUnitType.Star);
        MainBodyRow.MinHeight = 280;
        ActivityLogRow.Height = new GridLength(1, GridUnitType.Star);
        ActivityLogRow.MinHeight = 120;

        MinWidth = ExpandedMinWidth;
        MinHeight = ExpandedMinHeight;

        Width = Math.Max(_expandedWidth, ExpandedMinWidth);
        Height = Math.Max(_expandedHeight, ExpandedMinHeight);
        WindowState = _expandedWindowState;

        _isCompact = false;
    }

    private void CaptureExpandedLayout()
    {
        _expandedWindowState = WindowState == WindowState.Minimized
            ? WindowState.Normal
            : WindowState;

        if (!TryGetCurrentSize(out var width, out var height))
            return;

        if (width >= ExpandedMinWidth && height >= ExpandedMinHeight)
        {
            _expandedWidth = width;
            _expandedHeight = height;
        }
    }

    private void CaptureCompactLayout()
    {
        if (!TryGetCurrentSize(out var width, out var height))
            return;

        _compactWidth = Math.Max(width, CompactMinWidth);
        _compactHeight = Math.Max(height, CompactMinHeight);
    }

    private bool TryGetCurrentSize(out double width, out double height)
    {
        width = 0;
        height = 0;

        if (!IsLoaded)
            return false;

        if (WindowState == WindowState.Normal)
        {
            width = Width;
            height = Height;
        }
        else
        {
            width = RestoreBounds.Width;
            height = RestoreBounds.Height;
        }

        return width > 0 && height > 0;
    }
}
