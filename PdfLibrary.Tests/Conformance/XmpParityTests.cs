using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>Pins PdfLibrary's XMP conformance tables to veraPDF's, because the XMP Specification is
/// the WRONG standard to judge them by and reading it produces confident, wrong "fixes".
///
/// <para><b>Why this test exists (2026-08-13 XMP standards audit).</b> The tables are a direct port of
/// veraPDF's <c>org.verapdf.model.tools.xmp.XMPConstants</c>, and the engine's operative contract is
/// veraPDF parity — a strict subset with zero false positives across 1,316 files. PDF/A-2's governing
/// list is ISO 19005-2 Annex B, a 2005-era snapshot; the published XMP Specification is newer than
/// both. So the tables legitimately disagree with the current spec in at least nine places
/// (<c>xmpRights:Certificate</c> typed <c>url</c> where Part 1 Table 6 says Text; <c>ResourceEvent</c>
/// without <c>stEvt:changed</c>; <c>Marker.type</c> enumerating <c>Beat</c> where the spec says
/// <c>Speech</c>; <c>pdf:Trapped</c> absent entirely; ...). Every one of those looks exactly like a
/// bug to a reader holding the specification, and the 2026-08-13 audit initially reported four of them
/// as bugs. "Fixing" any of them makes the engine report findings veraPDF does not — a false positive,
/// which is the one thing this engine's contract forbids.</para>
///
/// <para><b>What it catches:</b> engine drift, in both directions, which is the bulk of the risk. It
/// does NOT catch veraPDF changing in a later release — the fixture is version-stamped and bumping
/// veraPDF is a deliberate act that regenerates it. See
/// <c>Resources/verapdf-xmp-parity-1.28.1.README.md</c> for the regeneration procedure, including the
/// trap that a <c>String[]</c>-only dump silently misses three registrations.</para>
///
/// <para>Deliberately NOT <c>[Trait("Category","LocalOnly")]</c>: this must run in CI, and this repo
/// has previously lost a fixture to <c>ci.yml</c>'s <c>Category!=LocalOnly</c> filter. It needs no
/// corpus and no veraPDF install — only the committed fixture.</para></summary>
public sealed class XmpParityTests
{
    private const string FixtureName = "verapdf-xmp-parity-1.28.1.txt";

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", FixtureName);

