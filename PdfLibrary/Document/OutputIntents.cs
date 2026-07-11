using ICCSharp.Profile;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>The ICC data colour-space family of an output intent's embedded destination profile.</summary>
public enum OutputIntentColorSpace { None, Gray, Rgb, Cmyk, Other }

/// <summary>
/// A read-only view of one <c>/OutputIntents</c> array entry (ISO 32000-1, 14.11.5): its subtype, output
/// condition metadata, and — when present — the embedded destination ICC profile. Built with
/// <see cref="PdfDocument.GetOutputIntents"/>.
/// </summary>
public sealed class OutputIntentDescriptor
{
    private readonly byte[]? _destProfileBytes;

    internal OutputIntentDescriptor(
        string? subtype, string? outputConditionIdentifier, string? outputCondition,
        string? registryName, string? info, OutputIntentColorSpace colorSpace, byte[]? destProfileBytes)
    {
        Subtype = subtype;
        OutputConditionIdentifier = outputConditionIdentifier;
        OutputCondition = outputCondition;
        RegistryName = registryName;
        Info = info;
        ColorSpace = colorSpace;
        _destProfileBytes = destProfileBytes;
    }

    /// <summary>The intent's <c>/S</c> subtype name (e.g. <c>"GTS_PDFA1"</c>, <c>"GTS_PDFX"</c>), or null
    /// when absent.</summary>
    public string? Subtype { get; }

    /// <summary>The intent's <c>/OutputConditionIdentifier</c> text string, or null when absent.</summary>
    public string? OutputConditionIdentifier { get; }

    /// <summary>The intent's <c>/OutputCondition</c> human-readable description, or null when absent.</summary>
    public string? OutputCondition { get; }

    /// <summary>The intent's <c>/RegistryName</c> text string, or null when absent.</summary>
    public string? RegistryName { get; }

    /// <summary>The intent's <c>/Info</c> text string, or null when absent.</summary>
    public string? Info { get; }

    /// <summary>The ICC data colour-space family of the embedded <c>/DestOutputProfile</c>, or
    /// <see cref="OutputIntentColorSpace.None"/> when there is no usable embedded profile.</summary>
    public OutputIntentColorSpace ColorSpace { get; }

    /// <summary>True iff a <c>/DestOutputProfile</c> stream is present, decoded, and parses as an ICC
    /// profile — independent of <see cref="ColorSpace"/> (a valid RGB profile still yields true here).</summary>
    public bool HasDestProfile => _destProfileBytes is not null;

    /// <summary>A defensive copy of the embedded ICC profile bytes, or null when no usable profile is
    /// present.</summary>
    public byte[]? GetDestProfileBytes() => _destProfileBytes is null ? null : (byte[])_destProfileBytes.Clone();
}

/// <summary>
/// Reads a document's <c>/OutputIntents</c> array (ISO 32000-1, 14.11.5) into public
/// <see cref="OutputIntentDescriptor"/>s. This deliberately duplicates the small catalog walk that
/// <c>Conformance.ConformanceContext.ReadOutputIntents</c> performs internally — kept independent (rather
/// than reused) so this public reader never risks perturbing the load-bearing conformance suite.
/// </summary>
internal static class OutputIntentReader
{
    public static IReadOnlyList<OutputIntentDescriptor> Read(PdfDocument document)
    {
        var result = new List<OutputIntentDescriptor>();
        if (Resolve(document, document.GetCatalog()?.Dictionary.Get("OutputIntents")) is not PdfArray array)
            return result;

        foreach (PdfObject entry in array)
        {
            if (Resolve(document, entry) is not PdfDictionary dict)
                continue;

            string? subtype = (Resolve(document, dict.Get("S")) as PdfName)?.Value;
            string? outputConditionIdentifier = TextValue(document, dict.Get("OutputConditionIdentifier"));
            string? outputCondition = TextValue(document, dict.Get("OutputCondition"));
            string? registryName = TextValue(document, dict.Get("RegistryName"));
            string? info = TextValue(document, dict.Get("Info"));

            (OutputIntentColorSpace colorSpace, byte[]? destProfileBytes) =
                ReadDestProfile(document, dict.Get("DestOutputProfile"));

            result.Add(new OutputIntentDescriptor(
                subtype, outputConditionIdentifier, outputCondition, registryName, info,
                colorSpace, destProfileBytes));
        }
        return result;
    }

    /// <summary>Decodes and parses the intent's /DestOutputProfile once, yielding both its colour-space
    /// family and its bytes together (None/null when absent or unparseable as an ICC profile).</summary>
    private static (OutputIntentColorSpace ColorSpace, byte[]? Bytes) ReadDestProfile(
        PdfDocument document, PdfObject? destRaw)
    {
        if (Resolve(document, destRaw) is not PdfStream destStream)
            return (OutputIntentColorSpace.None, null);

        try
        {
            byte[] bytes = destStream.GetDecodedData(document.Decryptor);
            ProfileHeader header = IccProfile.Parse(bytes).Header;
            OutputIntentColorSpace colorSpace =
                header.DataColorSpace == ColorSpaceSignatures.CMYK ? OutputIntentColorSpace.Cmyk :
                header.DataColorSpace == ColorSpaceSignatures.RGB ? OutputIntentColorSpace.Rgb :
                header.DataColorSpace == ColorSpaceSignatures.Gray ? OutputIntentColorSpace.Gray :
                OutputIntentColorSpace.Other;
            return (colorSpace, bytes);
        }
        catch (Exception)
        {
            // Not a usable embedded profile (bad filter, truncated/malformed ICC data, …) — report as absent.
            return (OutputIntentColorSpace.None, null);
        }
    }

    private static string? TextValue(PdfDocument document, PdfObject? obj) =>
        Resolve(document, obj) is PdfString s ? s.GetText() : null;

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;
}
