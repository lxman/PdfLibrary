using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance;

/// <summary>
/// Scans the content reachable from the pages to determine which device colour families are used. Covers
/// path/text colour operators (g/rg/k and cs/CS with a Device* name or a resource colour space), inline
/// images, image XObjects, tiling-pattern content, shadings, Type3 glyph procedures, annotation appearance
/// streams, the implicit (unset) DeviceGray fill/stroke, and Default(Gray|RGB|CMYK) remapping — a device
/// operator in a scope whose resources define the matching Default* entry is treated as remapped to that
/// (normally device-independent) space, not as device colour.
/// A named or inline Separation/DeviceN/Indexed/Pattern space is resolved to the device family of its
/// alternate/base space via <see cref="ColourSpaceClassifier"/> (so a spot colour whose fallback is an
/// uncalibrated device space is governed like a direct device fill).
/// Page content streams are walked usage-sensitively (only Form XObjects actually invoked via Do are
/// followed); tiling patterns, shadings, Type3 fonts and annotation appearances are walked usage-agnostically
/// from the reachable resource graph, mirroring <see cref="ConformanceContext.ReferencedFonts"/>.
/// </summary>
internal static class DeviceColourAnalysis
{
    public readonly record struct Usage(bool Gray, bool Rgb, bool Cmyk);

