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
    private const double CompactWidth = 480;
    private const double CompactHeight = 520;
    private const double CompactMinWidth = 360;
    private const double CompactMinHeight = 420;

    private readonly MainViewModel _viewModel;
    private double _expandedHeight = ExpandedHeight;
    private double _expandedWidth = ExpandedWidth;
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
        if (_viewModel.IsRunning)
        {
            var result = System.Windows.MessageBox.Show(
                "A transfer is still running. Close anyway?",
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
        CompactColumn.MinWidth = CompactMinWidth;
        SettingsColumn.Width = new GridLength(0);
        SettingsColumn.MinWidth = 0;
        MainBodyRow.Height = new GridLength(1, GridUnitType.Star);
        MainBodyRow.MinHeight = 0;
        ActivityLogRow.Height = new GridLength(0);
        ActivityLogRow.MinHeight = 0;

        MinWidth = CompactMinWidth;
        MinHeight = CompactMinHeight;

        if (!_isCompact || Width < CompactMinWidth || Height < CompactMinHeight)
        {
            Width = CompactWidth;
            Height = CompactHeight;
        }

        _isCompact = true;
    }

    private void ApplyExpandedLayout()
    {
        SizeToContent = SizeToContent.Manual;

        CompactColumn.Width = new GridLength(1, GridUnitType.Star);
        CompactColumn.MinWidth = 320;
        SettingsColumn.Width = new GridLength(1.4, GridUnitType.Star);
        SettingsColumn.MinWidth = 360;
        MainBodyRow.Height = new GridLength(1, GridUnitType.Star);
        MainBodyRow.MinHeight = 280;
        ActivityLogRow.Height = new GridLength(1, GridUnitType.Star);
        ActivityLogRow.MinHeight = 120;

        MinWidth = ExpandedMinWidth;
        MinHeight = ExpandedMinHeight;

        if (_isCompact)
        {
            Width = Math.Max(_expandedWidth, ExpandedMinWidth);
            Height = Math.Max(_expandedHeight, ExpandedMinHeight);
            WindowState = _expandedWindowState;
        }

        _isCompact = false;
    }

    private void CaptureExpandedLayout()
    {
        _expandedWindowState = WindowState == WindowState.Minimized
            ? WindowState.Normal
            : WindowState;

        if (IsLoaded)
        {
            var width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
            var height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;

            if (width >= ExpandedMinWidth && height >= ExpandedMinHeight)
            {
                _expandedWidth = width;
                _expandedHeight = height;
                return;
            }
        }

        _expandedWidth = ExpandedWidth;
        _expandedHeight = ExpandedHeight;
    }
}
