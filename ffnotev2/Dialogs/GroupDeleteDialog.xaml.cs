using System.Windows;

namespace ffnotev2.Dialogs;

public enum GroupDeleteChoice
{
    Cancel,
    DeleteAll,
    GroupOnly
}

public partial class GroupDeleteDialog : Window
{
    public GroupDeleteChoice Choice { get; private set; } = GroupDeleteChoice.Cancel;

    public GroupDeleteDialog(int memberCount)
    {
        InitializeComponent();
        MessageText.Text = $"포함된 노트 {memberCount}건도 삭제하시겠습니까?";
    }

    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        Choice = GroupDeleteChoice.DeleteAll;
        DialogResult = true;
        Close();
    }

    private void DeleteGroupOnly_Click(object sender, RoutedEventArgs e)
    {
        Choice = GroupDeleteChoice.GroupOnly;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = GroupDeleteChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
