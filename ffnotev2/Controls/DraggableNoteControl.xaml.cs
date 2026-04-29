using ffnotev2.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace ffnotev2.Controls;

public partial class DraggableNoteControl : UserControl
{
    private Canvas? _parentCanvas;
    private bool _isDragging;
    private Point _dragOffset;

    public DraggableNoteControl()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _parentCanvas = FindParent<Canvas>(this);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_parentCanvas == null) return;
        _isDragging = true;
        var mousePos = e.GetPosition(_parentCanvas);
        var item = (NoteItem)DataContext;
        _dragOffset = new Point(mousePos.X - item.X, mousePos.Y - item.Y);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null) return;
        var mousePos = e.GetPosition(_parentCanvas);
        var item = (NoteItem)DataContext;
        item.X = Math.Max(0, mousePos.X - _dragOffset.X);
        item.Y = Math.Max(0, mousePos.Y - _dragOffset.Y);
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        if (DataContext is NoteItem item)
            App.MainVM.UpdateNotePosition(item);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is NoteItem item)
            App.MainVM.DeleteNoteItem(item);
    }

    private void TextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        TextBox.IsReadOnly = false;
        TextBox.Focus();
        e.Handled = true;
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        TextBox.IsReadOnly = true;
        if (DataContext is NoteItem item)
            App.MainVM.UpdateNoteContent(item);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T result) return result;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