    [Fact]
    public void Predefined_properties_match_veraPdf_exactly()
    {
        ParityFixture fixture = ParityFixture.Load(FixturePath);

        var expected = fixture.Properties
            .ToDictionary(p => (p.NamespaceUri, p.LocalName), p => p.Type, TupleComparer);
        var actual = XmpPredefinedSchemas.All
            .ToDictionary(p => (p.NamespaceUri, p.LocalName), p => p.Type, TupleComparer);

        // Both directions, and reported together: a MISSING entry makes the engine raise a finding
        // veraPDF does not (a false positive), an EXTRA entry makes it silently accept a property
        // veraPDF rejects (a false negative). Neither is acceptable and each has a different cause,
        // so a failure must say which it is rather than just "counts differ".
        List<string> missing = expected.Keys.Except(actual.Keys, TupleComparer)
            .OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => $"  MISSING from engine: {k.Item1} {k.Item2} (veraPDF type '{expected[k]}')")
            .ToList();
        List<string> extra = actual.Keys.Except(expected.Keys, TupleComparer)
            .OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => $"  EXTRA in engine:     {k.Item1} {k.Item2} (engine type '{actual[k]}')")
            .ToList();
        List<string> wrongType = expected.Keys.Intersect(actual.Keys, TupleComparer)
            .Where(k => !string.Equals(expected[k], actual[k], StringComparison.Ordinal))
            .OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal)
            .Select(k => $"  TYPE DIFFERS:        {k.Item1} {k.Item2} — veraPDF '{expected[k]}', engine '{actual[k]}'")
            .ToList();

        Assert.True(
            missing.Count == 0 && extra.Count == 0 && wrongType.Count == 0,
            Explain($"{expected.Count} predefined properties", missing.Concat(wrongType).Concat(extra)));
    }

    [Fact]
    public void Structured_value_types_match_veraPdf_exactly()
    {
        ParityFixture fixture = ParityFixture.Load(FixturePath);

        Dictionary<string, StructEntry> expected =
            fixture.Structures.ToDictionary(s => s.TypeName, StringComparer.Ordinal);
        Dictionary<string, StructEntry> actual = XmpTypeContainer.Predefined23.Structures
            .ToDictionary(
                s => s.TypeName,
                s => new StructEntry(s.TypeName, s.ChildNamespaceUri,
                                     new SortedDictionary<string, string>(
                                         s.Fields.ToDictionary(f => f.Key, f => f.Value),
                                         StringComparer.Ordinal)),
                StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (string name in expected.Keys.Except(actual.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            problems.Add($"  MISSING struct type in engine: {name}");
        foreach (string name in actual.Keys.Except(expected.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            problems.Add($"  EXTRA struct type in engine:   {name}");

        foreach (string name in expected.Keys.Intersect(actual.Keys, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
        {
            StructEntry e = expected[name], a = actual[name];
            if (!string.Equals(e.ChildNamespaceUri, a.ChildNamespaceUri, StringComparison.Ordinal))
                problems.Add($"  {name}: child namespace — veraPDF '{e.ChildNamespaceUri}', engine '{a.ChildNamespaceUri}'");

            foreach (string f in e.Fields.Keys.Except(a.Fields.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal))
                problems.Add($"  {name}: MISSING field '{f}' (veraPDF type '{e.Fields[f]}')");
            foreach (string f in a.Fields.Keys.Except(e.Fields.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal))
                problems.Add($"  {name}: EXTRA field '{f}' (engine type '{a.Fields[f]}')");
            foreach (string f in e.Fields.Keys.Intersect(a.Fields.Keys, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal))
                if (!string.Equals(e.Fields[f], a.Fields[f], StringComparison.Ordinal))
                    problems.Add($"  {name}.{f}: type — veraPDF '{e.Fields[f]}', engine '{a.Fields[f]}'");
        }

        Assert.True(problems.Count == 0, Explain($"{expected.Count} structured value types", problems));
    }

    /// <summary>Every simple value type veraPDF registers is one the engine can validate.
    ///
    /// <para>Deliberately weaker than the two tests above: the engine stores validators as
    /// <c>Func&lt;XmpNode,bool&gt;</c>, so a registered regex is not recoverable from the container and
    /// cannot be diffed. The regexes WERE verified during the 2026-08-13 audit by decompiling
    /// veraPDF's <c>SimpleTypeValidator$SimpleTypeEnum</c> — <c>real</c>, <c>boolean</c>,
    /// <c>integer</c> and <c>mimetype</c> are character-for-character identical, unparenthesised
    /// alternation included — but that was a one-off comparison, not something this fixture can pin.
    /// This test therefore pins the type-name SET only, and the regexes remain an unpinned residual
    /// recorded here rather than left implicit.</para></summary>
    [Fact]
    public void Every_simple_type_veraPdf_registers_is_known_to_the_engine()
    {
        ParityFixture fixture = ParityFixture.Load(FixturePath);

        List<string> unknown = fixture.SimpleTypes
            .Where(t => !XmpTypeContainer.Predefined23.IsKnownType(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .Select(t => $"  engine cannot validate veraPDF simple type '{t}'")
            .ToList();

        Assert.True(unknown.Count == 0, Explain($"{fixture.SimpleTypes.Count} simple types", unknown));
    }

    private static readonly IEqualityComparer<(string, string)> TupleComparer =
        EqualityComparer<(string, string)>.Default;

    private static string Explain(string what, IEnumerable<string> problems)
    {
        List<string> list = problems.ToList();
        return $"""
            PdfLibrary's XMP tables have drifted from veraPDF ({what} compared, {list.Count} problem(s)).

            {string.Join(Environment.NewLine, list.Take(40))}

            READ THIS BEFORE "FIXING" THE ENGINE.
            These tables answer to veraPDF, NOT to the XMP Specification. The engine's value is being a
            strict subset of the reference with zero false positives; a table entry that is "more
            correct" per the published spec but absent from veraPDF makes the engine raise findings the
            reference does not.

            If you changed a table deliberately, you must ALSO regenerate the fixture and justify the
            move away from parity in the commit message. See
            PdfLibrary.Tests/Resources/verapdf-xmp-parity-1.28.1.README.md.
            """;
    }

    // ── fixture parsing ─────────────────────────────────────────────────────────────────────────

    private sealed record StructEntry(
        string TypeName, string ChildNamespaceUri, SortedDictionary<string, string> Fields);

    private sealed record PropertyEntry(string NamespaceUri, string LocalName, string Type);

    private sealed class ParityFixture
    {
        public List<PropertyEntry> Properties { get; } = [];
        public List<StructEntry> Structures { get; } = [];
        public List<string> SimpleTypes { get; } = [];

        public static ParityFixture Load(string path)
        {
            Assert.True(File.Exists(path),
                $"veraPDF parity fixture missing at {path}. It is a committed test resource; if the " +
                "build did not copy it, check the <None Update=\"Resources\\...\"/> entry in " +
                "PdfLibrary.Tests.csproj.");

            var fixture = new ParityFixture();
            string section = string.Empty;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith('['))
                {
                    section = line[1..line.IndexOf(']')];
                    continue;
                }

                string[] parts = line.Split('\t');
                switch (section)
                {
                    case "properties":
                        fixture.Properties.Add(new PropertyEntry(parts[0], parts[1], parts[2]));
                        break;
                    case "structured-types":
                        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
                        if (parts.Length > 2 && parts[2].Length > 0)
                        {
                            foreach (string f in parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                            {
                                int eq = f.IndexOf('=');
                                fields[f[..eq]] = f[(eq + 1)..];
                            }
                        }
                        fixture.Structures.Add(new StructEntry(parts[0], parts[1], fields));
                        break;
                    case "simple-types":
                        fixture.SimpleTypes.Add(parts[0]);
                        break;
                }
            }

            // A truncated or mis-parsed fixture would make every comparison below vacuously pass,
            // which is the failure mode a pinning test must never have.
            Assert.True(fixture.Properties.Count > 250,
                $"parity fixture parsed only {fixture.Properties.Count} properties — it is truncated " +
                "or the format changed; the comparisons would pass vacuously.");
            Assert.True(fixture.Structures.Count > 15,
                $"parity fixture parsed only {fixture.Structures.Count} structured types — truncated " +
                "or format changed.");
            return fixture;
        }
    }
}
