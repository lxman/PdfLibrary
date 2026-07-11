using System.Text;
using ICCSharp.Profile;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// Tests for <see cref="PdfDocument.GetOutputIntents"/> — the public read-only view of a document's
/// <c>/OutputIntents</c> array (ISO 32000-1, 14.11.5): subtype, output-condition metadata, and the
/// embedded destination ICC profile (bytes and colour-space family) when present.
/// </summary>
public class OutputIntentsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class IntentSpec
    {
        public string? Subtype = "GTS_PDFA1";
        public byte[]? DestProfile;
        public string? OutputConditionIdentifier;
        public string? OutputCondition;
        public string? RegistryName;
        public string? Info;
    }

    /// <summary>An in-memory document whose catalog has an /OutputIntents array with one intent
    /// dictionary per element of <paramref name="specs"/>, in order.</summary>
    private static PdfDocument DocWithOutputIntents(params IntentSpec[] specs)
    {
        var doc = new PdfDocument();
        var intents = new PdfArray();
        var nextObjectNumber = 2;

        foreach (IntentSpec spec in specs)
        {
            var intentDict = new PdfDictionary();
            if (spec.Subtype is not null)
                intentDict[new PdfName("S")] = new PdfName(spec.Subtype);
            if (spec.OutputConditionIdentifier is not null)
                intentDict[new PdfName("OutputConditionIdentifier")] =
                    new PdfString(Encoding.ASCII.GetBytes(spec.OutputConditionIdentifier));
            if (spec.OutputCondition is not null)
                intentDict[new PdfName("OutputCondition")] =
                    new PdfString(Encoding.ASCII.GetBytes(spec.OutputCondition));
            if (spec.RegistryName is not null)
                intentDict[new PdfName("RegistryName")] =
                    new PdfString(Encoding.ASCII.GetBytes(spec.RegistryName));
            if (spec.Info is not null)
                intentDict[new PdfName("Info")] = new PdfString(Encoding.ASCII.GetBytes(spec.Info));

            if (spec.DestProfile is not null)
            {
                int objectNumber = nextObjectNumber++;
                doc.AddObject(objectNumber, 0, new PdfStream(new PdfDictionary(), spec.DestProfile));
                intentDict[new PdfName("DestOutputProfile")] = new PdfIndirectReference(objectNumber, 0);
            }

            intents.Add(intentDict);
        }

        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("OutputIntents")] = intents,
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>An in-memory document with a catalog but no /OutputIntents key at all.</summary>
    private static PdfDocument DocWithoutOutputIntents()
    {
        var catalog = new PdfDictionary { [new PdfName("Type")] = new PdfName("Catalog") };
        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void EmbeddedCmykProfile_readsSubtypeAndCmykColorSpace()
    {
        byte[] cmyk = IccResources.ReadDefaultCmykProfile();
        PdfDocument doc = DocWithOutputIntents(new IntentSpec { Subtype = "GTS_PDFA1", DestProfile = cmyk });

        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.Equal("GTS_PDFA1", intent.Subtype);
        Assert.True(intent.HasDestProfile);
        Assert.Equal(OutputIntentColorSpace.Cmyk, intent.ColorSpace);

        byte[]? bytes = intent.GetDestProfileBytes();
        Assert.NotNull(bytes);
        ProfileHeader header = IccProfile.Parse(bytes!).Header;
        Assert.Equal(ColorSpaceSignatures.CMYK, header.DataColorSpace);
    }

    [Fact]
    public void EmbeddedSrgbProfile_readsRgbColorSpace()
    {
        byte[] rgb = BuiltInProfiles.Srgb.Bytes.ToArray();
        PdfDocument doc = DocWithOutputIntents(new IntentSpec { DestProfile = rgb });

        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.True(intent.HasDestProfile);
        Assert.Equal(OutputIntentColorSpace.Rgb, intent.ColorSpace);
    }

    [Fact]
    public void NoDestProfile_conditionMetadataRoundTrips()
    {
        PdfDocument doc = DocWithOutputIntents(new IntentSpec
        {
            Subtype = "GTS_PDFX",
            OutputConditionIdentifier = "FOGRA39",
            OutputCondition = "Coated FOGRA39",
            RegistryName = "http://www.color.org",
            Info = "ISO Coated v2",
        });

        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.False(intent.HasDestProfile);
        Assert.Equal(OutputIntentColorSpace.None, intent.ColorSpace);
        Assert.Null(intent.GetDestProfileBytes());
        Assert.Equal("FOGRA39", intent.OutputConditionIdentifier);
        Assert.Equal("Coated FOGRA39", intent.OutputCondition);
        Assert.Equal("http://www.color.org", intent.RegistryName);
        Assert.Equal("ISO Coated v2", intent.Info);
    }

    [Fact]
    public void NoOutputIntentsKey_returnsEmptyList()
    {
        PdfDocument doc = DocWithoutOutputIntents();
        Assert.Empty(doc.GetOutputIntents());
    }

    [Fact]
    public void MalformedDestProfile_hasNoUsableProfile()
    {
        PdfDocument doc = DocWithOutputIntents(new IntentSpec { DestProfile = [1, 2, 3] });

        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.False(intent.HasDestProfile);
        Assert.Equal(OutputIntentColorSpace.None, intent.ColorSpace);
        Assert.Null(intent.GetDestProfileBytes());
    }

    [Fact]
    public void TwoIntents_orderPreserved()
    {
        PdfDocument doc = DocWithOutputIntents(
            new IntentSpec { Subtype = "GTS_PDFA1", OutputConditionIdentifier = "first" },
            new IntentSpec { Subtype = "GTS_PDFX", OutputConditionIdentifier = "second" });

        IReadOnlyList<OutputIntentDescriptor> intents = doc.GetOutputIntents();
        Assert.Equal(2, intents.Count);
        Assert.Equal("GTS_PDFA1", intents[0].Subtype);
        Assert.Equal("first", intents[0].OutputConditionIdentifier);
        Assert.Equal("GTS_PDFX", intents[1].Subtype);
        Assert.Equal("second", intents[1].OutputConditionIdentifier);
    }

    [Fact]
    public void GetDestProfileBytes_returnsDefensiveCopy()
    {
        byte[] cmyk = IccResources.ReadDefaultCmykProfile();
        PdfDocument doc = DocWithOutputIntents(new IntentSpec { DestProfile = cmyk });
        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());

        byte[]? first = intent.GetDestProfileBytes();
        Assert.NotNull(first);
        first![0] ^= 0xFF; // mutate the returned copy

        byte[]? second = intent.GetDestProfileBytes();
        Assert.NotNull(second);
        Assert.Equal(cmyk[0], second![0]); // unaffected by the mutation of the first copy
        Assert.NotEqual(first[0], second[0]);
    }
}
