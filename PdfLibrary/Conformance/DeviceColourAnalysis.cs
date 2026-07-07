using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance;

/// <summary>
/// Scans the content reachable from the pages (page content streams + Form XObjects, recursively) to
/// determine which device colour families are used. Covers path/text colour operators
/// (g/rg/k and cs/CS with an explicit Device* name), inline images, image XObjects, and
/// Default(Gray|RGB|CMYK) remapping — a device operator in a scope whose resources define the matching
/// Default* entry is treated as remapped to that (normally device-independent) space, not as device colour.
/// Deferred (may cause false negatives): tiling patterns, Type3 glyph procedures, shadings,
/// Separation/DeviceN/Indexed base spaces, annotation appearance streams, and the implicit default
/// (unset) fill colour, which is DeviceGray per ISO 32000.
/// </summary>
internal static class DeviceColourAnalysis
{
    public readonly record struct Usage(bool Gray, bool Rgb, bool Cmyk);

    public static Usage Scan(ConformanceContext context)
    {
        bool gray = false, rgb = false, cmyk = false;
        var visitedForms = new HashSet<int>();

        // Records a device colour space unless the current scope remaps it via a Default* entry.
        void Note(string? colourSpaceName, bool remapGray, bool remapRgb, bool remapCmyk)
        {
            switch (Normalize(colourSpaceName))
            {
                case "DeviceGray": if (!remapGray) gray = true; break;
                case "DeviceRGB": if (!remapRgb) rgb = true; break;
                case "DeviceCMYK": if (!remapCmyk) cmyk = true; break;
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
                            Note(csName.Value, remapGray, remapRgb, remapCmyk);
                        break;
                    case "BI" when op is InlineImageOperator { ImageMask: false } inlineImage:
                        // A stencil mask (/IM true) omits /CS and is painted in the current colour, so
                        // only a real image colour space counts (the operator defaults CS to DeviceGray).
                        Note(inlineImage.ColorSpace, remapGray, remapRgb, remapCmyk);
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
                // The invoking scope's Default* remapping applies to a device image colour space too.
                Note((context.Resolve(xobject.Dictionary.Get("ColorSpace")) as PdfName)?.Value,
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
