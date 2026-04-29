using ffnotev2.Dialogs;
using ffnotev2.Models;
using ffnotev2.Services;
using ffnotev2.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ffnotev2;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => App.MainVM;
    private readonly HotkeyService _hotkey = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey.Initialize(this);

        // Ctrl+Shift+N: 메인 창 표시
        _hotkey.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4E, () =>
            ((App)Application.Current).ShowMain());

        // Ctrl+Shift+M: 오버레이 토글
        _hotkey.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4D, () =>
            ((App)Application.Current).ToggleOverlay());
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandlePaste();
            e.Handled = true;
        }
    }

    private void HandlePaste()
    {
        if (ViewModel.CurrentNotebook == null) return;

        var mousePos = Mouse.GetPosition(NoteItemsControl);

        if (Clipboard.ContainsImage())
        {
            var bitmap = Clipboard.GetImage();
            if (bitmap != null)
                ViewModel.AddImageNote(bitmap, mousePos.X, mousePos.Y);
        }
        else if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
                ViewModel.AddLinkNote(text, mousePos.X, mousePos.Y);
            else
                ViewModel.AddTextNote(text, mousePos.X, mousePos.Y);
        }
    }

    private void NotebookMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not NoteBook nb) return;

        var menu = new ContextMenu();

        var renameItem = new MenuItem { Header = "이름 변경" };
        renameItem.Click += (_, _) => RenameNotebook(nb);
        menu.Items.Add(renameItem);

        var linkItem = new MenuItem { Header = "게임 프로세스 연동" };
        if (!string.IsNullOrEmpty(nb.ProcessName))
            linkItem.Header = $"게임 연동: {nb.ProcessName} (변경)";
        linkItem.Click += (_, _) => LinkGameProcess(nb);
        menu.Items.Add(linkItem);

        if (!string.IsNullOrEmpty(nb.ProcessName))
        {
            var unlinkItem = new MenuItem { Header = "게임 연동 해제" };
            unlinkItem.Click += (_, _) =>
            {
                ViewModel.SetNotebookProcess(nb, string.Empty);
            };
            menu.Items.Add(unlinkItem);
        }

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "삭제", Foreground = System.Windows.Media.Brushes.IndianRed };
        deleteItem.Click += (_, _) =>
        {
            if (MessageBox.Show($"'{nb.Name}' 노트북을 삭제하시겠습니까?\n모든 노트가 삭제됩니다.",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ViewModel.DeleteNotebook(nb);
        };
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
    }

    private void RenameNotebook(NoteBook nb)
    {
        var dialog = new RenameDialog(nb.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            ViewModel.RenameNotebook(nb, dialog.NewName);
    }

    private void LinkGameProcess(NoteBook nb)
    {
        var dialog = new GamePickerDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedProcessName != null)
            ViewModel.SetNotebookProcess(nb, dialog.SelectedProcessName);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey.Dispose();
        base.OnClosed(e);
    }
}
