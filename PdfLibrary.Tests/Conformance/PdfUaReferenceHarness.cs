using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Locates the PDF Association's PDF/UA-1 reference files at the sibling <c>../PDFUA-Reference-Files</c>
/// checkout and enumerates them. Every file is a real, published, <b>conformant</b> PDF/UA-1 document, so
/// this is a <b>pass-oracle</b>: the preflight must not flag any of them. These files are the ground-truth
/// oracle behind the Matterhorn-grounded reframe — the veraPDF corpus proves detection breadth, the
/// reference files prove zero false positives on documents real accessibility tooling accepts.
/// <para>
/// The files are not vendored (license/size), so consumers are <c>[Trait("Category","LocalOnly")]</c> and
/// skip when the checkout is absent. Set <c>PDFUA_REFERENCE_FILES</c> to override the location.
/// </para>
/// </summary>
internal static class PdfUaReferenceHarness
{
    private const string DirName = "PDFUA-Reference-Files";

    private static readonly Lazy<string?> RootLazy = new(Locate);

    public static string? Root => RootLazy.Value;

    public static bool IsAvailable => Root is not null;

    /// <summary>Every PDF/UA-1 reference file under the checkout, in stable path order.</summary>
    public static IEnumerable<string> Files()
    {
        if (Root is null)
            yield break;

        foreach (string path in Directory.EnumerateFiles(Root, "*.pdf", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
            yield return path;
    }

    private static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("PDFUA_REFERENCE_FILES");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, DirName);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
