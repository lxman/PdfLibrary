namespace PdfLibrary.Fonts;

/// <summary>
/// A font dictionary's indirect object number.
///
/// <para>Generation is deliberately not carried. <see cref="Conformance.Finding.ObjectNumber"/> does
/// not carry it either, and a handle the findings cannot express would let the two disagree about
/// which font a finding names.</para>
/// </summary>
public readonly record struct FontId(int ObjectNumber)
{
    public override string ToString() => ObjectNumber.ToString();
}
