using System.Windows;
using System.Threading;
using PortaFile.Services;
using PortaFile.ViewModels;
using PortaFile.Views;

namespace PortaFile;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\PortaFile.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryAcquireSingleInstanceMutex())
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        var dialogService = new WindowsUserDialogService(mainWindow);
        var viewModel = new MainWindowViewModel(dialogService, action => mainWindow.Dispatcher.Invoke(action));

        mainWindow.DataContext = viewModel;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseSingleInstanceMutex();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstanceMutex()
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
        return false;
    }

    private void ReleaseSingleInstanceMutex()
    {
        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Ignore
            }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}

