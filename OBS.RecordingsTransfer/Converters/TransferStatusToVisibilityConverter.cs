using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Converters;

public class TransferStatusToVisibilityConverter : IValueConverter
{
    public TransferActionStatus Status { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TransferActionStatus status && status == Status
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
