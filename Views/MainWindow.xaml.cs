using System.Windows;
using PortaFile.Services;
using PortaFile.ViewModels;

namespace PortaFile.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new WindowsUserDialogService(this),
            action => Dispatcher.Invoke(action));
        DataContext = _viewModel;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _viewModel.CanAcceptFilesForSend()
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            await _viewModel.SendPathsAsync((string[])e.Data.GetData(DataFormats.FileDrop)!);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
