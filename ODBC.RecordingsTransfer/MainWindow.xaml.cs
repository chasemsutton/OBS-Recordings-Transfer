using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ODBC.RecordingsTransfer.ViewModels;

namespace ODBC.RecordingsTransfer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.RequestClose += () => Dispatcher.Invoke(Close);
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
                e.Cancel = true;
        }
    }

    private void StatusText_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CancelAutoCloseCommand.Execute(null);
    }
}
