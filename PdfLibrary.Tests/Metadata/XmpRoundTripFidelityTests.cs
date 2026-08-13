using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Round-trip fidelity for RDF shapes the 2026-08-13 standards audit found PdfLibrary's own
/// parser/serializer losing or reshaping. One region per defect, keyed to
/// <c>Docs/superpowers/specs/2026-08-13-xmp-round-trip-fidelity-design.md</c>.
///
/// <para>Every case here was REPRODUCED against the shipped code before its fix was written — the
/// audit found these by reading, and a defect nobody has demonstrated may not be reachable. The
/// fragments are the ones the spec documents, so the spec and these tests cannot drift.</para>
///
/// <para>Deliberately not <c>LocalOnly</c> and sub-second: <c>ci.yml</c> filters
/// <c>Category!=LocalOnly</c>, and this repo has previously lost a fixture that way.</para></summary>
public sealed class XmpRoundTripFidelityTests
{
    private static byte[] Packet(string body) => Encoding.UTF8.GetBytes($"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                   xmlns:xmp="http://ns.adobe.com/xap/1.0/">
            <rdf:Description rdf:about="">
        {body}
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """);

    private static string RoundTrip(string body) =>
        Encoding.UTF8.GetString(XmpPacket.Parse(Packet(body)).Serialize());

    // ── D1: xml:lang on a plain literal property ────────────────────────────────────────────────

    /// <summary>A language qualifier on a SIMPLE property survives the round trip.
    ///
    /// <para>It did not before 2026-08-13. <c>XmpTreeParser</c> populated
    /// <c>HasXmlLang</c>/<c>XmlLang</c> only in <c>SetArray</c>, for <c>rdf:li</c> items, so a plain
    /// literal's language was never read and could not be re-emitted; the <c>RawXml</c> safety net
    /// engages only in the <c>rdf:value</c> branch, which a plain literal never enters. Part 1 treats
    /// <c>xml:lang</c> as a qualifier legal on ANY value, not only on array items.</para>
    ///
    /// <para>The serializer needed no change: <c>EmitShape</c> already emitted the attribute for any
    /// node carrying it, ahead of its shape dispatch. The defect was entirely on the read side.</para></summary>
    [Fact]
    public void Xml_lang_on_a_simple_property_survives_the_round_trip()
    {
        string outXml = RoundTrip("""      <dc:source xml:lang="en-us">Photo by A. Person</dc:source>""");

        Assert.Contains("Photo by A. Person", outXml);
        Assert.Contains("xml:lang=\"en-us\"", outXml);
    }

    /// <summary>The language qualifier is readable from the parsed packet, not merely re-emitted —
    /// so a consumer sees it too, rather than it surviving only as bytes.</summary>
    [Fact]
    public void Xml_lang_on_a_simple_property_round_trips_through_a_second_parse()
    {
        string once = RoundTrip("""      <dc:source xml:lang="en-us">Photo by A. Person</dc:source>""");
        string twice = Encoding.UTF8.GetString(
            XmpPacket.Parse(Encoding.UTF8.GetBytes(once)).Serialize());

        // Idempotence is the real property: a qualifier that survives one pass but not the next is
        // still lost, just later — and a document is commonly re-saved more than once.
        Assert.Contains("xml:lang=\"en-us\"", twice);
    }

    /// <summary>A simple property with NO language is unchanged — the fix must not invent a
    /// qualifier, which would be its own corruption and would alter every existing document.</summary>
    [Fact]
    public void A_simple_property_without_a_language_gains_none()
    {
        string outXml = RoundTrip("""      <dc:source>Photo by A. Person</dc:source>""");

        Assert.Contains("Photo by A. Person", outXml);
        Assert.DoesNotContain("xml:lang", outXml);
    }

    /// <summary>A lang-alt still round-trips: its items' languages are read by <c>SetArray</c>, and
    /// the D1 fix must not disturb that path or double-emit the attribute.</summary>
    [Fact]
    public void A_lang_alt_still_round_trips_with_its_item_languages()
    {
        string outXml = RoundTrip("""
              <dc:title><rdf:Alt>
                <rdf:li xml:lang="x-default">Title</rdf:li>
                <rdf:li xml:lang="fr-FR">Titre</rdf:li>
              </rdf:Alt></dc:title>
        """);

        Assert.Contains("xml:lang=\"x-default\"", outXml);
        Assert.Contains("xml:lang=\"fr-FR\"", outXml);
        Assert.Contains("Titre", outXml);
    }
}
