using ffnotev2.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace ffnotev2;

public partial class OverlayWindow : Window
{
    private OverlayViewModel ViewModel => App.OverlayVM;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void QuickInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel.Submit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.QuickNoteText = string.Empty;
            Hide();
            e.Handled = true;
        }
    }

    private void CloseOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        Hide();
    }

    // 창 드래그
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
