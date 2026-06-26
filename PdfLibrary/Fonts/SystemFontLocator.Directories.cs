namespace PdfLibrary.Fonts;

public sealed partial class SystemFontLocator
{
    /// <summary>Create a locator that scans the platform's standard font directories.</summary>
    public SystemFontLocator() : this(DefaultFontDirectories()) { }

    /// <summary>The standard OS font directories for the current platform.</summary>
    public static IReadOnlyList<string> DefaultFontDirectories()
    {
        var dirs = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(winFonts)) dirs.Add(winFonts);

            // Per-user fonts (Windows 10+): %LOCALAPPDATA%\Microsoft\Windows\Fonts
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
                dirs.Add(Path.Combine(localApp, "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add("/System/Library/Fonts");
            dirs.Add("/Library/Fonts");
            if (!string.IsNullOrEmpty(home)) dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        else // Linux and other Unix
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
                dirs.Add(Path.Combine(home, ".fonts"));
            }
        }

        return dirs;
    }
}
