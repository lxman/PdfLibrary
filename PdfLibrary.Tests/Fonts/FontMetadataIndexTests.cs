using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontMetadataIndexTests
{
    private static FontFaceRecord Face(string ps, string family, bool italic, bool bold, int index = 0) =>
        new("f.ttf", index, ps, [family], family, italic ? "Italic" : "Regular", italic, bold);

    [Fact]
    public void PickBest_prefers_the_face_matching_both_style_bits()
    {
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
            Face("F-Italic", "F", italic: true, bold: false, index: 2),
            Face("F-BoldItalic", "F", italic: true, bold: true, index: 3),
        ];

        Assert.Equal("F-Italic", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
        Assert.Equal("F-BoldItalic", FontMetadataIndex.PickBest(faces, bold: true, italic: true)!.PostScriptName);
        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_degrades_rather_than_failing_when_the_style_is_absent()
    {
        // Italic requested, none available: keep the regular rather than returning nothing.
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
        ];

        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
    }

    [Fact]
    public void PickBest_breaks_ties_on_lowest_face_index()
    {
        // Indistinguishable faces must resolve the way they did before this index existed.
        FontFaceRecord[] faces = [Face("B", "F", false, false, index: 3), Face("A", "F", false, false, index: 1)];

        Assert.Equal("A", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_of_nothing_is_null()
    {
        Assert.Null(FontMetadataIndex.PickBest([], bold: false, italic: false));
    }

    [Fact]
    public void Indexes_the_real_system_fonts_by_postscript_name()
    {
        var index = new FontMetadataIndex(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(index.Faces.Count == 0, "no system fonts on this machine");

        // Measured on all three CI machines: 100% of faces carry a PostScript name.
        Assert.All(index.Faces, f => Assert.NotEmpty(f.PostScriptName));

        FontFaceRecord first = index.Faces[0];
        Assert.Same(first, index.ByPostScriptName(first.PostScriptName));
    }

    [Fact]
    public void Missing_directories_are_skipped_not_thrown()
    {
        var index = new FontMetadataIndex(["/definitely/not/a/real/path", ""]);
        Assert.Empty(index.Faces);
    }

    [Fact]
    public void A_fault_partway_through_a_directory_walk_does_not_escape_the_constructor()
    {
        // Directory.EnumerateFiles is LAZY: it throws nothing when called, so a try wrapped only
        // around the call catches nothing. The IO/permission fault surfaces while the foreach pulls
        // items. This matters far more than a skipped directory: the constructor runs inside a
        // Lazy<SystemFontLocator> with LazyThreadSafetyMode.ExecutionAndPublication, which CACHES a
        // thrown exception and rethrows it on every later access — one transient fault on one font
        // directory would kill font substitution for the whole process lifetime.
        string root = NewTempDir();
        string before = NewTempDir();
        string after = NewTempDir();
        // The subdirectory sorts AFTER the font file: Windows opens a subdirectory handle the moment
        // the walk meets the entry, so a name sorting first would fault before anything is yielded
        // and there would be nothing to prove survived.
        string denied = Path.Combine(root, "zz-denied");
        Directory.CreateDirectory(denied);

        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        File.WriteAllBytes(Path.Combine(before, "BeforeFont.ttf"), font);
        File.WriteAllBytes(Path.Combine(root, "RootFont.ttf"), font);
        File.WriteAllBytes(Path.Combine(denied, "DeniedFont.ttf"), font);
        File.WriteAllBytes(Path.Combine(after, "AfterFont.ttf"), font);

        try
        {
            Assert.SkipUnless(TryDenyTraversal(denied), "cannot revoke directory access on this platform/account");

            // Prove the walk really faults MID-traversal here — otherwise this test would pass for
            // the wrong reason (nothing thrown at all).
            var yielded = 0;
            Exception? fault = Record.Exception(() =>
            {
                foreach (string _ in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)) yielded++;
            });
            Assert.SkipWhen(fault is null, "the denied subdirectory did not fault the walk (elevated account?)");
            Assert.SkipWhen(yielded == 0, "the walk faulted before yielding anything — cannot prove survival");

            var index = new FontMetadataIndex([before, root, after]);

            // Earlier directories keep their results, the faulting directory keeps what it walked
            // before the fault, and later directories are still scanned.
            Assert.NotNull(index.FindPath("BeforeFont"));
            Assert.NotNull(index.FindPath("RootFont"));
            Assert.NotNull(index.FindPath("AfterFont"));
        }
        finally
        {
            RestoreTraversal(denied);
            foreach (string d in new[] { root, before, after })
                try { Directory.Delete(d, true); } catch { /* best effort */ }
        }
    }

    private static string NewTempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fmi-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Make <paramref name="dir"/> unlistable by this process. Windows needs an explicit
    /// deny ACE (an owner still has implicit rights, so removing an allow ACE is not enough); Unix
    /// mode 0 does it, except for root.</summary>
    private static bool TryDenyTraversal(string dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(dir, UnixFileMode.None); return true; }
            catch { return false; }
        }
        return Icacls(dir, $"/deny \"{CurrentUser()}\":(OI)(CI)(RX)");
    }

    private static void RestoreTraversal(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (OperatingSystem.IsWindows()) Icacls(dir, $"/remove:d \"{CurrentUser()}\"");
        else try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
             catch { /* best effort */ }
    }

    private static string CurrentUser() =>
        string.IsNullOrEmpty(Environment.UserDomainName)
            ? Environment.UserName
            : Environment.UserDomainName + "\\" + Environment.UserName;

    private static bool Icacls(string dir, string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls",
                $"\"{dir}\" {args}") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return false;
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public void PickFaceIndex_of_a_single_face_font_is_zero()
    {
        byte[] bare = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        Assert.Equal(0, FontMetadataIndex.PickFaceIndex(bare, bold: false, italic: true));
    }

    [Fact]
    public void PickFaceIndex_of_malformed_bytes_is_zero_not_an_exception()
    {
        Assert.Equal(0, FontMetadataIndex.PickFaceIndex([0x00, 0x01], bold: false, italic: false));
    }

    [Fact]
    public void PickBest_breaks_ties_the_same_way_regardless_of_candidate_order()
    {
        // Both score 1 against a Regular request: one is bold-only, one is italic-only. Today the
        // winner is whichever was enumerated first, which is filesystem order and not portable.
        var boldOnly = new FontFaceRecord("/b.ttf", 0, "Fam-Bold", ["Fam"], "Fam", "Bold", false, true);
        var italicOnly = new FontFaceRecord("/i.ttf", 0, "Fam-Italic", ["Fam"], "Fam", "Italic", true, false);

        FontFaceRecord? forward = FontMetadataIndex.PickBest([boldOnly, italicOnly], false, false);
        FontFaceRecord? reverse = FontMetadataIndex.PickBest([italicOnly, boldOnly], false, false);

        Assert.Equal(1, FontMetadataIndex.StyleScore(forward!, false, false));
        Assert.Equal(1, FontMetadataIndex.StyleScore(reverse!, false, false));
        Assert.Equal(forward!.PostScriptName, reverse!.PostScriptName);
    }
}
