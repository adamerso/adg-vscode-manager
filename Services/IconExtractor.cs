using System.IO;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for loading application icons from embedded resources
/// </summary>
public static class IconExtractor
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    
    // Cached icons
    private static Icon? _colorIconInsiders;
    private static Icon? _colorIconRelease;
    private static Icon? _blinkIcon;
    private static Icon? _windowIcon;
    
    /// <summary>
    /// Get the color tray icon for specified channel
    /// Insiders = Code-Insiders.ico (blue), Release = Code_main.ico (green)
    /// </summary>
    public static Icon GetColorIcon(VscodeChannel channel)
    {
        if (channel == VscodeChannel.Insiders)
        {
            _colorIconInsiders ??= LoadEmbeddedIcon("icons.Code-Insiders.ico");
            return _colorIconInsiders;
        }
        else
        {
            _colorIconRelease ??= LoadEmbeddedIcon("icons.Code_main.ico");
            return _colorIconRelease;
        }
    }
    
    /// <summary>
    /// Get the blink icon (darker BW for 0.1s blink when update available)
    /// </summary>
    public static Icon GetBlinkIcon()
    {
        _blinkIcon ??= LoadEmbeddedIcon("icons.Code_bw_30.ico");
        return _blinkIcon;
    }
    
    /// <summary>
    /// Get the window/dialog icon (lighter BW - Code_bw_60.ico)
    /// </summary>
    public static Icon GetWindowIcon()
    {
        _windowIcon ??= LoadEmbeddedIcon("icons.Code_bw_60.ico");
        return _windowIcon;
    }
    
    /// <summary>
    /// Get window icon as WPF ImageSource (for dialog icons)
    /// </summary>
    public static System.Windows.Media.ImageSource? GetWindowIconImageSource()
    {
        return IconToImageSource(GetWindowIcon());
    }
    
    /// <summary>
    /// Convert Icon to WPF ImageSource
    /// </summary>
    public static System.Windows.Media.ImageSource? IconToImageSource(Icon? icon)
    {
        if (icon == null)
            return null;
        
        try
        {
            using var bitmap = icon.ToBitmap();
            var hBitmap = bitmap.GetHbitmap();
            
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }
    
    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = $"AdgVscodeManager.{resourceName}";
        
        var stream = assembly.GetManifestResourceStream(fullName);
        if (stream == null)
        {
            var available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new FileNotFoundException($"Embedded resource not found: {fullName}. Available: {available}");
        }
        
        // Read stream into memory to avoid issues with disposed streams
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        stream.Dispose();
        memoryStream.Position = 0;
        
        return new Icon(memoryStream);
    }
}
