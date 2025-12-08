namespace PdfLibrary.Fixups;

/// <summary>
/// Centralized configuration for PDF fixups.
/// This is the single source of truth for which fixups are enabled by default.
/// </summary>
public static class FixupDefaults
{
    /// <summary>
    /// Creates a FixupConfiguration with recommended default settings.
    /// Modify this method to enable/disable fixups globally.
    /// </summary>
    public static FixupConfiguration CreateDefaultConfiguration()
    {
        var config = new FixupConfiguration();

        // Add more fixups here as they are implemented
        // Example:
        // config.Enable("ColorSpaceFallback");
        // config.Enable("ImageDecodeArrayFix");

        return config;
    }

    /// <summary>
    /// Creates a FixupConfiguration with all fixups disabled.
    /// Useful for debugging or when you want to start with a clean slate.
    /// </summary>
    public static FixupConfiguration CreateDisabledConfiguration()
    {
        return new FixupConfiguration();
    }

    /// <summary>
    /// Registers all available fixups with the manager.
    /// This is the single place where fixup instances are created and registered.
    /// </summary>
    public static void RegisterAllFixups(FixupManager manager)
    {
        // Add fixups here as they are implemented
        // Example:
        // manager.RegisterFixup(new ColorSpaceFallbackFixup());
        // manager.RegisterFixup(new ImageDecodeArrayFixup());
    }
}
