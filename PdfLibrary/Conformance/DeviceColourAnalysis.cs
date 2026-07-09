using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance;

/// <summary>
/// Scans the content reachable from the pages (page content streams + Form XObjects, recursively) to
/// determine which device colour families are used. Covers path/text colour operators
/// (g/rg/k and cs/CS with a Device* name or a resource colour space), inline images, image XObjects, and
/// Default(Gray|RGB|CMYK) remapping — a device operator in a scope whose resources define the matching
/// Default* entry is treated as remapped to that (normally device-independent) space, not as device colour.
/// A named or inline Separation/DeviceN/Indexed/Pattern space is resolved to the device family of its
/// alternate/base space via <see cref="ColourSpaceClassifier"/> (so a spot colour whose fallback is an
/// uncalibrated device space is governed like a direct device fill).
/// Deferred (may cause false negatives): tiling patterns, Type3 glyph procedures, shadings, annotation
/// appearance streams, and the implicit default (unset) fill colour, which is DeviceGray per ISO 32000.
/// </summary>
internal static class DeviceColourAnalysis
{
    public readonly record struct Usage(bool Gray, bool Rgb, bool Cmyk);

    public static Usage Scan(ConformanceContext context)
    {
        bool gray = false, rgb = false, cmyk = false;
        var visitedForms = new HashSet<int>();

        // Notes a resolved device family, honouring the scope's Default* remap — a Separation/Indexed/
        // Pattern that bottoms out in DeviceCMYK is redirected by a DefaultCMYK entry just as a bare
        // device operator is (ISO 32000-1, 8.6.5.6), so the device usage does not count when remapped.
        void NoteFamily(OutputIntentColour family, bool remapGray, bool remapRgb, bool remapCmyk)
        {
            switch (family)
            {
                case OutputIntentColour.Gray: if (!remapGray) gray = true; break;
                case OutputIntentColour.Rgb: if (!remapRgb) rgb = true; break;
                case OutputIntentColour.Cmyk: if (!remapCmyk) cmyk = true; break;
            }
        }

        // A colour space named in content: a Device* abbreviation honours the scope's Default* remap;
        // any other name is a resource colour space, resolved and classified by its alternate/base family.
        void NoteNamed(string? name, PdfDictionary? colourSpaces, bool remapGray, bool remapRgb, bool remapCmyk)
        {
            switch (Normalize(name))
            {
                case "DeviceGray": if (!remapGray) gray = true; return;
                case "DeviceRGB": if (!remapRgb) rgb = true; return;
                case "DeviceCMYK": if (!remapCmyk) cmyk = true; return;
            }
            if (name is null || colourSpaces is null) return;
            PdfObject? definition = colourSpaces.Get(new PdfName(name));
            if (definition is not null)
                NoteFamily(ColourSpaceClassifier.DeviceFamily(context, definition), remapGray, remapRgb, remapCmyk);
        }

        // An image XObject's /ColorSpace: a name (Device* or resource) or an inline colour-space array.
        void NoteImageColourSpace(PdfObject? csObj, PdfDictionary? colourSpaces,
                                  bool remapGray, bool remapRgb, bool remapCmyk)
        {
            switch (context.Resolve(csObj))
            {
                case PdfName nm:
                    NoteNamed(nm.Value, colourSpaces, remapGray, remapRgb, remapCmyk);
                    break;
                case PdfArray arr:
                    NoteFamily(ColourSpaceClassifier.DeviceFamily(context, arr), remapGray, remapRgb, remapCmyk);
                    break;
            }
        }

        void Walk(PdfStream content, PdfResources? resources, int depth)
        {
            if (depth > 24) return;

            // DefaultGray/RGB/CMYK in this scope's /ColorSpace redirect the corresponding device space
            // (ISO 32000-1, 8.6.5.6), so device operators here do not count as device colour usage.
            PdfDictionary? colourSpaces = resources?.GetColorSpaces();
            bool remapGray = colourSpaces?.ContainsKey(new PdfName("DefaultGray")) == true;
            bool remapRgb = colourSpaces?.ContainsKey(new PdfName("DefaultRGB")) == true;
            bool remapCmyk = colourSpaces?.ContainsKey(new PdfName("DefaultCMYK")) == true;

            List<PdfOperator> ops;
            try { ops = PdfContentParser.Parse(content.GetDecodedData(context.Document.Decryptor)); }
            catch (Exception) { return; }

            foreach (PdfOperator op in ops)
            {
                switch (op.Name)
                {
                    case "g": case "G": if (!remapGray) gray = true; break;
                    case "rg": case "RG": if (!remapRgb) rgb = true; break;
                    case "k": case "K": if (!remapCmyk) cmyk = true; break;
                    case "cs": case "CS":
                        if (op.Operands.Count > 0 && op.Operands[0] is PdfName csName)
                            NoteNamed(csName.Value, colourSpaces, remapGray, remapRgb, remapCmyk);
                        break;
                    case "BI" when op is InlineImageOperator { ImageMask: false } inlineImage:
                        // A stencil mask (/IM true) omits /CS and is painted in the current colour, so
                        // only a real image colour space counts (the operator defaults CS to DeviceGray).
                        NoteNamed(inlineImage.ColorSpace, colourSpaces, remapGray, remapRgb, remapCmyk);
                        break;
                    case "Do" when resources is not null
                                   && op.Operands.Count > 0 && op.Operands[0] is PdfName xName:
                        WalkXObject(xName.Value, resources, depth, remapGray, remapRgb, remapCmyk);
                        break;
                }
            }
        }

        void WalkXObject(string name, PdfResources resources, int depth,
                         bool remapGray, bool remapRgb, bool remapCmyk)
        {
            PdfStream? xobject = resources.GetXObject(name);
            if (xobject is null) return;
            string? subtype = (xobject.Dictionary.Get("Subtype") as PdfName)?.Value;
            if (subtype == "Image")
            {
                // The invoking scope's Default* remapping applies to a device image colour space too; a
                // resource-name /ColorSpace resolves against that same scope's /ColorSpace dictionary.
                NoteImageColourSpace(xobject.Dictionary.Get("ColorSpace"), resources.GetColorSpaces(),
                    remapGray, remapRgb, remapCmyk);
            }
            else if (subtype == "Form")
            {
                // Guard the active recursion path only (add on enter, remove on exit) so a cycle is
                // caught while a form legitimately reused across scopes is still walked each time.
                if (xobject.IsIndirect && !visitedForms.Add(xobject.ObjectNumber)) return;
                PdfResources? formResources =
                    context.Resolve(xobject.Dictionary.Get("Resources")) is PdfDictionary rd
                        ? new PdfResources(rd, context.Document)
                        : resources; // inherit parent resources when the form has none
                Walk(xobject, formResources, depth + 1);
                if (xobject.IsIndirect) visitedForms.Remove(xobject.ObjectNumber);
            }
        }

        foreach (PdfPage page in context.Document.GetPages())
        {
            PdfResources? res = page.GetResources();
            foreach (PdfStream content in page.GetContents())
                Walk(content, res, 0);
        }

        return new Usage(gray, rgb, cmyk);
    }

    private static string? Normalize(string? cs) => cs switch
    {
        "G" or "DeviceGray" => "DeviceGray",
        "RGB" or "DeviceRGB" => "DeviceRGB",
        "CMYK" or "DeviceCMYK" => "DeviceCMYK",
        _ => null,
    };
}
