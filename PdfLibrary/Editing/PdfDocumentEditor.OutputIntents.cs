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

    /// <summary>
    /// Replaces the <paramref name="intentIndex"/>-th /OutputIntents entry's embedded profile and rewrites
    /// its /OutputConditionIdentifier and /Info to match. The stale /OutputCondition and /RegistryName
    /// (human-readable claims that described the OLD profile) are removed rather than left to contradict
    /// the new one.
    /// </summary>
    public void ReplaceOutputIntentProfile(int intentIndex, byte[] iccProfileBytes,
                                            string outputConditionIdentifier, string? info = null)
    {
        ArgumentNullException.ThrowIfNull(iccProfileBytes);
        ArgumentNullException.ThrowIfNull(outputConditionIdentifier);
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("The document has no catalog.");

        if (ResolveObject(catalog.Get("OutputIntents")) is not PdfArray intents || intents.Count == 0)
            throw new InvalidOperationException("The document has no output intents.");
        if (intentIndex < 0 || intentIndex >= intents.Count)
            throw new ArgumentOutOfRangeException(nameof(intentIndex));

        var profileDict = new PdfDictionary
        {
            [new PdfName("N")] = new PdfInteger(IccComponentCount(iccProfileBytes)),
        };
        PdfIndirectReference profileRef = _document.RegisterObject(new PdfStream(profileDict, iccProfileBytes));

        PdfObject entry = intents[intentIndex];
        if (ResolveObject(entry) is not PdfDictionary intentDict)
            throw new InvalidOperationException("The output intent entry is not a dictionary.");

        intentDict[new PdfName("DestOutputProfile")] = profileRef;
        intentDict[new PdfName("OutputConditionIdentifier")] = PdfString.FromText(outputConditionIdentifier);
        if (info is not null)
            intentDict[new PdfName("Info")] = PdfString.FromText(info);
        else
            intentDict.Remove(new PdfName("Info"));
        intentDict.Remove(new PdfName("OutputCondition"));
        intentDict.Remove(new PdfName("RegistryName"));

        if (entry is PdfIndirectReference indirectRef)
        {
            _document.ReplaceObject(indirectRef.ObjectNumber, intentDict);
        }
        else
        {
            var newIntents = new PdfArray();
            for (int i = 0; i < intents.Count; i++)
                newIntents.Add(i == intentIndex ? intentDict : intents[i]);
            catalog[new PdfName("OutputIntents")] = newIntents;
        }
    }

    /// <summary>
    /// Drops every /OutputIntents entry except the one at <paramref name="keepIndex"/>. Dropped intent
    /// and profile objects are left in the object graph (orphaned objects are harmless, and other
    /// references to them may exist) rather than removed.
    /// </summary>
    public void ConsolidateOutputIntents(int keepIndex)
    {
        PdfDictionary catalog = _document.CatalogDictionary
            ?? throw new InvalidOperationException("The document has no catalog.");

        if (ResolveObject(catalog.Get("OutputIntents")) is not PdfArray intents || intents.Count == 0)
            throw new InvalidOperationException("The document has no output intents.");
        if (keepIndex < 0 || keepIndex >= intents.Count)
            throw new ArgumentOutOfRangeException(nameof(keepIndex));

        var kept = new PdfArray { intents[keepIndex] };
        catalog[new PdfName("OutputIntents")] = kept;
    }
}
