using System;
using System.Windows;
using System.Windows.Media.Imaging;
using ODBC.RecordingsTransfer.ViewModels;

namespace ODBC.RecordingsTransfer;

public partial class MainWindow : Window
{
    private const double ExpandedHeight = 760;
    private const double ExpandedWidth = 1040;
    private const double ExpandedMinHeight = 640;
    private const double ExpandedMinWidth = 900;
    private const double CompactColumnWidth = 440;

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
        if (_isCompact)
            FitCompactWindowToContent();

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

        MinWidth = 1;
        MinHeight = 1;

        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        CompactColumn.Width = new GridLength(CompactColumnWidth);
        SettingsColumn.Width = new GridLength(0);
        MainBodyRow.Height = GridLength.Auto;
        ActivityLogRow.Height = new GridLength(0);

        SizeToContent = SizeToContent.WidthAndHeight;
        _isCompact = true;
        FitCompactWindowToContent();
    }

    private void ApplyExpandedLayout()
    {
        SizeToContent = SizeToContent.Manual;

        CompactColumn.Width = new GridLength(CompactColumnWidth);
        SettingsColumn.Width = new GridLength(1, GridUnitType.Star);
        MainBodyRow.Height = GridLength.Auto;
        ActivityLogRow.Height = new GridLength(1, GridUnitType.Star);

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

    private void FitCompactWindowToContent()
    {
        if (!IsLoaded)
            return;

        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        MinWidth = ActualWidth;
        MinHeight = ActualHeight;
    }
}
