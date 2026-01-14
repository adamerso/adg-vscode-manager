using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AdgVscodeManager.Views;

/// <summary>
/// Dialog for initial setup - channel selection and download
/// </summary>
public partial class SetupDialog : Window
{
    private VscodeChannel _selectedChannel = VscodeChannel.Release;
    
    public VscodeChannel SelectedChannel => _selectedChannel;
    public bool ShouldDownload { get; private set; } = false;
    
    private static readonly SolidColorBrush SelectedBorder = new(Color.FromRgb(0, 122, 204));
    private static readonly SolidColorBrush UnselectedBorder = new(Color.FromRgb(69, 69, 69));
    private static readonly SolidColorBrush InsiderSelectedBorder = new(Color.FromRgb(46, 160, 67));
    
    public SetupDialog()
    {
        InitializeComponent();
        Title = $"{Constants.AppName} v{Constants.AppVersion} - Setup";
        UpdateSelectionVisual();
    }
    
    private void Release_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedChannel = VscodeChannel.Release;
        UpdateSelectionVisual();
    }
    
    private void Insider_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedChannel = VscodeChannel.Insiders;
        UpdateSelectionVisual();
    }
    
    private void UpdateSelectionVisual()
    {
        if (_selectedChannel == VscodeChannel.Release)
        {
            ReleaseBorder.BorderBrush = SelectedBorder;
            InsiderBorder.BorderBrush = UnselectedBorder;
        }
        else
        {
            ReleaseBorder.BorderBrush = UnselectedBorder;
            InsiderBorder.BorderBrush = InsiderSelectedBorder;
        }
    }
    
    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldDownload = true;
        DialogResult = true;
        Close();
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldDownload = false;
        DialogResult = false;
        Close();
    }
}
