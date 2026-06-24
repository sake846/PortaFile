namespace PortaFile.Services;

public interface IUserDialogService
{
    void ShowInformation(string message, string title);
    void ShowWarning(string message, string title);
    void ShowError(string message, string title);
    Task<bool> ConfirmWarningAsync(string message, string title);
    string[] SelectFiles(string title, string? initialDirectory);
    void OpenFolder(string path);
}
