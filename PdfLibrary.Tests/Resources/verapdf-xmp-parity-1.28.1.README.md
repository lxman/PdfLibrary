# Regenerating `verapdf-xmp-parity-1.28.1.txt`

This fixture is veraPDF's **effective** PDF/A-2/3 predefined schema definition. `XmpParityTests` pins
PdfLibrary's XMP conformance tables to it.

**Do not hand-edit it.** If a comparison fails, either the engine drifted (fix the engine) or veraPDF
was deliberately bumped (regenerate, and say why in the commit message).

## Why this fixture exists

PdfLibrary's XMP tables are a direct port of veraPDF's `XMPConstants`, and the engine's contract is
veraPDF parity — a strict subset with zero false positives across 1,316 files. The published XMP
Specification is newer than both veraPDF and ISO 19005-2 Annex B (a 2005-era snapshot), so the tables
legitimately disagree with the current spec in at least nine places. Each of those reads as an obvious
bug to anyone holding the specification; the 2026-08-13 audit reported four of them as bugs before the
oracle existed. Changing any one makes the engine raise findings the reference does not.

See `Docs/superpowers/notes/2026-08-13-xmp-standards-audit.md`.

## Procedure

Requires a JDK (any 11+) and the veraPDF jar. Neither is in this repo.

- Jar: `RiderProjects/EInvoice/tools/verapdf/bin/greenfield-apps-1.28.1.jar`
  *(outside this repository — if it has moved, find the matching veraPDF release before regenerating,
  and do not silently substitute a different version)*
- JDK used to produce the committed fixture: `Eclipse Adoptium jdk-21.0.12.8-hotspot`
- Generator: `DumpParity.java` — reproduced at the bottom of this file.

```
javac -cp <jar> -d <workdir> DumpParity.java
java  -cp "<jar>;<workdir>" DumpParity > verapdf-xmp-parity-<version>.txt
```

Then rename the fixture and `FixtureName` in `XmpParityTests` together, so the version in the filename
never disagrees with the jar it came from.

## The trap — read before writing your own generator

**Dumping veraPDF's `String[]` constants is NOT sufficient**, and a partial dump fails *silently* by
reporting absence as evidence. During the audit it produced two false "engine invented this" findings.

Three registrations do not live in any `String[]`:

1. `TIFF_YCBCRSUBSAMPLING_SEQ_CHOICE_COMMON` — a `String[][]`, reached via
   `XMPConstants.getTiffYcbcrsubsamplingSeqChoiceCommon()`.
2. `EXIF_COMPONENTS_CONFIGURATION_CLOSED_SEQ_CHOICE_COMMON` — likewise.
3. Camera Raw `ToneCurve` — registered in `SchemasDefinitionCreator`'s own bytecode via
   `registerRestrictedSeqTextFieldForSchema`, with no constant of its own.

These back `XmpPredefinedSchemas.cs:128,129,145`.

`DumpParity.java` avoids the whole class of problem by **calling veraPDF's builder** —
`SchemasDefinitionCreator.getPredefinedSchemaDefinitionForPDFA_2_3(false)` — and reading the assembled
map, rather than reassembling the constants. Any generator you write should do the same.

The `false` argument is **no-closed-choice mode**, which is what PdfLibrary ports (restricted struct
fields fold to their permissive base type). Passing `true` produces a different, wrong fixture.

## What it does and does not pin

Pins, both directions: the predefined property map (namespace, local name, type) and every structured
value type (child namespace + field name/type).

Does **not** pin the simple-type regexes. The engine stores validators as `Func<XmpNode,bool>`, so a
registered pattern is not recoverable from the container. The audit verified them by decompiling
`SimpleTypeValidator$SimpleTypeEnum` (`real`, `boolean`, `integer`, `mimetype` all
character-for-character identical), but that comparison is not automated. `XmpParityTests` pins the
type-name set only; the regexes are a known unpinned residual.

Does not pin veraPDF itself — the fixture is version-stamped, and a veraPDF bump is a deliberate act.

## `DumpParity.java`

