// Rewrites a C# XML documentation file so it describes only the assembly's PUBLIC surface.
//
// The C# compiler emits a <member> entry for every documented member regardless of accessibility, so a
// package's .xml file leaks the doc comments of internal types (the 2.5.2 package shipped 86 internal rule
// types this way). This filter reads the built assembly's metadata (System.Reflection.Metadata, no loading)
// and keeps a <member> only when its doc-id resolves to a public type, or to a member NAME that has at
// least one public declaration on a public type. Matching is by name, not overload: an internal overload
// that shares a public name keeps its entry. That gap is accepted (release spec 2026-09-06, D6).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace PdfLibrary.Build;

public static class DocFilter
{
    /// <summary>Public type full names (dotted, doc-id style) mapped to their public member names.</summary>
    public sealed class PublicSurface
    {
        public HashSet<string> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> MembersByType { get; } = new(StringComparer.Ordinal);
    }

    public static PublicSurface ReadPublicSurface(Stream assemblyStream)
    {
        var surface = new PublicSurface();
        using var pe = new PEReader(assemblyStream);
        MetadataReader md = pe.GetMetadataReader();

        var fullNames = new Dictionary<TypeDefinitionHandle, string>();
        var isPublic = new Dictionary<TypeDefinitionHandle, bool>();
        foreach (TypeDefinitionHandle h in md.TypeDefinitions)
            Resolve(md, h, fullNames, isPublic);

        foreach (TypeDefinitionHandle h in md.TypeDefinitions)
        {
            if (!isPublic[h]) continue;
            string typeName = fullNames[h];
            surface.Types.Add(typeName);
            var members = new HashSet<string>(StringComparer.Ordinal);
            TypeDefinition td = md.GetTypeDefinition(h);
            foreach (MethodDefinitionHandle mh in td.GetMethods())
            {
                MethodDefinition m = md.GetMethodDefinition(mh);
                if ((m.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
                string name = md.GetString(m.Name);
                members.Add(name == ".ctor" ? "#ctor" : name);
            }
            foreach (FieldDefinitionHandle fh in td.GetFields())
            {
                FieldDefinition f = md.GetFieldDefinition(fh);
                if ((f.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) continue;
                members.Add(md.GetString(f.Name));
            }
            // A property or event is public when any accessor is; the accessors are already in `members`.
            foreach (PropertyDefinitionHandle ph in td.GetProperties())
            {
                string name = md.GetString(md.GetPropertyDefinition(ph).Name);
                if (members.Contains("get_" + name) || members.Contains("set_" + name)) members.Add(name);
            }
            foreach (EventDefinitionHandle eh in td.GetEvents())
            {
                string name = md.GetString(md.GetEventDefinition(eh).Name);
                if (members.Contains("add_" + name) || members.Contains("remove_" + name)) members.Add(name);
            }
            surface.MembersByType[typeName] = members;
        }
        return surface;
    }

    private static void Resolve(MetadataReader md, TypeDefinitionHandle h,
        Dictionary<TypeDefinitionHandle, string> fullNames, Dictionary<TypeDefinitionHandle, bool> isPublic)
    {
        if (fullNames.ContainsKey(h)) return;
        TypeDefinition td = md.GetTypeDefinition(h);
        string name = md.GetString(td.Name);
        TypeAttributes vis = td.Attributes & TypeAttributes.VisibilityMask;
        TypeDefinitionHandle parent = td.GetDeclaringType();
        if (parent.IsNil)
        {
            string ns = md.GetString(td.Namespace);
            fullNames[h] = ns.Length == 0 ? name : ns + "." + name;
            isPublic[h] = vis == TypeAttributes.Public;
        }
        else
        {
            Resolve(md, parent, fullNames, isPublic);
            fullNames[h] = fullNames[parent] + "." + name;   // doc-ids join nested types with '.'
            isPublic[h] = isPublic[parent] && vis == TypeAttributes.NestedPublic;
        }
    }

    public static (XDocument Filtered, int Removed, int Kept) Filter(XDocument doc, PublicSurface surface)
    {
        int removed = 0, kept = 0;
        XElement? members = doc.Root?.Element("members");
        if (members is null) return (doc, 0, 0);
        foreach (XElement member in members.Elements("member").ToList())
        {
            string? id = member.Attribute("name")?.Value;
            if (id is null || IsPublic(id, surface)) { kept++; continue; }
            member.Remove();
            removed++;
        }
        return (doc, removed, kept);
    }

    public static bool IsPublic(string docId, PublicSurface surface)
    {
        if (docId.Length < 2 || docId[1] != ':') return true;   // malformed: leave it alone
        char kind = docId[0];
        string body = docId.Substring(2);
        if (kind == 'N') return true;                           // namespaces carry no code
        int paren = body.IndexOf('(');                          // parameter list (methods, indexers)
        if (paren >= 0) body = body.Substring(0, paren);
        int tilde = body.IndexOf('~');                          // conversion-operator return type
        if (tilde >= 0) body = body.Substring(0, tilde);
        if (kind == 'T') return surface.Types.Contains(body);
        int dot = body.LastIndexOf('.');
        if (dot <= 0) return false;
        string type = body.Substring(0, dot);
        string member = body.Substring(dot + 1);
        int hash = member.LastIndexOf('#');                     // explicit interface implementation: IFace#Method
        if (hash > 0) member = member.Substring(hash + 1);
        return surface.MembersByType.TryGetValue(type, out HashSet<string>? names) && names.Contains(member);
    }

    /// <summary>Filters <paramref name="xmlPath"/> in place. Returns (removed, kept).</summary>
    public static (int Removed, int Kept) FilterFile(string assemblyPath, string xmlPath)
    {
        PublicSurface surface;
        using (FileStream fs = File.OpenRead(assemblyPath)) surface = ReadPublicSurface(fs);
        XDocument doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
        (XDocument filtered, int removed, int kept) = Filter(doc, surface);
        if (removed > 0) filtered.Save(xmlPath);
        return (removed, kept);
    }
}
