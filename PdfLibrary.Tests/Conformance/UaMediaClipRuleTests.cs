using System;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/UA-1 (ISO 14289-1:2014, 7.18.6.2, <see cref="UaMediaClipRule"/>) — media clip data requirements,
/// calibrated against veraPDF's PDF_UA-1 profile and its 7.18.6.2-t01/t02 fixtures. A media clip is reached
/// from an annotation's Rendition action (<c>/A → /S /Rendition → /R → /C</c>). veraPDF's model
/// (<c>PDMediaClip</c>) applies two tests: <c>CT != null</c> (a present content-type string) and
/// <c>hasCorrectAlt</c> (a <c>/Alt</c> array of even length whose every element is a string and whose every
/// odd-indexed text value is non-empty).
/// </summary>
public class UaMediaClipRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.ASCII.GetBytes(s));
    private static PdfArray Rect() =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100));

    private static PdfArray Alt(params string[] items) => new(items.Select(Str).Cast<PdfObject>().ToArray());

    private static Finding[] Findings(PdfDocument doc) =>
        new UaMediaClipRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfUA1)).ToArray();

    /// <summary>A one-page document whose page carries a single <paramref name="subtype"/> annotation
    /// (object 40) reaching a media clip dictionary configured by <paramref name="configClip"/> through a
    /// Rendition action. <paramref name="via"/> controls how the action is wired to the annotation:
    /// <c>"A"</c> (the /A entry), <c>"AA"</c> (an /AA/PV additional action), or <c>"Next"</c> (the /Next of a
    /// benign /A action). When <paramref name="action"/> is provided it replaces the default Rendition
    /// action at /A entirely (used for the negative "no media clip" cases).</summary>
    private static PdfDocument ClipDoc(
        Action<PdfDictionary>? configClip, string subtype = "Screen", PdfObject? action = null, string via = "A")
    {
        var doc = new PdfDocument();

        var annot = new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N(subtype),
            [N("Rect")] = Rect(),
        };

        if (action is not null)
        {
            annot[N("A")] = action;
        }
        else
        {
            var clip = new PdfDictionary
            {
                [N("Type")] = N("MediaClip"),
                [N("S")] = N("MCD"),
                [N("D")] = new PdfDictionary { [N("Type")] = N("Filespec"), [N("F")] = Str("movie.mp4") },
            };
            configClip?.Invoke(clip);

            var renditionAction = new PdfDictionary
            {
                [N("Type")] = N("Action"),
                [N("S")] = N("Rendition"),
                [N("R")] = new PdfDictionary
                {
                    [N("Type")] = N("Rendition"),
                    [N("S")] = N("MR"),
                    [N("C")] = clip,
                },
            };

            switch (via)
            {
                case "AA":
                    annot[N("AA")] = new PdfDictionary { [N("PV")] = renditionAction };
                    break;
                case "Next":
                    annot[N("A")] = new PdfDictionary
                    {
                        [N("Type")] = N("Action"), [N("S")] = N("NoOp"), [N("Next")] = renditionAction,
                    };
                    break;
                default:
                    annot[N("A")] = renditionAction;
                    break;
            }
        }

        doc.AddObject(40, 0, annot);

        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Annots")] = new PdfArray(Ref(40)),
            [N("Tabs")] = N("S"),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── test 1: CT content type ───────────────────────────────────────────────

    [Fact]
    public void A_media_clip_without_a_CT_content_type_is_flagged()
    {
        // t01-fail-a: /Alt is a valid multi-language array, but /CT is absent.
        Finding f = Assert.Single(Findings(ClipDoc(clip => clip[N("Alt")] = Alt("", "A description"))));
        Assert.Equal("ua-media-clip", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfUA1, "7.18.6.2"), f.Clause);
        Assert.Contains("CT", f.Message);
    }

    [Fact]
    public void A_media_clip_with_a_non_string_CT_is_flagged()
    {
        // veraPDF getStringKey(CT) returns null unless /CT is a string, so a name value fails the CT test.
        Finding f = Assert.Single(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = N("video-mp4");
            clip[N("Alt")] = Alt("", "A description");
        })));
        Assert.Contains("CT", f.Message);
    }

    [Fact]
    public void A_media_clip_with_an_empty_string_CT_is_not_flagged_for_CT()
    {
        // t01-pass-a carries /CT () — an empty but present string, which veraPDF's CT != null accepts.
        Assert.Empty(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("");
            clip[N("Alt")] = Alt("en-US", "A description");
        })));
    }

    // ── test 2: Alt multi-language text array ─────────────────────────────────

    [Fact]
    public void A_media_clip_without_an_Alt_array_is_flagged()
    {
        // t02-fail-a: /CT is present, but /Alt is absent.
        Finding f = Assert.Single(Findings(ClipDoc(clip => clip[N("CT")] = Str("video/mp4"))));
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfUA1, "7.18.6.2"), f.Clause);
        Assert.Contains("Alt", f.Message);
    }

    [Fact]
    public void A_media_clip_whose_Alt_default_text_is_empty_is_flagged()
    {
        // t02-fail-b: /Alt [() ()] — the odd-indexed text value is an empty string.
        Finding f = Assert.Single(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("video/mp4");
            clip[N("Alt")] = Alt("", "");
        })));
        Assert.Contains("Alt", f.Message);
    }

    [Fact]
    public void A_media_clip_whose_Alt_array_has_odd_length_is_flagged()
    {
        // A multi-language text array must pair each language with a text string (even length).
        Finding f = Assert.Single(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("video/mp4");
            clip[N("Alt")] = Alt("en-US", "A description", "");
        })));
        Assert.Contains("Alt", f.Message);
    }

    [Fact]
    public void A_media_clip_whose_Alt_contains_a_non_string_element_is_flagged()
    {
        Finding f = Assert.Single(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("video/mp4");
            clip[N("Alt")] = new PdfArray(Str("en-US"), new PdfInteger(7));
        })));
        Assert.Contains("Alt", f.Message);
    }

    // ── passing clips ─────────────────────────────────────────────────────────

    [Fact]
    public void A_media_clip_with_CT_and_a_single_language_Alt_pair_is_not_flagged()
    {
        // t01-pass-a shape: /CT present, /Alt [(en-US) (text)].
        Assert.Empty(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("video/mp4");
            clip[N("Alt")] = Alt("en-US", "A description");
        })));
    }

    [Fact]
    public void A_media_clip_with_CT_and_a_default_language_Alt_pair_is_not_flagged()
    {
        // t02-pass-a shape: /Alt [(en-US) (text) () (Default text)] — an empty language KEY is allowed.
        Assert.Empty(Findings(ClipDoc(clip =>
        {
            clip[N("CT")] = Str("video/mp4");
            clip[N("Alt")] = Alt("en-US", "A description", "", "Default text");
        })));
    }

    // ── no media clip present ─────────────────────────────────────────────────

    [Fact]
    public void An_annotation_without_a_rendition_action_is_not_flagged()
    {
        var link = new PdfDictionary
        {
            [N("Type")] = N("Action"), [N("S")] = N("URI"), [N("URI")] = Str("https://example.org"),
        };
        Assert.Empty(Findings(ClipDoc(configClip: null, subtype: "Link", action: link)));
    }

    // ── traversal: actions reachable from the annotation ──────────────────────

    [Fact]
    public void A_media_clip_reached_via_an_additional_action_is_flagged()
    {
        // A Screen annotation that auto-plays on page view wires the rendition action under /AA /PV.
        Finding f = Assert.Single(Findings(ClipDoc(clip => clip[N("CT")] = Str("video/mp4"), via: "AA")));
        Assert.Contains("Alt", f.Message);
    }

    [Fact]
    public void A_media_clip_reached_via_a_Next_action_chain_is_flagged()
    {
        Finding f = Assert.Single(Findings(ClipDoc(clip => clip[N("CT")] = Str("video/mp4"), via: "Next")));
        Assert.Contains("Alt", f.Message);
    }

    [Fact]
    public void A_rendition_action_without_a_media_clip_is_not_flagged()
    {
        // A Rendition action whose /R rendition carries no /C media clip (e.g. a selector rendition).
        var action = new PdfDictionary
        {
            [N("Type")] = N("Action"),
            [N("S")] = N("Rendition"),
            [N("R")] = new PdfDictionary { [N("Type")] = N("Rendition"), [N("S")] = N("SR") },
        };
        Assert.Empty(Findings(ClipDoc(configClip: null, action: action)));
    }
}
