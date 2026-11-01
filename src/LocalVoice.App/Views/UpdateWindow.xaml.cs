using System.ComponentModel;
using System.Windows;
using LocalVoice.App.Services;

namespace LocalVoice.App.Views;

public partial class UpdateWindow : Window
{
    private bool _allowClose;
    public UpdateWindow()
    {
        InitializeComponent();
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => UiMotion.Enter(Root, 10);
    public void CloseFromPrompt()
    {
        _allowClose = true;
        Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (DataContext is not UpdateService service) return;
        if (service.IsDownloading)
        {
            e.Cancel = true;
            service.StatusText = "Идёт загрузка обновления";
            service.DetailText = "Дождитесь окончания или нажмите «Отменить загрузку», затем закройте окно.";
            return;
        }
        // The system close button is a safe "later", never an accidental skip or install.
        e.Cancel = true;
        if (service.PostponeUpdateCommand.CanExecute(null)) service.PostponeUpdateCommand.Execute(null);
    }
}
