using System.Reflection;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Access to ICC color profiles bundled as embedded resources in PdfLibrary.
/// </summary>
internal static class IccResources
{
    /// <summary>Manifest resource name of the bundled default CMYK profile (U.S. Web Coated (SWOP) v2).</summary>
    public const string SwopResourceName = "PdfLibrary.Rendering.Icc.Profiles.USWebCoatedSWOP.icc";

    /// <summary>Reads the bundled SWOP v2 profile bytes. Throws if the resource is missing.</summary>
    public static byte[] ReadSwop()
    {
        using Stream stream = typeof(IccResources).Assembly.GetManifestResourceStream(SwopResourceName)
            ?? throw new InvalidOperationException(
                $"Bundled ICC resource '{SwopResourceName}' was not found in the PdfLibrary assembly.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
