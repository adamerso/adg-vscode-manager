using System.Windows;

namespace AdgVscodeManager.Views;

/// <summary>
/// Dialog for asking user about installing an update
/// </summary>
public partial class UpdateDialog : Window
{
    public enum UpdateDialogResult
    {
        UpdateNow,
        UpdateLater,
        SkipVersion
    }
    
    public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.UpdateLater;
    
    public UpdateDialog()
    {
        InitializeComponent();
    }
    
    public void SetVersionInfo(string currentVersion, string newVersion, int releaseCount, int commitCount, string changelog, bool isInsiders = false)
    {
        CurrentVersionText.Text = currentVersion;
        NewVersionText.Text = newVersion;
        
        if (isInsiders)
        {
            // For insiders: show both releases and commits
            var releaseLabel = releaseCount == 1 ? "release" : "releases";
            var commitLabel = commitCount == 1 ? "commit" : "commits";
            UpdateCountText.Text = $"{releaseCount} {releaseLabel}, {commitCount} {commitLabel}";
        }
        else
        {
            // For stable: just show versions
            var countLabel = releaseCount == 1 ? "version" : "versions";
            UpdateCountText.Text = $"{releaseCount} {countLabel}";
        }
        
        ChangelogText.Text = changelog;
    }
    
    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.UpdateNow;
        DialogResult = true;
        Close();
    }
    
    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.UpdateLater;
        DialogResult = false;
        Close();
    }
    
    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdateDialogResult.SkipVersion;
        DialogResult = false;
        Close();
    }
}
