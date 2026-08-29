using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Saved-byte witnesses for every branch in the clause 6.6.2.3.3 detector. A plain full
/// rewrite preserves all 18 malformed forms. The narrow repair closes conventional-prefix failures
/// and absent human-readable descriptions, while identity/type/category gaps remain explicit refusals.</summary>
public class XmpExtensionSchemaStructureRepairTests
{
    private const string RuleId = "pdfa-xmp-extension-schema-structure";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["schema", "namespace prefix", true, BuildPacket("schema", null, wrongPrefix: true)];
        yield return ["property", "namespace prefix", true, BuildPacket("property", null, wrongPrefix: true)];
        yield return ["value type", "namespace prefix", true, BuildPacket("value type", null, wrongPrefix: true)];
        yield return ["field", "namespace prefix", true, BuildPacket("field", null, wrongPrefix: true)];

        yield return ["schema", "namespaceURI", false, BuildPacket("schema", "namespaceURI")];
        yield return ["schema", "prefix", false, BuildPacket("schema", "prefix")];
        yield return ["schema", "schema", true, BuildPacket("schema", "schema")];

        yield return ["property", "name", false, BuildPacket("property", "name")];
        yield return ["property", "valueType", false, BuildPacket("property", "valueType")];
        yield return ["property", "category", false, BuildPacket("property", "category")];
        yield return ["property", "description", true, BuildPacket("property", "description")];

        yield return ["value type", "type", false, BuildPacket("value type", "type")];
        yield return ["value type", "namespaceURI", false, BuildPacket("value type", "namespaceURI")];
        yield return ["value type", "prefix", false, BuildPacket("value type", "prefix")];
        yield return ["value type", "description", true, BuildPacket("value type", "description")];

