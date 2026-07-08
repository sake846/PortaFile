using System;
using System.Windows;
using System.Threading;
using System.IO;
using System.Diagnostics;
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
        // グローバル未処理例外ハンドラ
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // スタートアップ診断ログの書き出し
        WriteStartupDiagnostics();

        if (!TryAcquireSingleInstanceMutex())
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var lastStateService = new LastStateService();
        var lastState = lastStateService.Load();
        var localization = new AppLocalization(lastState);
        var mainWindow = new MainWindow();
        var dialogService = new WindowsUserDialogService(mainWindow);
        var viewModel = new MainWindowViewModel(dialogService, lastStateService, localization, action => mainWindow.Dispatcher.Invoke(action));

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

    private static void WriteStartupDiagnostics()
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "PortaFile");
            Directory.CreateDirectory(tempPath);
            var logPath = Path.Combine(tempPath, "startup.log");

            var process = Process.GetCurrentProcess();
            var lines = new[]
            {
                "==== PortaFile Startup Diagnostics ====",
                $"Time: {DateTime.Now:O}",
                $"ProcessPath: {Environment.ProcessPath ?? "(null)"}",
                $"MainModule: {process.MainModule?.FileName ?? "(null)"}",
                $"BaseDirectory: {AppContext.BaseDirectory}",
                $"CurrentDirectory: {Environment.CurrentDirectory}",
                $"CommandLine: {Environment.CommandLine}",
                $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
                $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
                string.Empty
            };

            File.WriteAllLines(logPath, lines); // 上書き保存
        }
        catch
        {
            // 診断ログ書き込みの失敗は無視する
        }
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            HandleUnhandledException(ex);
        }
    }

    private void HandleUnhandledException(Exception ex)
    {
        try
        {
            var message = $"未処理の例外が発生しました。アプリケーションを終了します。\n\n【エラー内容】\n{ex.Message}\n\n【スタックトレース】\n{ex.StackTrace}";
            MessageBox(nint.Zero, message, "PortaFile - エラー", 0x00000010 | 0x00010000 | 0x00040000); // MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST
        }
        catch
        {
        }
        finally
        {
            Shutdown();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);
}
