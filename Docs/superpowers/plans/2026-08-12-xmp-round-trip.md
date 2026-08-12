# XMP Honest Round-Trip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `XmpPacket` destroying structured XMP on save, by extracting the XMP format layer into its own project and making the existing recursive `XmpNode` model the single shared model with a serializer it never had.

**Architecture:** Two movements. First a pure relocation — the format layer (`XmpNode`, `XmpTreeParser`, `XmpDate`, `XmpPacket`, `XmpProperty`, `XmpSchemas`, `XmpValueKind`) moves to a new `Xmp` project with type forwarders preserving binary compatibility, no logic changed. Then the fix — `XmpNode` gains a serializer, and `XmpPacket` is re-implemented as a facade over the node tree while keeping its public API and flat accessors as computed projections.

**Tech Stack:** C#, .NET (`netstandard2.1` for the new project, matching `ICCSharp`/`FontParser`), xUnit v3, `System.Xml.Linq`.

**Spec:** `Docs/superpowers/specs/2026-08-12-xmp-round-trip-design.md`

## Global Constraints

- New project targets `netstandard2.1`, `LangVersion` `latest`, `Nullable` `enable` — matching `ICCSharp/ICCSharp.csproj`.
- Public types keep the `PdfLibrary.Metadata` namespace. Internal types move to `PdfLibrary.Xmp`.
- `Lxman.PdfLibrary` 2.5.2 is published. Moving public types across an assembly boundary is binary-breaking; type forwarders are **mandatory**, and a test must assert they exist.
- The format layer must not reference any `PdfLibrary` type. BCL only.
- All 36 pre-existing XMP tests keep their **assertions** unchanged. `using` edits are allowed; a changed assertion means the facade is wrong.
- The conformance suite stays green at every commit — the parser is veraPDF-parity-verified across 1,316 files.
- Fidelity is asserted on **parsed trees, not bytes** — attribute-form and element-form are equivalent RDF.
- `xunit v3`: there is no `Xunit.Abstractions`; do not use `ITestOutputHelper` from that namespace.
- Run the full engine suite with `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`.

## File Structure

**Created**
- `Xmp/Xmp.csproj` — the new format-layer project.
- `Xmp/XmpNode.cs` — moved model + `XmpTreeParser` (namespace `PdfLibrary.Xmp`).
- `Xmp/XmpDate.cs` — moved (namespace `PdfLibrary.Xmp`).
- `Xmp/XmpTreeSerializer.cs` — **new**: the missing write side.
- `Xmp/XmpPacket.cs`, `Xmp/XmpProperty.cs`, `Xmp/XmpSchemas.cs`, `Xmp/XmpValueKind.cs` — moved, namespace `PdfLibrary.Metadata` retained.
- `PdfLibrary/TypeForwards.cs` — forwarders for the four moved public types, with the removal note.
- `PdfLibrary.Tests/Metadata/XmpTypeForwardingTests.cs`
- `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`

**Modified**
- `PdfLibrary/PdfLibrary.csproj` — add `ProjectReference` to `Xmp`.
- 13 engine source files + 8 test files — `using` additions only (Task 1/2 enumerate them).
- `Docs/Architecture.md`, `CHANGELOG.md`.

---

### Task 1: Create the `Xmp` project and move the internal format layer

Pure relocation of the internal types. Behaviour-neutral: reviewable as "no logic diff, suite green".

**Files:**
- Create: `Xmp/Xmp.csproj`
- Move: `PdfLibrary/Conformance/Xmp/XmpNode.cs` → `Xmp/XmpNode.cs`
- Move: `PdfLibrary/Conformance/Xmp/XmpDate.cs` → `Xmp/XmpDate.cs`
- Modify: `PdfLibrary/PdfLibrary.csproj`
- Modify (usings only): `PdfLibrary/Conformance/ConformanceContext.cs`, `ConformanceClaim.cs`, `Conformance/Xmp/XmpTypeContainer.cs`, `Conformance/Xmp/XmpExtensionSchemas.cs`, `Conformance/Xmp/XmpPredefinedSchemas.cs`, `Conformance/Xmp/XmpStructTypes.cs`, and `Conformance/Rules/{PdfaIdentificationRule,PdfxVersionRule,UaContentLangRule,UaIdentificationRule,UaTitleRule,XmpExtensionSchemaStructureRule,XmpPropertyPredefinedRule,XmpPropertyTypeRule}.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: assembly `PdfLibrary.Xmp` exposing `internal` `XmpNode`, `XmpTreeParser`, `XmpDate` in namespace `PdfLibrary.Xmp`, visible to `PdfLibrary` and `PdfLibrary.Tests` via `InternalsVisibleTo`.

- [ ] **Step 1: Create the project file**

Create `Xmp/Xmp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>PdfLibrary.Xmp</AssemblyName>
    <RootNamespace>PdfLibrary</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- XmpNode/XmpTreeParser/XmpDate are internal to this assembly; PdfLibrary's conformance
         rules and the test project consume them directly. -->
    <InternalsVisibleTo Include="PdfLibrary" />
    <InternalsVisibleTo Include="PdfLibrary.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the project to the solution and reference it**

