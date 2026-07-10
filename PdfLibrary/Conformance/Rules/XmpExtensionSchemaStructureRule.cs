using PdfLibrary.Conformance.Xmp;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A requires a well-formed extension-schema container: every <c>pdfaExtension:schemas</c> entry, and
/// each of its nested property / value-type / field descriptions, must carry the mandatory fields and use
/// the standard namespace prefix (ISO 19005-2, 6.6.2.3.3). This validates the container's <em>structure</em>
/// — complementing 6.6.2.3.1, which validates that packet properties are predefined or extension-declared.
///
/// <para>The required/optional split is exactly veraPDF's: a schema requires namespaceURI, prefix and schema
/// (property and valueType are optional); a property requires name, valueType, category and description; a
/// value type requires type, namespaceURI, prefix and description (its field array is optional); a field
/// requires name, valueType and description. At each level the description fields must use the conventional
/// prefix (pdfaSchema / pdfaProperty / pdfaType / pdfaField). A field that is present but empty is not
/// "missing" — presence is what the clause tests — so only absence and a wrong prefix are reported.</para>
/// </summary>
internal sealed class XmpExtensionSchemaStructureRule : IConformanceRule
{
    private const string ExtensionNs = "http://www.aiim.org/pdfa/ns/extension/";
    private const string SchemaNs = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PropertyNs = "http://www.aiim.org/pdfa/ns/property#";
    private const string TypeNs = "http://www.aiim.org/pdfa/ns/type#";
    private const string FieldNs = "http://www.aiim.org/pdfa/ns/field#";

    public string RuleId => "pdfa-xmp-extension-schema-structure";
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (XmpNode node in context.XmpTree)
        {
            if (node.NamespaceUri != ExtensionNs || node.LocalName != "schemas" || !node.IsArray)
                continue;
            foreach (XmpNode schema in node.Children)
                foreach (Finding finding in ValidateSchema(context, schema))
                    yield return finding;
        }
    }

    private IEnumerable<Finding> ValidateSchema(ConformanceContext context, XmpNode schema)
    {
        if (WrongPrefix(schema, SchemaNs, "pdfaSchema"))
            yield return Error(context, "A PDF/A extension schema description does not use the 'pdfaSchema' namespace prefix.");

        foreach (string required in RequiredSchemaFields)
            if (!HasField(schema, SchemaNs, required))
                yield return Error(context, $"A PDF/A extension schema is missing the required pdfaSchema:{required} entry.");

        if (FindField(schema, SchemaNs, "property") is { IsArray: true } propertyBag)
            foreach (XmpNode property in propertyBag.Children)
                foreach (Finding finding in ValidateProperty(context, property))
                    yield return finding;

        if (FindField(schema, SchemaNs, "valueType") is { IsArray: true } valueTypeBag)
            foreach (XmpNode valueType in valueTypeBag.Children)
                foreach (Finding finding in ValidateValueType(context, valueType))
                    yield return finding;
    }

    private IEnumerable<Finding> ValidateProperty(ConformanceContext context, XmpNode property)
    {
        if (WrongPrefix(property, PropertyNs, "pdfaProperty"))
            yield return Error(context, "A PDF/A extension schema property does not use the 'pdfaProperty' namespace prefix.");

        foreach (string required in RequiredPropertyFields)
            if (!HasField(property, PropertyNs, required))
                yield return Error(context, $"A PDF/A extension schema property is missing the required pdfaProperty:{required} entry.");
    }

    private IEnumerable<Finding> ValidateValueType(ConformanceContext context, XmpNode valueType)
    {
        if (WrongPrefix(valueType, TypeNs, "pdfaType"))
            yield return Error(context, "A PDF/A extension schema value type does not use the 'pdfaType' namespace prefix.");

        foreach (string required in RequiredValueTypeFields)
            if (!HasField(valueType, TypeNs, required))
                yield return Error(context, $"A PDF/A extension schema value type is missing the required pdfaType:{required} entry.");

        if (FindField(valueType, TypeNs, "field") is { IsArray: true } fieldBag)
            foreach (XmpNode field in fieldBag.Children)
                foreach (Finding finding in ValidateField(context, field))
                    yield return finding;
    }

    private IEnumerable<Finding> ValidateField(ConformanceContext context, XmpNode field)
    {
        if (WrongPrefix(field, FieldNs, "pdfaField"))
            yield return Error(context, "A PDF/A extension schema field does not use the 'pdfaField' namespace prefix.");

        foreach (string required in RequiredFieldFields)
            if (!HasField(field, FieldNs, required))
                yield return Error(context, $"A PDF/A extension schema field is missing the required pdfaField:{required} entry.");
    }

    private static readonly string[] RequiredSchemaFields = ["namespaceURI", "prefix", "schema"];
    private static readonly string[] RequiredPropertyFields = ["name", "valueType", "category", "description"];
    private static readonly string[] RequiredValueTypeFields = ["type", "namespaceURI", "prefix", "description"];
    private static readonly string[] RequiredFieldFields = ["name", "valueType", "description"];

    private static bool HasField(XmpNode node, string ns, string name)
    {
        foreach (XmpNode child in node.Children)
            if (child.NamespaceUri == ns && child.LocalName == name)
                return true;
        return false;
    }

    private static XmpNode? FindField(XmpNode node, string ns, string name)
    {
        foreach (XmpNode child in node.Children)
            if (child.NamespaceUri == ns && child.LocalName == name)
                return child;
        return null;
    }

    // The description fields of a level all share one namespace binding; any field in that namespace
    // carrying a prefix other than the conventional one means the container used the wrong prefix.
    private static bool WrongPrefix(XmpNode node, string ns, string expectedPrefix)
    {
        foreach (XmpNode child in node.Children)
            if (child.NamespaceUri == ns && child.Prefix != expectedPrefix)
                return true;
        return false;
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.6.2.3.3"),
        Message = message,
    };
}
