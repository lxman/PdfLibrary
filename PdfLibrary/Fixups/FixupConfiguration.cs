namespace PdfLibrary.Fixups;

/// <summary>
/// Configuration for PDF fixups.
/// Controls which fixups are enabled and their settings.
/// </summary>
public class FixupConfiguration
{
    /// <summary>
    /// Dictionary of fixup names to their enabled state.
    /// If a fixup is not in this dictionary, it defaults to disabled.
    /// </summary>
    public Dictionary<string, bool> EnabledFixups { get; set; } = new();

    /// <summary>
    /// Checks if a specific fixup is enabled.
    /// </summary>
    /// <param name="fixupName">The name of the fixup to check.</param>
    /// <returns>True if the fixup is enabled, false otherwise.</returns>
    public bool IsEnabled(string fixupName)
    {
        return EnabledFixups.TryGetValue(fixupName, out bool enabled) && enabled;
    }

    /// <summary>
    /// Enables a fixup.
    /// </summary>
    /// <param name="fixupName">The name of the fixup to enable.</param>
    public void Enable(string fixupName)
    {
        EnabledFixups[fixupName] = true;
    }

    /// <summary>
    /// Disables a fixup.
    /// </summary>
    /// <param name="fixupName">The name of the fixup to disable.</param>
    public void Disable(string fixupName)
    {
        EnabledFixups[fixupName] = false;
    }

    /// <summary>
    /// Creates a default configuration with all fixups disabled.
    /// </summary>
    public static FixupConfiguration CreateDefault()
    {
        return new FixupConfiguration();
    }

    /// <summary>
    /// Creates a configuration with common fixups enabled.
    /// </summary>
    public static FixupConfiguration CreateWithCommonFixups()
    {
        var config = new FixupConfiguration();
        // Add common fixups here as they are identified
        return config;
    }
}