```bash
cd C:/Users/jorda/RiderProjects/PdfLibrary
dotnet sln add Xmp/Xmp.csproj
```

In `PdfLibrary/PdfLibrary.csproj`, add to the `ItemGroup` that already holds the sibling references (next to `ICCSharp`, around line 148):

```xml
      <ProjectReference Include="..\Xmp\Xmp.csproj" PrivateAssets="all" />
```

`PrivateAssets="all"` matches every sibling: the assembly is bundled into the package by the existing `CopyProjectReferencesToPackage` target rather than being a separate dependency.

- [ ] **Step 3: Move the two files with `git mv` (preserves history)**

```bash
cd C:/Users/jorda/RiderProjects/PdfLibrary
git mv PdfLibrary/Conformance/Xmp/XmpNode.cs Xmp/XmpNode.cs
git mv PdfLibrary/Conformance/Xmp/XmpDate.cs Xmp/XmpDate.cs
```

- [ ] **Step 4: Change the namespace in the moved files**

In both `Xmp/XmpNode.cs` and `Xmp/XmpDate.cs`, change the namespace declaration:

```csharp
namespace PdfLibrary.Xmp;
```

Change **nothing else** in these files. Leaving them under `…Conformance.Xmp` once they are no longer conformance-owned would re-create the confusion this slice exists to remove.

- [ ] **Step 5: Build and let the compiler enumerate the broken consumers**

```bash
dotnet build PdfLibrary/PdfLibrary.csproj 2>&1 | grep -E "error CS0246|error CS0103" | sort -u
```

Expected: `CS0246` for `XmpNode` / `XmpTreeParser` / `XmpDate` in the conformance files listed under **Files** above.

- [ ] **Step 6: Add `using PdfLibrary.Xmp;` to each reported file**

Add the using to every file the compiler named. Do not reorder or reformat other usings — keep the diff to one added line per file so the review stays mechanical.

- [ ] **Step 7: Build clean, then run the full suite**

```bash
dotnet build PdfLibrary/PdfLibrary.csproj 2>&1 | grep -E "Build succeeded|[0-9]+ Warning|[0-9]+ Error"
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

Expected: build succeeded, 0 warnings, 0 errors; the suite passes with the same counts as before the task. If the test project also fails to compile, add `using PdfLibrary.Xmp;` to the reported test files too.

- [ ] **Step 8: Commit**

```bash
git add Xmp PdfLibrary/PdfLibrary.csproj PdfLibrary.sln PdfLibrary PdfLibrary.Tests
git commit -m "refactor(xmp): extract the internal XMP format layer into its own project

XmpNode/XmpTreeParser/XmpDate reference no PdfLibrary type - they only live
under Conformance because conformance needed them first, which is how the
editing side ended up with a second, weaker XMP model. Pure move: namespace
changed to PdfLibrary.Xmp, no logic touched."
```

---

### Task 2: Move the public types with type forwarders

`Lxman.PdfLibrary` 2.5.2 is published. This task is where binary compatibility is either preserved or silently broken.

**Files:**
- Move: `PdfLibrary/Metadata/{XmpPacket,XmpProperty,XmpSchemas,XmpValueKind}.cs` → `Xmp/`
- Create: `PdfLibrary/TypeForwards.cs`
- Create: `PdfLibrary.Tests/Metadata/XmpTypeForwardingTests.cs`
- Modify (usings only): `PdfLibrary/Editing/PdfMetadata.cs` and any file the compiler reports

**Interfaces:**
- Consumes: the `PdfLibrary.Xmp` assembly from Task 1.
- Produces: `PdfLibrary.Metadata.XmpPacket`, `XmpProperty`, `XmpSchemas`, `XmpValueKind` now living in assembly `PdfLibrary.Xmp`, forwarded from `PdfLibrary`. Public API unchanged.

- [ ] **Step 1: Write the failing forwarder test**

Create `PdfLibrary.Tests/Metadata/XmpTypeForwardingTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Lxman.PdfLibrary 2.5.2 shipped XmpPacket and friends as public types IN PdfLibrary.dll.
/// Moving them to the Xmp assembly is binary-breaking without forwarders: anything compiled against
/// 2.5.2 gets a TypeLoadException. Nothing else in this repo can catch that, because everything here
/// recompiles from source — hence this test.</summary>
public class XmpTypeForwardingTests
{
    [Theory]
    [InlineData("PdfLibrary.Metadata.XmpPacket")]
    [InlineData("PdfLibrary.Metadata.XmpProperty")]
    [InlineData("PdfLibrary.Metadata.XmpSchemas")]
    [InlineData("PdfLibrary.Metadata.XmpValueKind")]
    public void PdfLibrary_still_forwards_the_moved_public_xmp_types(string fullName)
    {
        Assembly pdfLibrary = typeof(PdfLibrary.Document.PdfDocument).Assembly;

        string[] forwarded = pdfLibrary.GetForwardedTypes().Select(t => t.FullName!).ToArray();

        Assert.Contains(fullName, forwarded);
    }