    public static Usage Scan(ConformanceContext context)
    {
        bool gray = false, rgb = false, cmyk = false;
        var visitedForms = new HashSet<int>(); // active Do-recursion path (page/form content walk)
        var graphSeen = new HashSet<int>();     // usage-agnostic resource-graph guard (patterns/forms/Type3/AP/resource dicts)

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

        // A /ColorSpace value (image XObject, shading, or PatternType-2 shading): a name (Device* or
        // resource) or an inline colour-space array. Honours the enclosing scope's Default* remap.
        void NoteColourSpace(PdfObject? csObj, PdfDictionary? colourSpaces,
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

        // Processes the operators of one content stream in a scope. When <paramref name="trackImplicitGray"/>
        // (page content only), a path-paint op fired before its fill/stroke colour was ever set counts the
        // implicit initial DeviceGray colour (ISO 32000-1, 8.6.3). q/Q is not modelled — colour-set state is
        // never un-set, which can only under-report (never a false positive).
        void WalkOps(List<PdfOperator> ops, PdfResources? resources, int depth, bool trackImplicitGray)
        {
            // DefaultGray/RGB/CMYK in this scope's /ColorSpace redirect the corresponding device space
            // (ISO 32000-1, 8.6.5.6), so device operators here do not count as device colour usage.
            PdfDictionary? colourSpaces = resources?.GetColorSpaces();
            bool remapGray = colourSpaces?.ContainsKey(new PdfName("DefaultGray")) == true;
            bool remapRgb = colourSpaces?.ContainsKey(new PdfName("DefaultRGB")) == true;
            bool remapCmyk = colourSpaces?.ContainsKey(new PdfName("DefaultCMYK")) == true;

            bool fillSet = false, strokeSet = false; // meaningful only when trackImplicitGray

            foreach (PdfOperator op in ops)
            {
                switch (op.Name)
                {
                    case "g": if (!remapGray) gray = true; fillSet = true; break;
                    case "G": if (!remapGray) gray = true; strokeSet = true; break;
                    case "rg": if (!remapRgb) rgb = true; fillSet = true; break;
                    case "RG": if (!remapRgb) rgb = true; strokeSet = true; break;
                    case "k": if (!remapCmyk) cmyk = true; fillSet = true; break;
                    case "K": if (!remapCmyk) cmyk = true; strokeSet = true; break;
                    case "cs":
                        if (op.Operands.Count > 0 && op.Operands[0] is PdfName csName)
                            NoteNamed(csName.Value, colourSpaces, remapGray, remapRgb, remapCmyk);
                        fillSet = true;
                        break;
                    case "CS":
                        if (op.Operands.Count > 0 && op.Operands[0] is PdfName csNameStroke)
                            NoteNamed(csNameStroke.Value, colourSpaces, remapGray, remapRgb, remapCmyk);
                        strokeSet = true;
                        break;
                    case "sc": case "scn": fillSet = true; break;
                    case "SC": case "SCN": strokeSet = true; break;
                    case "BI" when op is InlineImageOperator { ImageMask: false } inlineImage:
                        // A stencil mask (/IM true) omits /CS and is painted in the current colour, so
                        // only a real image colour space counts (the operator defaults CS to DeviceGray).
                        NoteNamed(inlineImage.ColorSpace, colourSpaces, remapGray, remapRgb, remapCmyk);
                        break;
                    case "Do" when resources is not null
                                   && op.Operands.Count > 0 && op.Operands[0] is PdfName xName:
                        WalkXObject(xName.Value, resources, depth, remapGray, remapRgb, remapCmyk);
                        break;
                    // Path-paint operators: the implicit initial fill/stroke colour is DeviceGray.
                    case "f": case "F": case "f*":
                        if (trackImplicitGray && !fillSet && !remapGray) gray = true;
                        break;
                    case "S":
                        if (trackImplicitGray && !strokeSet && !remapGray) gray = true;
                        break;
                    case "s":
                        if (trackImplicitGray && !strokeSet && !remapGray) gray = true;
                        break;
                    case "B": case "B*": case "b": case "b*":
                        if (trackImplicitGray && !remapGray && (!fillSet || !strokeSet)) gray = true;
                        break;
                }
            }
        }

        void Walk(PdfStream content, PdfResources? resources, int depth, bool trackImplicitGray = false)
        {
            if (depth > 24) return;
            List<PdfOperator> ops;
            try { ops = PdfContentParser.Parse(content.GetDecodedData(context.Document.Decryptor)); }
            catch (Exception) { return; }
            WalkOps(ops, resources, depth, trackImplicitGray);
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
                NoteColourSpace(xobject.Dictionary.Get("ColorSpace"), resources.GetColorSpaces(),
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

        // ── usage-agnostic reachability over the resource graph (mirrors CollectReferencedFonts) ──────
        // Follows tiling patterns, shadings, Type3 fonts, and nested Form XObject resources present in any
        // reachable /Resources — independent of whether a content operator actually invokes them.

        void WalkColourResources(PdfResources? resources, int depth)
        {
            if (resources is null || depth > 24) return;
            if (resources.Dictionary.IsIndirect && !graphSeen.Add(resources.Dictionary.ObjectNumber)) return;

            PdfDictionary? colourSpaces = resources.GetColorSpaces();
            bool remapGray = colourSpaces?.ContainsKey(new PdfName("DefaultGray")) == true;
            bool remapRgb = colourSpaces?.ContainsKey(new PdfName("DefaultRGB")) == true;
            bool remapCmyk = colourSpaces?.ContainsKey(new PdfName("DefaultCMYK")) == true;

            if (resources.GetPatterns() is { } patterns)
                foreach (PdfObject pattern in patterns.Values)
                    WalkPattern(pattern, colourSpaces, remapGray, remapRgb, remapCmyk, depth);

            if (resources.GetShadings() is { } shadings)
                foreach (PdfObject shading in shadings.Values)
                    NoteShading(shading, colourSpaces, remapGray, remapRgb, remapCmyk);

            if (resources.GetXObjects() is { } xobjects)
                foreach (PdfObject xobject in xobjects.Values)
                    WalkFormResources(xobject, depth);

            if (resources.GetFonts() is { } fonts)
                foreach (PdfObject font in fonts.Values)
                    WalkType3Font(font, depth);
        }

        void WalkPattern(PdfObject patternObj, PdfDictionary? enclosingColourSpaces,
                         bool remapGray, bool remapRgb, bool remapCmyk, int depth)
        {
            PdfObject? resolved = context.Resolve(patternObj);
            PdfDictionary? patternDict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
            if (patternDict is null) return;
            int patternType = (context.Resolve(patternDict.Get("PatternType")) as PdfInteger)?.Value ?? 0;

            if (patternType == 2)
            {
                // A shading pattern colours through its /Shading; classify it in the ENCLOSING scope.
                NoteShading(patternDict.Get("Shading"), enclosingColourSpaces, remapGray, remapRgb, remapCmyk);
                return;
            }

            // A tiling pattern (PatternType 1) is a content stream painted through the pattern's OWN
            // /Resources — recompute the Default* remap from that scope (never inherit the page's).
            if (resolved is not PdfStream tiling) return;
            if (tiling.IsIndirect && !graphSeen.Add(tiling.ObjectNumber)) return;
            PdfResources? patternResources =
                context.Resolve(tiling.Dictionary.Get("Resources")) is PdfDictionary prd
                    ? new PdfResources(prd, context.Document)
                    : null;
            Walk(tiling, patternResources, depth + 1);
            WalkColourResources(patternResources, depth + 1);
        }

        void NoteShading(PdfObject? shadingObj, PdfDictionary? colourSpaces,
                         bool remapGray, bool remapRgb, bool remapCmyk)
        {
            PdfObject? resolved = context.Resolve(shadingObj);
            PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
            if (dict is null) return;
            NoteColourSpace(dict.Get("ColorSpace"), colourSpaces, remapGray, remapRgb, remapCmyk);
        }

        void WalkFormResources(PdfObject? xobjectObj, int depth)
        {
            if (context.Resolve(xobjectObj) is not PdfStream stream) return;
            if ((stream.Dictionary.Get("Subtype") as PdfName)?.Value != "Form") return;
            if (stream.IsIndirect && !graphSeen.Add(stream.ObjectNumber)) return;
            // The form's own content colour is walked usage-sensitively via Do; here we only descend into
            // its resources to reach patterns/shadings/Type3 fonts that live inside the form.
            if (context.Resolve(stream.Dictionary.Get("Resources")) is PdfDictionary rd)
                WalkColourResources(new PdfResources(rd, context.Document), depth + 1);
        }

        void WalkType3Font(PdfObject? fontObj, int depth)
        {
            if (context.Resolve(fontObj) is not PdfDictionary font) return;
            if ((context.Resolve(font.Get("Subtype")) as PdfName)?.Value != "Type3") return;
            if (font.IsIndirect && !graphSeen.Add(font.ObjectNumber)) return;

            PdfResources? fontResources =
                context.Resolve(font.Get("Resources")) is PdfDictionary frd
                    ? new PdfResources(frd, context.Document)
                    : null;

            if (context.Resolve(font.Get("CharProcs")) is PdfDictionary charProcs)
                foreach (PdfObject glyphObj in charProcs.Values)
                    if (context.Resolve(glyphObj) is PdfStream glyph)
                        WalkType3Glyph(glyph, fontResources, depth + 1);

            WalkColourResources(fontResources, depth + 1);
        }

        void WalkType3Glyph(PdfStream glyph, PdfResources? fontResources, int depth)
        {
            if (depth > 24) return;
            List<PdfOperator> ops;
            try { ops = PdfContentParser.Parse(glyph.GetDecodedData(context.Document.Decryptor)); }
            catch (Exception) { return; }

            // A glyph procedure beginning with d1 is an uncoloured stencil (ISO 32000-1, 9.6.5.1): its
            // colour operators are ignored, so a stray colour op in a d1 glyph is not device colour usage.
            foreach (PdfOperator op in ops)
            {
                if (op.Name == "d1") return;
                if (op.Name == "d0") break;
            }
            WalkOps(ops, fontResources, depth, trackImplicitGray: false);
        }

        void WalkAnnotationAppearance(PdfObject? apObj, int depth)
        {
            if (context.Resolve(apObj) is not PdfDictionary appearance) return;
            foreach (PdfObject state in appearance.Values) // /N, /D, /R
            {
                switch (context.Resolve(state))
                {
                    case PdfStream apStream:
                        WalkAppearanceStream(apStream, depth);
                        break;
                    case PdfDictionary subStates: // per-state appearances (e.g. button on/off)
                        foreach (PdfObject sub in subStates.Values)
                            if (context.Resolve(sub) is PdfStream subStream)
                                WalkAppearanceStream(subStream, depth);
                        break;
                }
            }
        }

        void WalkAppearanceStream(PdfStream apStream, int depth)
        {
            if (apStream.IsIndirect && !graphSeen.Add(apStream.ObjectNumber)) return;
            PdfResources? apResources =
                context.Resolve(apStream.Dictionary.Get("Resources")) is PdfDictionary ard
                    ? new PdfResources(ard, context.Document)
                    : null;
            Walk(apStream, apResources, depth); // an appearance inherits the caller's colour, not gray
            WalkColourResources(apResources, depth);
        }

        foreach (PdfPage page in context.Document.GetPages())
        {
            PdfResources? res = page.GetResources();

            // A page's content streams are one logical stream (ISO 32000-1, 7.8.2): concatenate them so an
            // operator — or the fill/stroke colour-set state the implicit-gray check tracks — spans a stream
            // boundary. Implicit-gray tracking is page-content only (a form inherits the caller's colour).
            var combined = new List<byte>();
            foreach (PdfStream content in page.GetContents())
            {
                combined.AddRange(content.GetDecodedData(context.Document.Decryptor));
                combined.Add((byte)'\n');
            }
            try { WalkOps(PdfContentParser.Parse(combined.ToArray()), res, 0, trackImplicitGray: true); }
            catch (Exception) { /* unparseable page content: skip */ }

            WalkColourResources(res, 0);
        }

        foreach (PdfDictionary annot in context.Annotations)
            WalkAnnotationAppearance(annot.Get("AP"), 0);

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
