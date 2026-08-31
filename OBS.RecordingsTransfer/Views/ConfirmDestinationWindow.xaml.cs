using System.Windows;
using OBS.RecordingsTransfer.Models;

namespace OBS.RecordingsTransfer.Views;

public partial class ConfirmDestinationWindow : Window
{
    public DestinationConfirmResult Result { get; private set; } = DestinationConfirmResult.Cancel;
    public bool DontAskAgain { get; private set; }

    public ConfirmDestinationWindow(string destinationPath, int folderYear, int currentYear)
    {
        InitializeComponent();

        MessageText.Text =
            $"The destination path ends with \"{folderYear}\", but the current year is {currentYear}.\n\n" +
            $"Destination:\n{destinationPath}\n\n" +
            "Choose Yes to continue with this path, or Update Year to change only the year at the end of the path.";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        Result = DestinationConfirmResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        Result = DestinationConfirmResult.Continue;
        DialogResult = true;
        Close();
    }

    private void UpdateYearButton_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        Result = DestinationConfirmResult.UpdateYear;
        DialogResult = true;
        Close();
    }
}