    [Fact]
    public void The_forwarded_types_resolve_out_of_the_Xmp_assembly()
    {
        Assert.Equal("PdfLibrary.Xmp", typeof(XmpPacket).Assembly.GetName().Name);
    }
}
```

If `PdfLibrary.Document.PdfDocument` is not the correct type name for anchoring on the `PdfLibrary` assembly, substitute any public type that definitively lives in `PdfLibrary.dll` (confirm with `grep -rn "public sealed class PdfDocument" PdfLibrary/`).

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~XmpTypeForwardingTests" 2>&1 | grep -E "Passed!|Failed!|error"
```

Expected: FAIL — the types still live in `PdfLibrary`, so `GetForwardedTypes()` does not contain them and `typeof(XmpPacket).Assembly` is `PdfLibrary`.

- [ ] **Step 3: Move the four files**

```bash
cd C:/Users/jorda/RiderProjects/PdfLibrary
git mv PdfLibrary/Metadata/XmpPacket.cs    Xmp/XmpPacket.cs
git mv PdfLibrary/Metadata/XmpProperty.cs  Xmp/XmpProperty.cs
git mv PdfLibrary/Metadata/XmpSchemas.cs   Xmp/XmpSchemas.cs
git mv PdfLibrary/Metadata/XmpValueKind.cs Xmp/XmpValueKind.cs
```

**Do not change their namespace.** They must stay `namespace PdfLibrary.Metadata;` — that is what keeps source compatibility for downstream consumers.

- [ ] **Step 4: Add the forwarders**

Create `PdfLibrary/TypeForwards.cs`:

```csharp
using System.Runtime.CompilerServices;
using PdfLibrary.Metadata;

// These four types were public in Lxman.PdfLibrary 2.5.2 and shipped inside PdfLibrary.dll. The XMP
// format layer moved to the PdfLibrary.Xmp assembly on 2026-08-12; the forwarders keep every binary
// compiled against 2.5.2 working. Source is unaffected either way — the namespace did not change.
//
// TRANSITIONAL. These exist only to avoid a binary break within the 2.x line. Remove them at the next
// MAJOR version bump (3.0.0), together with XmpTypeForwardingTests, and note the removal in
// CHANGELOG.md as a breaking change so consumers know a recompile is required.
[assembly: TypeForwardedTo(typeof(XmpPacket))]
[assembly: TypeForwardedTo(typeof(XmpProperty))]
[assembly: TypeForwardedTo(typeof(XmpSchemas))]
[assembly: TypeForwardedTo(typeof(XmpValueKind))]
```

- [ ] **Step 5: Fix any consumers the compiler reports**

```bash
dotnet build PdfLibrary/PdfLibrary.csproj 2>&1 | grep -E "error CS" | sort -u
```

`PdfLibrary/Editing/PdfMetadata.cs` and files using `XmpSchemas` may need no change at all (the namespace is unchanged); fix only what the compiler actually reports.

- [ ] **Step 6: Run the forwarder test — expect PASS**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~XmpTypeForwardingTests" 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 7: Run the full suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

Expected: PASS, with the 36 XMP tests among them and their assertions untouched.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(xmp): move the public XMP types to the Xmp assembly, with forwarders

XmpPacket/XmpProperty/XmpSchemas/XmpValueKind keep the PdfLibrary.Metadata
namespace so source compatibility is untouched, and PdfLibrary carries
TypeForwardedTo for each so binaries built against Lxman.PdfLibrary 2.5.2 keep
resolving. The forwarders are transitional - removal is due at 3.0.0 and is
noted in TypeForwards.cs and CHANGELOG.md.

