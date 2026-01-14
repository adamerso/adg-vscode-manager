using System.Windows;

namespace AdgVscodeManager.Views;

/// <summary>
/// Dialog showing installation/upgrade progress
/// </summary>
public partial class InstallProgressDialog : Window
{
    public InstallProgressDialog()
    {
        InitializeComponent();
    }
    
    public void SetVersionInfo(string fromVersion, string toVersion, int releaseCount, int commitCount, string changelog, bool isInsiders = false)
    {
        FromVersionText.Text = fromVersion;
        ToVersionText.Text = toVersion;
        
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
    
    public void SetStatus(string status)
    {
        Dispatcher.Invoke(() => StatusText.Text = status);
    }
    
    public void SetProgress(int percentage)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = percentage;
        });
    }
    
    public void Complete(string message = "Upgrade complete!")
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            FooterText.Text = "";
            OkButton.Visibility = System.Windows.Visibility.Visible;
        });
    }
    
    public void ShowError(string error)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Error: {error}";
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            FooterText.Text = "";
            OkButton.Visibility = System.Windows.Visibility.Visible;
        });
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
