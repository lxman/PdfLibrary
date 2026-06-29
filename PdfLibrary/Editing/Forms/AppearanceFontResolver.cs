using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Resolves (or synthesises) the font resource needed for a field appearance stream.
/// Returns the resource name and an indirect reference to the font dictionary, so the
/// AP stream's /Resources /Font sub-dict can reference the font by object number.
/// </summary>
internal static class AppearanceFontResolver
{
    /// <summary>
    /// Looks up <paramref name="daFontName"/> in /AcroForm /DR /Font.
    /// If found, returns the name as-is and the existing (or newly-registered) indirect ref.
    /// If absent, synthesises a standard-14 font dict, registers it, and returns it.
    /// </summary>
    public static (string ResName, PdfIndirectReference FontRef) Resolve(
        PdfDocument doc, string daFontName)
    {
        // Try to find in /AcroForm /DR /Font
        PdfDictionary? acro = GetAcroForm(doc);
        if (acro is not null)
        {
            PdfObject? drRaw = acro.Get(new PdfName("DR"));
            if (Resolve(doc, drRaw) is PdfDictionary dr)
            {
                PdfObject? fontDictRaw = dr.Get(new PdfName("Font"));
                if (Resolve(doc, fontDictRaw) is PdfDictionary fontDict)
                {
                    // Look up by the DA font name
                    string lookupName = string.IsNullOrEmpty(daFontName) ? "Helv" : daFontName;
                    PdfObject? entry = fontDict.Get(new PdfName(lookupName));
                    if (entry is not null)
                    {
                        // If it's already an indirect reference, use it directly
                        if (entry is PdfIndirectReference ir)
                            return (lookupName, ir);

                        // It's a direct dict — register it as an indirect object
                        if (entry is PdfDictionary directFont)
                        {
                            PdfIndirectReference fontRef = doc.RegisterObject(directFont);
                            fontDict[new PdfName(lookupName)] = fontRef;
                            return (lookupName, fontRef);
                        }
                    }
                }
            }
        }

        // Synthesise a standard-14 Type1 font and register it
        string resName = string.IsNullOrEmpty(daFontName) ? "Helv" : daFontName;
        string baseFont = Standard14FontMap.BaseFont(resName);

        var synthesized = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName(baseFont)
        };

        PdfIndirectReference synthesizedRef = doc.RegisterObject(synthesized);

        // Also register in /AcroForm /DR /Font so repeated calls find it
        if (acro is not null)
        {
            PdfObject? drRaw = acro.Get(new PdfName("DR"));
            PdfDictionary dr;
            if (Resolve(doc, drRaw) is PdfDictionary existing)
            {
                dr = existing;
            }
            else
            {
                dr = new PdfDictionary();
                acro[new PdfName("DR")] = dr;
            }

            PdfObject? fontDictRaw = dr.Get(new PdfName("Font"));
            PdfDictionary fontDict;
            if (Resolve(doc, fontDictRaw) is PdfDictionary existingFontDict)
            {
                fontDict = existingFontDict;
            }
            else
            {
                fontDict = new PdfDictionary();
                dr[new PdfName("Font")] = fontDict;
            }

            fontDict[new PdfName(resName)] = synthesizedRef;
        }

        return (resName, synthesizedRef);
    }

    private static PdfDictionary? GetAcroForm(PdfDocument doc)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return null;
        PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        return Resolve(doc, acroRaw) as PdfDictionary;
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
