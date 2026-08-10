using System.Windows;
using Altechap.Models;

using WpfMsgBox = System.Windows.MessageBox;

namespace Altechap.Views;

public partial class WindowPickerDialog : Window
{
    public DofusWindow? Chosen { get; private set; }

    public WindowPickerDialog(List<DofusWindow> windows)
    {
        InitializeComponent();
        Lst.ItemsSource = windows;
        if (windows.Count > 0) Lst.SelectedIndex = 0;
    }

    private void OK_Click(object s, RoutedEventArgs e)
    {
        if (Lst.SelectedItem is not DofusWindow w)
        {
            WpfMsgBox.Show("Veuillez sélectionner une fenêtre.", "Aucune sélection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Chosen = w;
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => Close();
}