XmpTypeForwardingTests exists because nothing else here can catch a dropped
forwarder: every consumer in this repo recompiles from source."
```

---

### Task 3: Serializer — emit structs and arrays-of-structs

The actual new code. TDD against the real Illustrator packet that proved the bug.

**Files:**
- Create: `Xmp/XmpTreeSerializer.cs`
- Create: `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`

**Interfaces:**
- Consumes: `PdfLibrary.Xmp.XmpNode` (`Value`, `Children`, `IsSimple`, `IsStruct`, `IsArray`, `IsArrayOrdered`, `IsArrayAlternate`, `IsArrayAltText`, `HasXmlLang`, `XmlLang`, `NamespaceUri`, `LocalName`, `Prefix`), `PdfLibrary.Xmp.XmpTreeParser.Parse(byte[]?)`.
- Produces: `internal static class XmpTreeSerializer` with `internal static byte[] Serialize(IReadOnlyList<XmpNode> properties)` in namespace `PdfLibrary.Xmp`.

- [ ] **Step 1: Write the failing golden test**

Create `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>The bug this slice exists to fix: a Seq of ResourceEvent structs (Adobe Illustrator
/// 25.2 output, from CC-MAIN corpus file 0000_0000007.pdf) was flattened to one concatenated text
/// blob on serialize, because array items were read with XElement.Value and written back as plain
/// rdf:li text. Field names must survive by name.</summary>
public class XmpStructRoundTripTests
{
    private const string IllustratorPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
    xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#"
    xmlns:dc="http://purl.org/dc/elements/1.1/">
   <dc:format>application/pdf</dc:format>
   <xmpMM:History>
    <rdf:Seq>
     <rdf:li rdf:parseType="Resource">
      <stEvt:action>saved</stEvt:action>
      <stEvt:instanceID>xmp.iid:7acea5a3-d3b5-4e05-a570-0a5cf27dfe45</stEvt:instanceID>
      <stEvt:when>2021-06-04T14:38:59+09:00</stEvt:when>
      <stEvt:softwareAgent>Adobe Illustrator 25.2 (Macintosh)</stEvt:softwareAgent>
     </rdf:li>
    </rdf:Seq>
   </xmpMM:History>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    private static XmpNode Find(IReadOnlyList<XmpNode> nodes, string localName)
        => Assert.Single(nodes, n => n.LocalName == localName);

    [Fact]
    public void A_seq_of_structs_survives_serialize_with_its_field_names()
    {
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        byte[] emitted = XmpTreeSerializer.Serialize(before);
        string text = Encoding.UTF8.GetString(emitted);

        // The exact assertions the diagnostic probe failed against the old writer.
        Assert.Contains("stEvt:action", text);
        Assert.Contains("stEvt:when", text);
        Assert.Contains("stEvt:softwareAgent", text);
        Assert.Contains("parseType", text);
    }

    [Fact]
    public void Parse_serialize_parse_yields_an_equivalent_tree()
    {
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        IReadOnlyList<XmpNode> after = XmpTreeParser.Parse(XmpTreeSerializer.Serialize(before));

        // Compare trees, not bytes: attribute-form and element-form are equivalent RDF, so a byte
        // comparison would fail for correct output.
        XmpNode historyBefore = Find(before, "History");
        XmpNode historyAfter = Find(after, "History");

        Assert.True(historyAfter.IsArray);
        Assert.True(historyAfter.IsArrayOrdered);
        XmpNode eventBefore = Assert.Single(historyBefore.Children);
        XmpNode eventAfter = Assert.Single(historyAfter.Children);
        Assert.True(eventAfter.IsStruct);

        Assert.Equal(
            eventBefore.Children.Select(c => (c.LocalName, c.Value)).OrderBy(x => x.LocalName),
            eventAfter.Children.Select(c => (c.LocalName, c.Value)).OrderBy(x => x.LocalName));
    }

    [Fact]
    public void A_simple_property_still_round_trips()
    {
        IReadOnlyList<XmpNode> after =
            XmpTreeParser.Parse(XmpTreeSerializer.Serialize(
                XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket))));

        Assert.Equal("application/pdf", Find(after, "format").Value);
    }
}
```

Add `using System.Linq;` to the file — `Select`/`OrderBy` are used above.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~XmpStructRoundTripTests" 2>&1 | grep -E "Passed!|Failed!|error CS"
```

Expected: FAIL to compile — `XmpTreeSerializer` does not exist yet (`error CS0103`/`CS0246`). That is the correct first failure.

- [ ] **Step 3: Implement the serializer**

