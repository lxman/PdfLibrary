import com.adobe.internal.xmp.XMPConst;
import com.adobe.internal.xmp.XMPException;
import com.adobe.internal.xmp.XMPIterator;
import com.adobe.internal.xmp.XMPMeta;
import com.adobe.internal.xmp.XMPMetaFactory;
import com.adobe.internal.xmp.options.PropertyOptions;
import com.adobe.internal.xmp.options.SerializeOptions;
import com.adobe.internal.xmp.properties.XMPPropertyInfo;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * Reports what Adobe's XMPCore makes of an XMP packet: the properties it surfaces, the shape flags
 * it assigns, and how it re-serializes what it read.
 *
 * <p>An ORACLE, not a dependency. Nothing in PdfLibrary links against this; it answers questions the
 * corpus cannot, for round-trip shapes no document we hold happens to contain. See README.md for why
 * this particular jar and not the C++ toolkit.
 *
 * <p>Usage:  java -cp lib/xmpcore-6.1.11.jar:. XmpOracle [--serialize] &lt;file.xmp | -&gt;
 */
public final class XmpOracle {

    public static void main(String[] args) throws IOException {
        boolean serialize = false;
        String source = null;
        for (String arg : args) {
            if ("--serialize".equals(arg)) serialize = true;
            else source = arg;
        }
        if (source == null) {
            System.err.println("usage: XmpOracle [--serialize] <file.xmp | ->");
            System.exit(2);
        }

        byte[] packet = "-".equals(source) ? readAll(System.in) : Files.readAllBytes(Path.of(source));

        XMPMeta meta;
        try {
            meta = XMPMetaFactory.parseFromBuffer(packet);
        } catch (XMPException ex) {
            // Adobe's parser THROWS where this engine is deliberately tolerant (an unparseable packet
            // yields an empty property list, never an exception — the no-false-positive contract).
            // A throw here is therefore a real answer to "is this packet well-formed XMP?", not a
            // tool failure, so it is reported on stdout in the normal format rather than as a crash.
            System.out.println("PARSE-ERROR\t" + ex.getMessage());
            return;
        }

        for (String line : describe(meta)) System.out.println(line);

        if (serialize) {
            System.out.println("--- re-serialized by Adobe XMPCore ---");
            try {
                System.out.println(XMPMetaFactory.serializeToString(meta, new SerializeOptions()
                        .setOmitPacketWrapper(true)
                        .setUseCompactFormat(false)
                        .setIndent("  ")));
            } catch (XMPException ex) {
                System.out.println("SERIALIZE-ERROR\t" + ex.getMessage());
            }
        }
    }

    /**
     * One tab-separated line per property: path, value, then the shape flags that carry meaning for
     * round-trip questions. Schema nodes (the namespace groupings XMPIterator emits) are skipped —
     * they are an artefact of the iterator, not properties of the document.
     */
    private static List<String> describe(XMPMeta meta) {
        var lines = new ArrayList<String>();
        try {
            for (XMPIterator it = meta.iterator(); it.hasNext(); ) {
                var info = (XMPPropertyInfo) it.next();
                PropertyOptions opts = info.getOptions();
                if (opts.isSchemaNode()) continue;

                lines.add(String.join("\t",
                        info.getPath(),
                        "value=" + quote(info.getValue()),
                        flags(opts)));
            }
        } catch (XMPException ex) {
            lines.add("ITERATE-ERROR\t" + ex.getMessage());
        }
        if (lines.isEmpty()) lines.add("(no properties)");
        return lines;
    }

    /** Only the flags that have ever mattered to a round-trip question, so a diff stays readable. */
    private static String flags(PropertyOptions o) {
        var on = new ArrayList<String>();
        if (o.isURI()) on.add("URI");                       // rdf:resource form (our IsUriValue)
        if (o.isSimple()) on.add("simple");
        if (o.isStruct()) on.add("struct");
        if (o.isArray()) on.add("array");
        if (o.isArrayOrdered()) on.add("ordered");
        if (o.isArrayAlternate()) on.add("alt");
        if (o.isArrayAltText()) on.add("altText");
        if (o.getHasQualifiers()) on.add("hasQualifiers");     // the shape we CAPTURE rather than model
        if (o.isQualifier()) on.add("isQualifier");
        if (o.getHasLanguage()) on.add("xml:lang");
        return on.isEmpty() ? "-" : String.join(",", on);
    }

    private static String quote(String value) {
        return value == null ? "(null)" : "\"" + value.replace("\n", "\\n") + "\"";
    }

    private static byte[] readAll(InputStream in) throws IOException {
        return in.readAllBytes();
    }

    private XmpOracle() { }

    static {
        // Touch XMPConst so an incompatible jar fails loudly at startup rather than mid-report.
        assert XMPConst.NS_DC != null;
    }
}
