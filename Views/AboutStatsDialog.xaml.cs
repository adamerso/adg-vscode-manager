using System.Windows;
using AdgVscodeManager.Services;

namespace AdgVscodeManager.Views;

public partial class AboutStatsDialog : Window
{
    public AboutStatsDialog()
    {
        InitializeComponent();
        LoadData();
    }
    
    private void LoadData()
    {
        // Get config service from App
        var app = (App)Application.Current;
        var config = app.ConfigService.Config;
        
        // Version
        VersionText.Text = Constants.AppVersion;
        
        // Installed updates
        if (config.SuccessfulInstallsCount > 0)
        {
            InstalledCountText.Text = $"{config.SuccessfulInstallsCount} update(s)";
            
            if (!string.IsNullOrEmpty(config.LastSuccessfulInstallUtc) && 
                DateTime.TryParse(config.LastSuccessfulInstallUtc, out var lastTime))
            {
                InstalledLastText.Text = lastTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                InstalledLastText.Text = "unknown";
            }
        }
        else
        {
            InstalledCountText.Text = "never";
            InstalledLastText.Text = "n/a";
        }
        
        // Failed updates
        if (config.FailedInstallsCount > 0)
        {
            FailedCountText.Text = $"{config.FailedInstallsCount} time(s)";
            
            if (!string.IsNullOrEmpty(config.LastFailedInstallUtc) && 
                DateTime.TryParse(config.LastFailedInstallUtc, out var lastTime))
            {
                FailedLastText.Text = lastTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                FailedLastText.Text = "unknown";
            }
            
            FailedReasonText.Text = !string.IsNullOrEmpty(config.LastFailedInstallReason)
                ? config.LastFailedInstallReason
                : "unknown";
        }
        else
        {
            FailedCountText.Text = "never";
            FailedLastText.Text = "n/a";
            FailedReasonText.Text = "n/a";
        }
        
        // Reverted updates
        if (config.RevertsCount > 0)
        {
            RevertsCountText.Text = $"{config.RevertsCount} time(s)";
            
            if (!string.IsNullOrEmpty(config.LastRevertUtc) && 
                DateTime.TryParse(config.LastRevertUtc, out var lastTime))
            {
                RevertsLastText.Text = lastTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                RevertsLastText.Text = "unknown";
            }
        }
        else
        {
            RevertsCountText.Text = "never";
            RevertsLastText.Text = "n/a";
        }
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