```java
import java.lang.reflect.*;
import java.util.*;
import java.util.regex.Pattern;
import javax.xml.namespace.QName;

public class DumpParity {

    public static void main(String[] args) throws Exception {
        boolean closedChoice = false; // PdfLibrary ports the "no closed-choice" mode.

        Class<?> creator = Class.forName("org.verapdf.model.tools.xmp.SchemasDefinitionCreator");
        Method get = creator.getMethod("getPredefinedSchemaDefinitionForPDFA_2_3", boolean.class);
        Object def = get.invoke(null, closedChoice);

        StringBuilder out = new StringBuilder();
        out.append("# veraPDF predefined schema definition - PDF/A-2/3, closedChoice=")
           .append(closedChoice).append('\n');
        out.append("# Generated from SchemasDefinitionCreator.getPredefinedSchemaDefinitionForPDFA_2_3.\n");
        out.append("# Canonical + sorted; regenerate rather than hand-edit.\n");

        Map<QName, String> props = readField(def, "org.verapdf.model.tools.xmp.SchemasDefinition",
                                             "properties");
        List<String> propLines = new ArrayList<>();
        for (Map.Entry<QName, String> e : props.entrySet()) {
            propLines.add(e.getKey().getNamespaceURI() + '\t' + e.getKey().getLocalPart()
                          + '\t' + e.getValue());
        }
        Collections.sort(propLines);
        out.append("\n[properties] count=").append(propLines.size()).append('\n');
        for (String s : propLines) out.append(s).append('\n');

        Object container = def.getClass().getMethod("getValidatorsContainer").invoke(def);
        Map<String, Object> validators = readField(container,
                "org.verapdf.model.tools.xmp.ValidatorsContainer", "validators");

        List<String> structLines = new ArrayList<>();
        List<String> simpleLines = new ArrayList<>();
        for (Map.Entry<String, Object> e : new TreeMap<>(validators).entrySet()) {
            Object v = e.getValue();
            Map<String, String> fields = tryFields(v);
            if (fields != null) {
                String childNs = tryString(v);
                List<String> fs = new ArrayList<>();
                for (Map.Entry<String, String> f : new TreeMap<>(fields).entrySet())
                    fs.add(f.getKey() + '=' + f.getValue());
                structLines.add(e.getKey() + '\t' + childNs + '\t' + String.join(";", fs));
            } else {
                simpleLines.add(e.getKey() + '\t' + v.getClass().getSimpleName()
                                + '\t' + describeSimple(v));
            }
        }
        Collections.sort(structLines);
        Collections.sort(simpleLines);

        out.append("\n[structured-types] count=").append(structLines.size()).append('\n');
        for (String s : structLines) out.append(s).append('\n');
        out.append("\n[simple-types] count=").append(simpleLines.size()).append('\n');
        for (String s : simpleLines) out.append(s).append('\n');

        System.out.print(out);
    }

    @SuppressWarnings("unchecked")
    private static <T> T readField(Object target, String declaringClass, String name)
            throws Exception {
        Field f = Class.forName(declaringClass).getDeclaredField(name);
        f.setAccessible(true);
        return (T) f.get(target);
    }

    @SuppressWarnings("unchecked")
    private static Map<String, String> tryFields(Object v) {
        for (Field f : v.getClass().getDeclaredFields()) {
            if (!Map.class.isAssignableFrom(f.getType())) continue;
            f.setAccessible(true);
            try {
                Map<?, ?> m = (Map<?, ?>) f.get(v);
                if (m == null || m.isEmpty()) continue;
                Object k = m.keySet().iterator().next();
                Object val = m.values().iterator().next();
                if (k instanceof String && val instanceof String) return (Map<String, String>) m;
            } catch (IllegalAccessException ignored) { }
        }
        return null;
    }

    private static String tryString(Object v) {
        for (Field f : v.getClass().getDeclaredFields()) {
            if (f.getType() != String.class) continue;
            f.setAccessible(true);
            try {
                Object s = f.get(v);
                if (s != null) return (String) s;
            } catch (IllegalAccessException ignored) { }
        }
        return "-";
    }

    private static String describeSimple(Object v) {
        List<String> parts = new ArrayList<>();
        for (Field f : v.getClass().getDeclaredFields()) {
            f.setAccessible(true);
            try {
                Object val = f.get(v);
                if (val instanceof Pattern) parts.add("regex=" + val);
                else if (val instanceof String) parts.add("str=" + val);
            } catch (IllegalAccessException ignored) { }
        }
        return parts.isEmpty() ? "unconstrained" : String.join(",", parts);
    }
}
```
