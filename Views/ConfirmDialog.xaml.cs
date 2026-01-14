using System.Windows;

namespace AdgVscodeManager.Views;

/// <summary>
/// Generic confirmation dialog with customizable buttons
/// </summary>
public partial class ConfirmDialog : Window
{
    public bool PrimaryClicked { get; private set; }
    
    public ConfirmDialog()
    {
        InitializeComponent();
    }
    
    public static ConfirmDialog Create(
        string title, 
        string message, 
        string primaryButton = "Confirm", 
        string secondaryButton = "Cancel")
    {
        var dialog = new ConfirmDialog();
        dialog.TitleText.Text = title;
        dialog.Title = $"{Constants.AppName} v{Constants.AppVersion} - {title}";
        dialog.MessageText.Text = message;
        dialog.PrimaryButton.Content = primaryButton;
        dialog.SecondaryButton.Content = secondaryButton;
        return dialog;
    }
    
    /// <summary>
    /// Create a dialog for VSCode running warning
    /// </summary>
    public static ConfirmDialog CreateVscodeRunningDialog()
    {
        return Create(
            "VSCode is Running",
            "VSCode is currently running. You need to close all VSCode windows before proceeding with this operation.",
            "Kill Windows and Proceed",
            "Cancel"
        );
    }
    
    /// <summary>
    /// Create a restore confirmation dialog
    /// </summary>
    public static ConfirmDialog CreateRestoreConfirmDialog(string currentVersion, string backupVersion)
    {
        return Create(
            "Confirm Restore",
            $"Are you sure you want to restore to the previous version?\n\n" +
            $"Current version: {currentVersion}\n" +
            $"Restore to: {backupVersion}\n\n" +
            $"This will replace your current VSCode installation.",
            "Restore",
            "Cancel"
        );
    }
    
    /// <summary>
    /// Create second restore confirmation dialog
    /// </summary>
    public static ConfirmDialog CreateRestoreConfirmDialog2(string backupVersion)
    {
        return Create(
            "Final Confirmation",
            $"This is the final confirmation.\n\n" +
            $"You are about to restore VSCode to version {backupVersion}.\n\n" +
            $"Your settings and extensions will be preserved.",
            "Yes, Restore Now",
            "Cancel"
        );
    }
    
    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        PrimaryClicked = true;
        DialogResult = true;
        Close();
    }
    
    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        PrimaryClicked = false;
        DialogResult = false;
        Close();
    }
}
