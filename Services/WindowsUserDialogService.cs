using System.Diagnostics;
using System.Windows;

namespace PortaFile.Services;

public sealed class WindowsUserDialogService : IUserDialogService
{
    private readonly Window _owner;

    public WindowsUserDialogService(Window owner)
    {
        _owner = owner;
    }

    public void ShowInformation(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title) =>
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public Task<bool> ConfirmWarningAsync(string message, string title)
    {
        var result = MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public string[] SelectFiles(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Multiselect = true
        };

        return dialog.ShowDialog(_owner) == true ? dialog.FileNames : [];
    }

    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
