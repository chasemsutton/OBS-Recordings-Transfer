using System.Windows;

namespace ODBC.RecordingsTransfer.Views;

public partial class ConfirmDestinationWindow : Window
{
    public bool DontAskAgain { get; private set; }

    public ConfirmDestinationWindow(string destinationPath, int folderYear, int currentYear)
    {
        InitializeComponent();

        MessageText.Text =
            $"The destination path ends with \"{folderYear}\", but the current year is {currentYear}.\n\n" +
            $"Destination:\n{destinationPath}\n\nAre you sure you want to continue?";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        DialogResult = false;
        Close();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        DontAskAgain = DontAskAgainCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
