using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Appends an /OutputIntents entry with an embedded /DestOutputProfile ICC stream. /N is
    /// derived from the ICC header's data colour space (GRAY=1, RGB=3, CMYK=4). Plain append —
    /// callers deciding whether one is already present read <c>PdfDocument.GetOutputIntents()</c> first.
    /// </summary>
    public void AddOutputIntent(byte[] iccProfileBytes, string outputConditionIdentifier,
                                string? info = null, string subtype = "GTS_PDFA1")
    {
        ArgumentNullException.ThrowIfNull(iccProfileBytes);
        ArgumentNullException.ThrowIfNull(outputConditionIdentifier);
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("The document has no catalog.");

        var profileDict = new PdfDictionary
        {
            [new PdfName("N")] = new PdfInteger(IccComponentCount(iccProfileBytes)),
        };
        PdfIndirectReference profileRef = _document.RegisterObject(new PdfStream(profileDict, iccProfileBytes));

        var intent = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("OutputIntent"),
            [new PdfName("S")] = new PdfName(subtype),
            [new PdfName("OutputConditionIdentifier")] = PdfString.FromText(outputConditionIdentifier),
            [new PdfName("DestOutputProfile")] = profileRef,
        };
        if (info is not null)
            intent[new PdfName("Info")] = PdfString.FromText(info);
        PdfIndirectReference intentRef = _document.RegisterObject(intent);

        var intents = new PdfArray();
        if (ResolveObject(catalog.Get("OutputIntents")) is PdfArray existing)
            foreach (PdfObject entry in existing)
                intents.Add(entry);
        intents.Add(intentRef);
        catalog[new PdfName("OutputIntents")] = intents;
    }

    private static int IccComponentCount(byte[] icc)
    {
        if (icc.Length < 20)
            throw new ArgumentException("The bytes are too short to be an ICC profile.", nameof(icc));
        return Encoding.ASCII.GetString(icc, 16, 4) switch
        {
            "GRAY" => 1,
            "RGB " => 3,
            "CMYK" => 4,
            var cs => throw new ArgumentException($"Unsupported ICC data colour space '{cs.Trim()}'.", nameof(icc)),
        };
    }
}