Create `Xmp/XmpTreeSerializer.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace PdfLibrary.Xmp;

/// <summary>The write half of <see cref="XmpTreeParser"/>. Emits the node tree as XMP-shaped
/// RDF/XML: structs as <c>rdf:parseType="Resource"</c>, arrays as <c>rdf:Seq</c>/<c>Bag</c>/<c>Alt</c>,
/// alt-text items carrying <c>xml:lang</c>.
///
/// <para>Every namespace used ANYWHERE in the tree is declared on the rdf:Description — including
/// struct-field namespaces such as <c>stEvt:</c>, which can appear without any top-level property
/// using them. Missing those declarations is how a faithful tree still serializes to a broken
/// packet.</para></summary>
internal static class XmpTreeSerializer
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace X = "adobe:ns:meta/";
    private static readonly XNamespace XmlNs = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Padding lets a consumer rewrite the packet in place without moving the rest of the
    /// file; the XMP spec recommends roughly 2 KB. Matches the previous writer's behaviour.</summary>
    private const int PaddingBytes = 2048;

    internal static byte[] Serialize(IReadOnlyList<XmpNode> properties)
    {
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", string.Empty));

        var namespaces = new Dictionary<string, string>();   // uri -> prefix
        foreach (XmpNode property in properties)
            CollectNamespaces(property, namespaces);

        foreach (KeyValuePair<string, string> ns in namespaces)
            description.Add(new XAttribute(XNamespace.Xmlns + ns.Value, ns.Key));

        foreach (XmpNode property in properties)
            description.Add(Emit(property));

        var meta = new XElement(X + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName),
            new XElement(Rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                description));

        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append(meta);
        sb.Append('\n');
        sb.Append(' ', PaddingBytes);
        sb.Append("\n<?xpacket end=\"w\"?>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void CollectNamespaces(XmpNode node, IDictionary<string, string> into)
    {
        if (!string.IsNullOrEmpty(node.NamespaceUri) && !into.ContainsKey(node.NamespaceUri))
            into[node.NamespaceUri] = node.Prefix;

        foreach (XmpNode child in node.Children)
            CollectNamespaces(child, into);
    }

    private static XElement Emit(XmpNode node)
    {
        XNamespace ns = node.NamespaceUri;
        var element = new XElement(ns + node.LocalName);

        if (node.IsArray)
        {
            string container = node.IsArrayAlternate ? "Alt" : node.IsArrayOrdered ? "Seq" : "Bag";
            var array = new XElement(Rdf + container);
            foreach (XmpNode item in node.Children)
                array.Add(EmitArrayItem(item));
            element.Add(array);
            return element;
        }

        if (node.IsStruct)
        {
            element.Add(new XAttribute(Rdf + "parseType", "Resource"));
            foreach (XmpNode field in node.Children)
                element.Add(Emit(field));
            return element;
        }

        if (node.HasXmlLang && node.XmlLang is { } lang)
            element.Add(new XAttribute(XmlNs + "lang", lang));

        element.Value = node.Value ?? string.Empty;
        return element;
    }

    /// <summary>An array item is an rdf:li whose content is the item's own shape — a struct item
    /// carries parseType="Resource" and its fields, a simple item carries text (plus xml:lang for
    /// alt-text). The item node's own name is not emitted; rdf:li replaces it.</summary>
    private static XElement EmitArrayItem(XmpNode item)
    {
        var li = new XElement(Rdf + "li");

        if (item.IsStruct)
        {
            li.Add(new XAttribute(Rdf + "parseType", "Resource"));
            foreach (XmpNode field in item.Children)
                li.Add(Emit(field));
            return li;
        }

        if (item.HasXmlLang && item.XmlLang is { } lang)
            li.Add(new XAttribute(XmlNs + "lang", lang));

        li.Value = item.Value ?? string.Empty;
        return li;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~XmpStructRoundTripTests" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: PASS, 3 tests. If `Parse_serialize_parse_yields_an_equivalent_tree` fails on `IsStruct`, check that `XmpTreeParser` recognises `rdf:parseType="Resource"` on an `rdf:li` — read its `HasStructContent` helper (around `XmpNode.cs:163`) and make the emitted shape match what it detects.

- [ ] **Step 5: Sabotage check — prove the tests can fail**

Temporarily replace the `node.IsStruct` branch body in `Emit` with `element.Value = string.Empty;`, re-run, and confirm the golden test goes RED. Then restore. A test that passes for the wrong reason is worse than no test.

- [ ] **Step 6: Run the full suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 7: Commit**

```bash
git add Xmp/XmpTreeSerializer.cs PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs
git commit -m "feat(xmp): add the serializer XmpNode never had

Emits structs as rdf:parseType=Resource and arrays as Seq/Bag/Alt, declaring
every namespace found anywhere in the tree - including struct-field namespaces
like stEvt: that no top-level property references. Golden test is the Adobe
Illustrator 25.2 packet from CC-MAIN 0000_0000007.pdf whose ResourceEvent fields
the old writer flattened into one text blob."
```

---

### Task 4: Re-implement `XmpPacket` as a facade over the node tree

**Files:**
- Modify: `Xmp/XmpPacket.cs`
- Modify: `Xmp/XmpProperty.cs` (add a node-backed construction path)

**Interfaces:**
- Consumes: `XmpTreeParser.Parse`, `XmpTreeSerializer.Serialize`, `XmpNode` from Tasks 1 and 3.
- Produces: `XmpPacket` with its existing public API unchanged — `static XmpPacket Parse(byte[])`, `void SetSimple(string, string, string, string)`, `void SetArray(string, string, string, IEnumerable<string>, bool)`, `void SetLangAlt(string, string, string, IReadOnlyDictionary<string, string>)`, `void Remove(string, string)`, `byte[] Serialize()`, and `XmpProperty`'s `Kind`/`Value`/`Items`/`LangAlt` as computed projections.

- [ ] **Step 1: Confirm the current public surface before changing it**

```bash
cd C:/Users/jorda/RiderProjects/PdfLibrary
grep -nE "public (static )?[A-Za-z<>\[\]?]+ [A-Za-z]+\(" Xmp/XmpPacket.cs
grep -nE "public " Xmp/XmpProperty.cs
```

Write the list down. Every signature here must still exist and behave identically at the end of this task. This is the contract the 36 existing tests encode.

- [ ] **Step 2: Back `XmpPacket` with nodes**

Replace the `Dictionary<(string ns, string local), XmpProperty> _props` field with the parsed node list, keyed the same way:

```csharp
// The parsed tree IS the model now. XmpProperty is projected from it on demand so the flat
// accessors (Kind/Value/Items/LangAlt) keep working for UaTitleRule and the existing tests,
// while structs that those accessors cannot express survive untouched in the node.
private readonly Dictionary<(string ns, string local), XmpNode> _nodes = new();
```

`Parse` delegates to `XmpTreeParser.Parse` and indexes the result. `Serialize` delegates to `XmpTreeSerializer.Serialize(_nodes.Values.ToList())`. The `Set*` methods build the equivalent `XmpNode` (simple / array / alt-text) and replace the entry. `Remove` drops the key.

- [ ] **Step 3: Project `XmpProperty` from a node**

Add an internal factory on `XmpProperty` that maps a node to the legacy shape:

```csharp
internal static XmpProperty FromNode(XmpNode node)
{
    if (node.IsArrayAltText)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XmpNode item in node.Children)
            map[item.XmlLang ?? "x-default"] = item.Value ?? string.Empty;
        return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, map);
    }

    if (node.IsArray)
    {
        // A struct item has no scalar value; the legacy Items projection cannot express it, so it
        // contributes an empty string rather than a flattened blob. The node keeps the real data
        // and Serialize emits it faithfully - which is the whole point of this change.
        var items = new List<string>();
        foreach (XmpNode item in node.Children)
            items.Add(item.Value ?? string.Empty);
        return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, items, node.IsArrayOrdered);
    }

    return new XmpProperty(node.NamespaceUri, node.Prefix, node.LocalName, node.Value ?? string.Empty);
}
```

- [ ] **Step 4: Run the 36 existing XMP tests — assertions must be untouched**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~XmpPacket" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: PASS, 36 tests, **with no edits to their assertions**. If one needs relaxing, the projection is wrong — fix the projection, not the test.

- [ ] **Step 5: Run the struct round-trip tests and the full suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 6: Prove the end-to-end fix through the public editing API**

Add to `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`:

```csharp
    [Fact]
    public void Setting_a_metadata_property_does_not_flatten_existing_structs()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        packet.SetSimple("http://ns.adobe.com/pdf/1.3/", "pdf", "Producer", "Pellucid");

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.Contains("stEvt:softwareAgent", text);   // the History struct survived the edit
        Assert.Contains("Pellucid", text);              // and the edit landed
    }