        yield return ["field", "name", false, BuildPacket("field", "name")];
        yield return ["field", "valueType", false, BuildPacket("field", "valueType")];
        yield return ["field", "description", true, BuildPacket("field", "description")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_detector_branch_has_an_intentional_saved_byte_outcome(
        string level, string fieldName, bool safelyRepairable, string packetText)
    {
        byte[] original = DocumentBytes(Encoding.UTF8.GetBytes(packetText));
        PreflightResult before = Preflighter.Check(original, ConformanceProfile.PdfA2b);
        Assert.Contains(before.Findings, finding => finding.RuleId == RuleId);

        byte[] plainSaved = LoadEditSave(original);
        Assert.Contains(Preflighter.Check(plainSaved, ConformanceProfile.PdfA2b).Findings,
            finding => finding.RuleId == RuleId);

        XmpExtensionSchemaStructureRepairReport preview = Preview(original);
        if (safelyRepairable)
        {
            Assert.Equal(1, preview.AppliedCount);
            Assert.Empty(preview.Refused);
        }
        else
        {
            Assert.Equal(0, preview.AppliedCount);
            XmpExtensionSchemaStructureRefusal refusal = Assert.Single(preview.Refused);
            Assert.Equal(level, refusal.Level);
            Assert.Equal(fieldName, refusal.FieldName);
            Assert.NotEmpty(refusal.Reason);
        }

        byte[] repaired = LoadEditSave(original, editor =>
        {
            XmpPacket packet = editor.Metadata.Xmp;
            XmpExtensionSchemaStructureRepairReport report =
                XmpConformance.RepairExtensionSchemaStructure(packet);
            Assert.Equal(preview.AppliedCount, report.AppliedCount);
            Assert.Equal(preview.Refused.ToArray(), report.Refused.ToArray());
            editor.Metadata.SetRawXmp(packet.Serialize());
        });
        PreflightResult after = Preflighter.Check(repaired, ConformanceProfile.PdfA2b);

        if (safelyRepairable)
        {
            Assert.DoesNotContain(after.Findings, finding => finding.RuleId == RuleId);
            XmpExtensionSchemaStructureRepairReport secondPass = Preview(repaired);
            Assert.Equal(0, secondPass.AppliedCount);
            Assert.Empty(secondPass.Refused);
        }
        else
        {
            Assert.Contains(after.Findings, finding => finding.RuleId == RuleId);
            XmpExtensionSchemaStructureRepairReport secondPass = Preview(repaired);
            Assert.Equal(0, secondPass.AppliedCount);
            Assert.Single(secondPass.Refused);
        }

        AssertNoNewRuleIds(before, after);
        string savedXmp = ReadPacketText(repaired);
        string? missingValue = MissingValue(level, fieldName);
        foreach (string semanticValue in SemanticValues.Where(value => value != missingValue))
            Assert.Contains($">{semanticValue}<", savedXmp, StringComparison.Ordinal);
    }

    private static XmpExtensionSchemaStructureRepairReport Preview(byte[] bytes)
    {
        using PdfDocument document = PdfDocument.Load(new MemoryStream(bytes, writable: false));
        using var editor = document.Edit();
        return XmpConformance.PreviewExtensionSchemaStructureRepairs(editor.Metadata.Xmp);
    }

    private static string ReadPacketText(byte[] bytes)
    {
        using PdfDocument document = PdfDocument.Load(new MemoryStream(bytes, writable: false));
        using var editor = document.Edit();
        return Encoding.UTF8.GetString(editor.Metadata.Xmp.Serialize());
    }

    private static byte[] LoadEditSave(byte[] bytes, Action<PdfDocumentEditor>? mutate = null)
    {
        using PdfDocument document = PdfDocument.Load(new MemoryStream(bytes, writable: false));
        using PdfDocumentEditor editor = document.Edit();
        mutate?.Invoke(editor);
        using var output = new MemoryStream();
        editor.Save(output);
        return output.ToArray();
    }

    private static void AssertNoNewRuleIds(PreflightResult before, PreflightResult after)
    {
        HashSet<string> beforeIds = before.Findings.Select(finding => finding.RuleId).ToHashSet();
        List<string> unexpected = after.Findings.Select(finding => finding.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Except(beforeIds)
            .ToList();
        Assert.True(unexpected.Count == 0, $"Repair introduced: {string.Join(", ", unexpected)}");
    }

    private static readonly string[] SemanticValues =
    [
        "http://example.com/custom/", "custom", "Custom schema", "state", "Text", "external",
        "Property description", "MyType", "http://example.com/type/", "ct", "Type description",
        "fieldName", "Field description"
    ];

    private static string? MissingValue(string level, string fieldName) => (level, fieldName) switch
    {
        ("schema", "namespaceURI") => "http://example.com/custom/",
        ("schema", "prefix") => "custom",
        ("schema", "schema") => "Custom schema",
        ("property", "name") => "state",
        ("property", "valueType") => "Text",
        ("property", "category") => "external",
        ("property", "description") => "Property description",
        ("value type", "type") => "MyType",
        ("value type", "namespaceURI") => "http://example.com/type/",
        ("value type", "prefix") => "ct",
        ("value type", "description") => "Type description",
        ("field", "name") => "fieldName",
        ("field", "valueType") => "Text",
        ("field", "description") => "Field description",
        _ => null,
    };

    private static string BuildPacket(string level, string? missingField, bool wrongPrefix = false)
    {
        const string schemaNs = "http://www.aiim.org/pdfa/ns/schema#";
        const string propertyNs = "http://www.aiim.org/pdfa/ns/property#";
        const string typeNs = "http://www.aiim.org/pdfa/ns/type#";
        const string fieldNs = "http://www.aiim.org/pdfa/ns/field#";

        string schema = Field("pdfaSchema", "namespaceURI", "http://example.com/custom/")
                        + Field("pdfaSchema", "prefix", "custom")
                        + Field("pdfaSchema", "schema", "Custom schema");
        string property = Field("pdfaProperty", "name", "state")
                          + Field("pdfaProperty", "valueType", "Text")
                          + Field("pdfaProperty", "category", "external")
                          + Field("pdfaProperty", "description", "Property description");
        string valueType = Field("pdfaType", "type", "MyType")
                           + Field("pdfaType", "namespaceURI", "http://example.com/type/")
                           + Field("pdfaType", "prefix", "ct")
                           + Field("pdfaType", "description", "Type description");
        string field = Field("pdfaField", "name", "fieldName")
                       + Field("pdfaField", "valueType", "Text")
                       + Field("pdfaField", "description", "Field description");

        if (missingField is not null)
        {
            string value = MissingValue(level, missingField)!;
            string prefix = PrefixFor(level);
            string token = Field(prefix, missingField, value);
            switch (level)
            {
                case "schema": schema = schema.Replace(token, string.Empty, StringComparison.Ordinal); break;
                case "property": property = property.Replace(token, string.Empty, StringComparison.Ordinal); break;
                case "value type": valueType = valueType.Replace(token, string.Empty, StringComparison.Ordinal); break;
                case "field": field = field.Replace(token, string.Empty, StringComparison.Ordinal); break;
            }
        }

        if (wrongPrefix)
        {
            string expected = PrefixFor(level);
            string replacement = "wrong" + expected;
            switch (level)
            {
                case "schema": schema = schema.Replace(expected + ":", replacement + ":", StringComparison.Ordinal); break;
                case "property": property = property.Replace(expected + ":", replacement + ":", StringComparison.Ordinal); break;
                case "value type": valueType = valueType.Replace(expected + ":", replacement + ":", StringComparison.Ordinal); break;
                case "field": field = field.Replace(expected + ":", replacement + ":", StringComparison.Ordinal); break;
            }
        }

        string schemaBinding = NamespaceBinding("schema", level, wrongPrefix, schemaNs);
        string propertyBinding = NamespaceBinding("property", level, wrongPrefix, propertyNs);
        string typeBinding = NamespaceBinding("value type", level, wrongPrefix, typeNs);
        string fieldBinding = NamespaceBinding("field", level, wrongPrefix, fieldNs);
        string schemaElementPrefix = ElementPrefix("schema", level, wrongPrefix);
        string typeElementPrefix = ElementPrefix("value type", level, wrongPrefix);
        return $"""
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about=""
                xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
                {schemaBinding} {propertyBinding} {typeBinding} {fieldBinding}>
               <pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType="Resource">
                {schema}
                <{schemaElementPrefix}:property><rdf:Seq><rdf:li rdf:parseType="Resource">{property}</rdf:li></rdf:Seq></{schemaElementPrefix}:property>
                <{schemaElementPrefix}:valueType><rdf:Seq><rdf:li rdf:parseType="Resource">{valueType}
                 <{typeElementPrefix}:field><rdf:Seq><rdf:li rdf:parseType="Resource">{field}</rdf:li></rdf:Seq></{typeElementPrefix}:field>
                </rdf:li></rdf:Seq></{schemaElementPrefix}:valueType>
               </rdf:li></rdf:Bag></pdfaExtension:schemas>
              </rdf:Description>
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
    }

    private static string Field(string prefix, string name, string value) =>
        $"<{prefix}:{name}>{value}</{prefix}:{name}>";

    private static string PrefixFor(string level) => level switch
    {
        "schema" => "pdfaSchema",
        "property" => "pdfaProperty",
        "value type" => "pdfaType",
        "field" => "pdfaField",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static string NamespaceBinding(string bindingLevel, string malformedLevel, bool wrongPrefix, string ns)
    {
        string prefix = PrefixFor(bindingLevel);
        if (wrongPrefix && bindingLevel == malformedLevel)
            prefix = "wrong" + prefix;
        return $"xmlns:{prefix}=\"{ns}\"";
    }

    private static string ElementPrefix(string bindingLevel, string malformedLevel, bool wrongPrefix)
    {
        string prefix = PrefixFor(bindingLevel);
        return wrongPrefix && bindingLevel == malformedLevel ? "wrong" + prefix : prefix;
    }

    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static byte[] DocumentBytes(byte[] xmp)
    {
        using var document = new PdfDocument();
        document.AddObject(10, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("Metadata"), [N("Subtype")] = N("XML") }, xmp));
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(612), new PdfInteger(792)),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        document.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("Metadata")] = Ref(10),
        });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        using PdfDocumentEditor editor = document.Edit();
        using var output = new MemoryStream();
        editor.Save(output);
        return output.ToArray();
    }
}
