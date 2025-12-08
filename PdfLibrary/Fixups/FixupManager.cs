namespace PdfLibrary.Fixups;

/// <summary>
/// Manages registration and execution of PDF fixups.
/// </summary>
public class FixupManager
{
    private readonly List<IPdfFixup> _fixups = new();
    private readonly FixupConfiguration _configuration;

    /// <summary>
    /// Creates a new fixup manager with the specified configuration.
    /// </summary>
    /// <param name="configuration">The fixup configuration.</param>
    public FixupManager(FixupConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Registers a fixup with the manager.
    /// </summary>
    /// <param name="fixup">The fixup to register.</param>
    public void RegisterFixup(IPdfFixup fixup)
    {
        if (fixup == null)
            throw new ArgumentNullException(nameof(fixup));

        if (_fixups.Any(f => f.Name == fixup.Name))
            throw new InvalidOperationException($"A fixup with name '{fixup.Name}' is already registered.");

        // Set enabled state from configuration
        fixup.IsEnabled = _configuration.IsEnabled(fixup.Name);

        _fixups.Add(fixup);

        // Keep fixups sorted by priority
        _fixups.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Applies all enabled fixups to the OnBeforePageRender hook.
    /// </summary>
    /// <param name="context">The page rendering context.</param>
    public void ApplyBeforePageRenderFixups(PageRenderContext context)
    {
        foreach (IPdfFixup fixup in _fixups.Where(f => f.IsEnabled))
        {
            fixup.OnBeforePageRender(context);
        }
    }

    /// <summary>
    /// Applies all enabled fixups to the OnTextRun hook.
    /// </summary>
    /// <param name="context">The text run context.</param>
    public void ApplyTextRunFixups(TextRunContext context)
    {
        foreach (IPdfFixup fixup in _fixups.Where(f => f.IsEnabled))
        {
            fixup.OnTextRun(context);
        }
    }

    /// <summary>
    /// Applies all enabled fixups to the OnAfterPageRender hook.
    /// </summary>
    /// <param name="context">The page rendering context.</param>
    public void ApplyAfterPageRenderFixups(PageRenderContext context)
    {
        foreach (IPdfFixup fixup in _fixups.Where(f => f.IsEnabled))
        {
            fixup.OnAfterPageRender(context);
        }
    }

    /// <summary>
    /// Gets all registered fixups.
    /// </summary>
    public IReadOnlyList<IPdfFixup> GetFixups() => _fixups.AsReadOnly();

    /// <summary>
    /// Gets a fixup by name.
    /// </summary>
    /// <param name="name">The name of the fixup.</param>
    /// <returns>The fixup if found, null otherwise.</returns>
    public IPdfFixup? GetFixup(string name)
    {
        return _fixups.FirstOrDefault(f => f.Name == name);
    }
}
