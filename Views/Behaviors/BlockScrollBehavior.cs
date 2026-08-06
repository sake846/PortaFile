using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PortaFile.Views.Behaviors;

public static class BlockScrollBehavior
{
    public static readonly DependencyProperty ActiveBlockIndexProperty =
        DependencyProperty.RegisterAttached(
            "ActiveBlockIndex",
            typeof(int),
            typeof(BlockScrollBehavior),
            new PropertyMetadata(-1, OnActiveBlockIndexChanged));

    public static int GetActiveBlockIndex(DependencyObject obj) =>
        (int)obj.GetValue(ActiveBlockIndexProperty);

    public static void SetActiveBlockIndex(DependencyObject obj, int value) =>
        obj.SetValue(ActiveBlockIndexProperty, value);

    private static void OnActiveBlockIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var newIndex = (int)e.NewValue;
        if (newIndex < 0)
        {
            return;
        }

        if (d is ScrollViewer scrollViewer)
        {
            var itemsControl = FindChild<ItemsControl>(scrollViewer);
            if (itemsControl is not null)
            {
                ScrollToItem(scrollViewer, itemsControl, newIndex);
            }
            else
            {
                scrollViewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    var childControl = FindChild<ItemsControl>(scrollViewer);
                    if (childControl is not null)
                    {
                        ScrollToItem(scrollViewer, childControl, newIndex);
                    }
                }));
            }
        }
        else if (d is ItemsControl itemsControl)
        {
            var parentScrollViewer = FindParent<ScrollViewer>(itemsControl);
            if (parentScrollViewer is not null)
            {
                ScrollToItem(parentScrollViewer, itemsControl, newIndex);
            }
        }
    }

    private static void ScrollToItem(ScrollViewer scrollViewer, ItemsControl itemsControl, int index)
    {
        if (index < 0 || itemsControl.Items.Count <= index)
        {
            return;
        }

        void BringChildIntoView()
        {
            if (index < 0 || itemsControl.Items.Count <= index)
            {
                return;
            }

            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                container.BringIntoView();
            }
            else
            {
                itemsControl.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (itemsControl.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement lateContainer)
                    {
                        lateContainer.BringIntoView();
                    }
                }));
            }
        }

        scrollViewer.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(BringChildIntoView));
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var subChild = FindChild<T>(child);
            if (subChild is not null)
            {
                return subChild;
            }
        }

        return null;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null && parent is not T)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        return parent as T;
    }
}
