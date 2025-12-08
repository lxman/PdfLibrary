using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Parsing;

/// <summary>
/// Result from parsing cross-reference data
/// </summary>
internal class PdfXrefParseResult(PdfXrefTable table, PdfDictionary? trailerDictionary, bool isXRefStream)
{
    public PdfXrefTable Table { get; } = table;
    public PdfDictionary? TrailerDictionary { get; } = trailerDictionary;
    public bool IsXRefStream { get; } = isXRefStream;
}