using System.Windows;
using PortaFile.Services;
using PortaFile.ViewModels;
using PortaFile.Views;

namespace PortaFile;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        var dialogService = new WindowsUserDialogService(mainWindow);
        var viewModel = new MainWindowViewModel(dialogService, action => mainWindow.Dispatcher.Invoke(action));

        mainWindow.DataContext = viewModel;
        mainWindow.Show();
    }
}