```

Add `using PdfLibrary.Metadata;` to the test file for `XmpPacket`. Run it; expect PASS.

- [ ] **Step 7: Commit**

```bash
git add Xmp PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs
git commit -m "fix(xmp): back XmpPacket with the node tree so saves stop flattening structs

XmpPacket keeps its whole public API; Kind/Value/Items/LangAlt become projections
over XmpNode. Structs the flat accessors cannot express now survive untouched
instead of being concatenated into one rdf:li text blob, so
PdfDocumentEditor.Metadata.Title = ... no longer destroys xmpMM:History.

All 36 pre-existing XMP tests pass with their assertions unchanged, which is the
proof the projections are faithful."
```

---

### Task 5: Verbatim fallback for shapes the model cannot express

**Files:**
- Modify: `Xmp/XmpNode.cs` (add `RawXml`), `Xmp/XmpTreeSerializer.cs`
- Modify: `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`

**Interfaces:**
- Consumes: `XmpNode`, `XmpTreeSerializer.Emit`.
- Produces: `XmpNode.RawXml` (`internal string? RawXml { get; set; }`) — when set, the serializer emits it verbatim and ignores the other members.

- [ ] **Step 1: Write the failing test**

Add to `XmpStructRoundTripTests.cs`:

```csharp
    /// <summary>A model that loses data on meeting the unfamiliar is exactly what caused this bug.
    /// Anything the node model cannot express must survive verbatim.</summary>
    [Fact]
    public void An_unmodelled_shape_survives_verbatim()
    {
        const string exotic = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.invalid/ns/">
   <ex:qualified>
    <rdf:Description>
     <rdf:value>the value</rdf:value>
     <ex:qualifier>the qualifier</ex:qualifier>
    </rdf:Description>
   </ex:qualified>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(exotic));
        string text = Encoding.UTF8.GetString(XmpTreeSerializer.Serialize(parsed));

        Assert.Contains("ex:qualifier", text);
        Assert.Contains("the qualifier", text);
    }
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~An_unmodelled_shape" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: FAIL — the qualifier is dropped, because `rdf:value`-plus-qualifier is not a shape the node model shares.

- [ ] **Step 3: Add `RawXml` to the node**

In `Xmp/XmpNode.cs`:

```csharp
    /// <summary>Set when the parser met a shape this model cannot express (e.g. rdf:value with
    /// qualifiers). The serializer emits this verbatim and ignores everything else on the node, so
    /// an unfamiliar packet is preserved rather than silently reshaped.</summary>
    public string? RawXml { get; set; }
```

In `XmpTreeParser`, when an element matches none of the simple/struct/array shapes, set `node.RawXml = el.ToString(SaveOptions.DisableFormatting);` and leave the other flags false.

- [ ] **Step 4: Honour it in the serializer**

At the top of `XmpTreeSerializer.Emit`:

```csharp
        if (node.RawXml is { } raw)
            return XElement.Parse(raw);
```

Preserved subtrees carry their own namespace declarations because `XElement.ToString()` emits the in-scope declarations it needs — so prefix rewriting elsewhere in the packet cannot leave them dangling.

- [ ] **Step 5: Run the test — expect PASS, then the full suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 6: Commit**

