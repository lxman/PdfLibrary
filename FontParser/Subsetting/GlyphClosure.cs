using System.Collections.Generic;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Subsetting
{
    /// <summary>
    /// Computes the transitive closure of glyph IDs needed to retain a
    /// coherent TrueType subset. A composite glyph references one or more
    /// component glyphs by ID; those components may themselves be composites.
    /// Any subset that includes a composite glyph MUST also include every
    /// glyph in its transitive component tree, or the renderer will encounter
    /// missing glyphs.
    ///
    /// Usage:
    ///   var closure = GlyphClosure.Compute(sfntFont, usedGlyphIds);
    ///
    /// The returned sorted set contains every glyph ID the subset must retain.
    /// It always includes the originally requested IDs unchanged.
    ///
    /// Cycle safety: composite glyph definitions are not supposed to be cyclic
    /// per the spec, but real-world fonts occasionally contain corrupt data.
    /// The algorithm tracks visited nodes and will not recurse into a glyph it
    /// has already expanded, so cycles terminate rather than stack-overflow.
    /// </summary>
    public static class GlyphClosure
    {
        /// <summary>
        /// Compute the transitive glyph-ID closure for the given font and seed set.
        /// </summary>
        /// <param name="font">A fully parsed TrueType <see cref="SfntFont"/>.</param>
        /// <param name="usedGlyphIds">The seed set of glyph IDs used by the document.</param>
        /// <returns>
        /// A sorted array of glyph IDs (ascending) that a subset must retain.
        /// Includes every seed ID and every component ID transitively reachable
        /// from composite glyphs within that seed set.
        /// </returns>
        public static ushort[] Compute(SfntFont font, IEnumerable<ushort> usedGlyphIds)
        {
            GlyphTable? glyphTable = font.Glyf;

            // Work set: BFS / iterative DFS — we use a stack for DFS which naturally
            // handles arbitrary-depth composite trees without deep recursion.
            var closure = new SortedSet<ushort>();
            var stack = new Stack<ushort>();

            foreach (ushort id in usedGlyphIds)
            {
                if (closure.Add(id))
                    stack.Push(id);
            }

            // If there is no glyf table (CFF font or font without glyph data),
            // just return the seed set — there are no TrueType composites to expand.
            if (glyphTable is null)
            {
                var result = new ushort[closure.Count];
                closure.CopyTo(result);
                return result;
            }

            while (stack.Count > 0)
            {
                ushort current = stack.Pop();
                GlyphData? data = glyphTable.GetGlyphData(current);

                if (data?.GlyphSpec is not CompositeGlyph composite)
                    continue; // Simple glyph or missing glyph — no components to expand.

                foreach (CompositeGlyphComponent? component in composite.Components)
                {
                    ushort componentId = component.GlyphIndex;
                    // closure.Add returns true only when the id was NOT already present.
                    // This is our cycle/revisit guard: already-visited IDs are not
                    // pushed again, so cycles and diamond-patterns terminate cleanly.
                    if (closure.Add(componentId))
                        stack.Push(componentId);
                }
            }

            var arr = new ushort[closure.Count];
            closure.CopyTo(arr);
            return arr;
        }
    }
}
