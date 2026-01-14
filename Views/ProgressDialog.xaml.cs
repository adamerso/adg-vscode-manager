using System.Windows;

namespace AdgVscodeManager.Views;

/// <summary>
/// Progress dialog for update checking and downloading
/// </summary>
public partial class ProgressDialog : Window
{
    private readonly CancellationTokenSource _cts;
    
    public CancellationToken CancellationToken => _cts.Token;
    public bool WasCancelled { get; private set; }
    
    public ProgressDialog()
    {
        InitializeComponent();
        _cts = new CancellationTokenSource();
    }
    
    public void SetTitle(string title)
    {
        TitleText.Text = title;
        // Don't override window title if already set with version
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
            PercentageText.Text = $"{percentage}%";
        });
    }
    
    public void SetIndeterminate(bool indeterminate = true)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.IsIndeterminate = indeterminate;
            PercentageText.Text = "";
        });
    }
    
    public void Complete(string message = "Complete!")
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            CancelButton.Content = "Close";
        });
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_cts.IsCancellationRequested)
        {
            WasCancelled = true;
            _cts.Cancel();
        }
        Close();
    }
    
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Allow window to close and cancel any operation
        if (!_cts.IsCancellationRequested)
        {
            WasCancelled = true;
            _cts.Cancel();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        if (!_cts.IsCancellationRequested)
        {
            WasCancelled = true;
            _cts.Cancel();
        }
        base.OnClosed(e);
    }
}