```bash
git add Xmp PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs
git commit -m "feat(xmp): preserve unmodelled shapes verbatim rather than dropping them

rdf:value-with-qualifiers and anything else the node model does not share is kept
as its raw subtree and re-emitted unchanged. Losing data on meeting the
unfamiliar is precisely the failure this slice exists to fix."
```

---

### Task 6: Struct authoring setters

Needed by the later PDF/A XMP remediation, which must emit a nested `pdfaExtension:schemas` block.

**Files:**
- Modify: `Xmp/XmpPacket.cs`
- Modify: `PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs`

**Interfaces:**
- Consumes: `XmpNode`, `XmpPacket`'s node dictionary from Task 4.
- Produces: two new public methods on `XmpPacket`:
  - `void SetStruct(string namespaceUri, string prefix, string localName, IReadOnlyList<XmpField> fields)`
  - `void SetStructArray(string namespaceUri, string prefix, string localName, IReadOnlyList<IReadOnlyList<XmpField>> items, bool ordered)`
  - and the supporting `public readonly record struct XmpField(string NamespaceUri, string Prefix, string LocalName, string Value)` in namespace `PdfLibrary.Metadata`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void A_struct_array_can_be_authored_and_round_trips()
    {
        var packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";
        const string evt = "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#";

        packet.SetStructArray(mm, "xmpMM", "History",
            [[ new XmpField(evt, "stEvt", "action", "converted"),
               new XmpField(evt, "stEvt", "softwareAgent", "Pellucid") ]],
            ordered: true);

        string text = Encoding.UTF8.GetString(packet.Serialize());
        Assert.Contains("stEvt:action", text);
        Assert.Contains("converted", text);
        Assert.Contains("Pellucid", text);
    }
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~A_struct_array_can_be_authored" 2>&1 | grep -E "Passed!|Failed!|error CS"
```

Expected: compile failure — `SetStructArray` and `XmpField` do not exist.

- [ ] **Step 3: Add `XmpField` and the two setters**

In `Xmp/XmpPacket.cs` (or a new `Xmp/XmpField.cs`), namespace `PdfLibrary.Metadata`:

```csharp
/// <summary>One field of an authored XMP struct. Fields carry their own namespace because a struct's
/// fields routinely live in a different namespace from the property (stEvt: inside xmpMM:History).</summary>
public readonly record struct XmpField(string NamespaceUri, string Prefix, string LocalName, string Value);
```

On `XmpPacket`:

```csharp
    /// <summary>Sets or replaces a struct-valued property (serialized as rdf:parseType="Resource").</summary>
    public void SetStruct(string namespaceUri, string prefix, string localName, IReadOnlyList<XmpField> fields)
    {
        var node = new XmpNode(namespaceUri, localName, prefix) { IsStruct = true };
        foreach (XmpField f in fields)
            node.Children.Add(new XmpNode(f.NamespaceUri, f.LocalName, f.Prefix) { IsSimple = true, Value = f.Value });
        _nodes[(namespaceUri, localName)] = node;
        RegisterPrefix(namespaceUri, prefix);
    }

    /// <summary>Sets or replaces an array-of-structs property (rdf:Seq when <paramref name="ordered"/>).</summary>
    public void SetStructArray(string namespaceUri, string prefix, string localName,
                               IReadOnlyList<IReadOnlyList<XmpField>> items, bool ordered)
    {
        var node = new XmpNode(namespaceUri, localName, prefix) { IsArray = true, IsArrayOrdered = ordered };
        foreach (IReadOnlyList<XmpField> item in items)
        {
            var element = new XmpNode(namespaceUri, "li", prefix) { IsStruct = true };
            foreach (XmpField f in item)
                element.Children.Add(new XmpNode(f.NamespaceUri, f.LocalName, f.Prefix) { IsSimple = true, Value = f.Value });
            node.Children.Add(element);
        }
        _nodes[(namespaceUri, localName)] = node;
        RegisterPrefix(namespaceUri, prefix);
    }
```

`RegisterPrefix` is the existing private helper that maintains `_prefixMap`/`_reversePrefixMap`; if it has a different name in the current file, use that one.

- [ ] **Step 4: Run the test — expect PASS, then the full suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj 2>&1 | grep -E "Passed!|Failed!"
```

- [ ] **Step 5: Commit**

```bash
git add Xmp PdfLibrary.Tests/Metadata/XmpStructRoundTripTests.cs
git commit -m "feat(xmp): authoring for structs and arrays-of-structs

SetStruct/SetStructArray plus XmpField, enough to emit a nested
pdfaExtension:schemas block for the later PDF/A XMP remediation. Fields carry
their own namespace because struct fields routinely live in a different one from
the property (stEvt: inside xmpMM:History)."
```

---

### Task 7: Documentation

**Files:**
- Modify: `Docs/Architecture.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: everything above. Produces: no code.

- [ ] **Step 1: Add the project to `Docs/Architecture.md`**

Under `## Project Structure` (around line 35), after the existing sibling-project entries, add:

