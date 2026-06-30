using System.Windows;
using System.Windows.Input;

namespace PortaFile.Views.Behaviors;

public static class DragDropBehavior
{
    public static readonly DependencyProperty DragOverCommandProperty =
        DependencyProperty.RegisterAttached(
            "DragOverCommand",
            typeof(ICommand),
            typeof(DragDropBehavior),
            new PropertyMetadata(null, OnDragOverCommandChanged));

    public static readonly DependencyProperty DropCommandProperty =
        DependencyProperty.RegisterAttached(
            "DropCommand",
            typeof(ICommand),
            typeof(DragDropBehavior),
            new PropertyMetadata(null, OnDropCommandChanged));

    public static ICommand GetDragOverCommand(DependencyObject obj) => (ICommand)obj.GetValue(DragOverCommandProperty);
    public static void SetDragOverCommand(DependencyObject obj, ICommand value) => obj.SetValue(DragOverCommandProperty, value);

    public static ICommand GetDropCommand(DependencyObject obj) => (ICommand)obj.GetValue(DropCommandProperty);
    public static void SetDropCommand(DependencyObject obj, ICommand value) => obj.SetValue(DropCommandProperty, value);

    private static void OnDragOverCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.DragOver -= Element_DragOver;
            if (e.NewValue is not null)
            {
                element.DragOver += Element_DragOver;
            }
        }
    }

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.Drop -= Element_Drop;
            if (e.NewValue is not null)
            {
                element.Drop += Element_Drop;
            }
        }
    }

    private static void Element_DragOver(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d)
        {
            var command = GetDragOverCommand(d);
            if (command is not null && command.CanExecute(e))
            {
                command.Execute(e);
            }
        }
    }

    private static void Element_Drop(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d)
        {
            var command = GetDropCommand(d);
            if (command is not null && command.CanExecute(e))
            {
                command.Execute(e);
            }
        }
    }
}
