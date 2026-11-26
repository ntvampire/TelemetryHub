using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace KsitalTelemetryHub.UI.Avalonia.Views;

public partial class ConfirmDialogWindow : Window
{
    public bool IsConfirmed { get; private set; }

    public ConfirmDialogWindow() : this("Подтверждение", "Вы уверены?")
    {
    }

    public ConfirmDialogWindow(string title, string message, string confirmText = "Подтвердить", bool isDangerous = false)
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        BtnConfirm.Content = confirmText;

        if (isDangerous)
        {
            BtnConfirm.Background = new SolidColorBrush(Color.Parse("#B71C1C"));
        }
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText = "Подтвердить", bool isDangerous = false)
    {
        var dlg = new ConfirmDialogWindow(title, message, confirmText, isDangerous);
        await dlg.ShowDialog(owner);
        return dlg.IsConfirmed;
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