```markdown
### XMP (`Xmp/`)

The XMP format layer — model, parser, serializer — as its own `netstandard2.1` assembly
(`PdfLibrary.Xmp`), alongside `ICCSharp` and `FontParser`. It knows nothing about PDF; it reads and
writes ISO 16684-1 packets.

`XmpNode`/`XmpTreeParser`/`XmpTreeSerializer` are the single shared model. Both consumers use it: the
conformance rules read it, and `XmpPacket` (public, namespace `PdfLibrary.Metadata`) is a facade over
it for editing. There was previously a second, flatter write-side model here, which could not
represent structs and silently flattened `xmpMM:History` on save.

PDF/A's *rules about* XMP — `XmpPredefinedSchemas`, `XmpStructTypes`, `XmpTypeContainer`,
`XmpExtensionSchemas` — deliberately stay in `Conformance/`. They encode ISO 19005, not XMP.

`PdfLibrary` carries `TypeForwardedTo` for the four public types that moved out of it in 2.5.x. Those
forwarders are **transitional and should be removed at 3.0.0** — see `PdfLibrary/TypeForwards.cs`.
```

- [ ] **Step 2: Add the `CHANGELOG.md` entry**

Under `## [Unreleased]`:

```markdown
## [Unreleased]

### Fixed
- **XMP: structured properties are no longer destroyed on save.** Setting any document property
  (`PdfDocumentEditor.Metadata.Title` and friends) re-serialized the XMP packet through a model with
  no struct representation, flattening `xmpMM:History`, `xmpMM:DerivedFrom`, `xmpTPg:Fonts` and
  similar into a single concatenated text blob — irrecoverably. `XmpPacket` is now backed by the
  recursive `XmpNode` model and emits structs and arrays-of-structs faithfully. Shapes the model
  cannot express are preserved verbatim.

### Changed
- The XMP format layer moved to its own assembly, `PdfLibrary.Xmp`, bundled in the package as with
  `ICCSharp` and `FontParser`. Namespaces are unchanged and `PdfLibrary` forwards the four public
  types, so this is source- and binary-compatible. **The forwarders are transitional and are due for
  removal at 3.0.0**, which will require a recompile for consumers still binding to the old
  assembly.

### Added
- `XmpPacket.SetStruct` / `SetStructArray` and `XmpField`, for authoring nested XMP.
```

- [ ] **Step 3: Verify the docs match the code**

```bash
cd C:/Users/jorda/RiderProjects/PdfLibrary
grep -n "TypeForwardedTo" PdfLibrary/TypeForwards.cs        # four lines
grep -n "3.0.0" PdfLibrary/TypeForwards.cs Docs/Architecture.md CHANGELOG.md
```

Expected: the removal-at-3.0.0 note appears in all three places. If the version differs anywhere, make them agree — a note that only exists in one file is a note that gets lost.

- [ ] **Step 4: Commit**

```bash
git add Docs/Architecture.md CHANGELOG.md
git commit -m "docs(xmp): record the Xmp project split and the transitional forwarders

Architecture.md gains the project and states why PDF/A's rules about XMP stay in
Conformance. CHANGELOG records the data-loss fix, the assembly move, and that the
type forwarders are due for removal at 3.0.0 - noted in three places so it cannot
quietly outlive its purpose."
```

---

## Self-Review

**Spec coverage**

| Spec requirement | Task |
|---|---|
| Extract format layer to its own project | 1, 2 |
| `netstandard2.1`, matching siblings | 1 |
| Public types keep `PdfLibrary.Metadata`; internals move to `PdfLibrary.Xmp` | 1, 2 |
| Type forwarders mandatory + tested | 2 |
| `XmpNode` becomes the single shared model | 4 |
| Serializer with struct-field namespace collection | 3 |
| Verbatim fallback for unmodelled shapes | 5 |
| Struct authoring for `pdfaExtension:schemas` | 6 |
| Round-trip fidelity on trees not bytes | 3 |
| Golden test on the Illustrator packet | 3 |
| 36 existing tests, assertions unchanged | 4 |
| Sabotage check | 3 |
| Conformance suite green throughout | 1, 2, 4 (full-suite step each) |
| Docs + forwarder-removal note | 7 |

**Known gaps, accepted:** general XMP qualifiers are not modelled (spec records this); Task 5's verbatim fallback is the mitigation.

**Placeholder scan:** no TBD/TODO; every code step carries real code; no "similar to Task N".

**Type consistency:** `XmpTreeSerializer.Serialize(IReadOnlyList<XmpNode>) -> byte[]` is defined in Task 3 and consumed with that exact signature in Tasks 4 and 5. `XmpField` is defined in Task 6 and used only there. `XmpNode.RawXml` is defined in Task 5 and consumed only there. `_nodes` is introduced in Task 4 and used in Task 6.

**Risks the implementer should expect**

- Task 3 Step 4 names the likely failure: the parser's struct detection (`HasStructContent`, `XmpNode.cs:163`) must accept what the serializer emits. Read the parser before writing the emitter.
- Task 4 is the one place the 36 existing tests could legitimately go red. If they do, the projection is wrong — do not edit the tests.
- `RegisterPrefix` in Task 6 is a placeholder for whatever the existing private prefix helper is called; check `Xmp/XmpPacket.cs` before using it.
