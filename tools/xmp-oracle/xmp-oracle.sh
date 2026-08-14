#!/usr/bin/env bash
#
# Ask Adobe's XMPCore what it makes of an XMP packet.
#
# Fetches the jar on first use, compiles the driver when it is out of date, then runs it.
# Nothing here is a build dependency of PdfLibrary — see README.md.
#
# Usage:
#   tools/xmp-oracle/xmp-oracle.sh <file.xmp>              # properties + shape flags
#   tools/xmp-oracle/xmp-oracle.sh --serialize <file.xmp>  # ...and Adobe's own re-serialization
#   echo '<x:xmpmeta …>' | tools/xmp-oracle/xmp-oracle.sh -
#
# A packet Adobe REFUSES prints "PARSE-ERROR<TAB>reason" and exits 0: a refusal is an answer, not a
# tool failure. This engine is deliberately tolerant where Adobe throws, so the disagreement itself
# is often the finding.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="6.1.11"
JAR="$HERE/lib/xmpcore-$VERSION.jar"
URL="https://repo1.maven.org/maven2/com/adobe/xmp/xmpcore/$VERSION/xmpcore-$VERSION.jar"

command -v java >/dev/null 2>&1 || { echo "xmp-oracle: java not found on PATH" >&2; exit 1; }

if [ ! -f "$JAR" ]; then
    echo "xmp-oracle: fetching xmpcore-$VERSION.jar from Maven Central…" >&2
    mkdir -p "$HERE/lib"
    command -v curl >/dev/null 2>&1 || { echo "xmp-oracle: curl not found; download $URL to $JAR" >&2; exit 1; }
    curl -fsS -o "$JAR" "$URL"
fi

# Recompile when the source is newer than the class, so an edit cannot be silently ignored.
if [ ! -f "$HERE/out/XmpOracle.class" ] || [ "$HERE/XmpOracle.java" -nt "$HERE/out/XmpOracle.class" ]; then
    command -v javac >/dev/null 2>&1 || { echo "xmp-oracle: javac not found (a JRE is not enough)" >&2; exit 1; }
    javac -cp "$JAR" -d "$HERE/out" "$HERE/XmpOracle.java"
fi

# A Windows JVM under Git Bash needs BOTH halves of this: ';' as the classpath separator, and native
# paths. The separator alone is not enough — a Windows java cannot resolve `/c/Users/...` at all, and
# the failure is the unhelpful "Could not find or load main class" rather than anything about paths.
CP_JAR="$JAR"
CP_OUT="$HERE/out"
SEP=":"
if java -XshowSettings:properties -version 2>&1 | grep -q 'java.home = [A-Za-z]:\\'; then
    SEP=";"
    if command -v cygpath >/dev/null 2>&1; then
        CP_JAR="$(cygpath -w "$JAR")"
        CP_OUT="$(cygpath -w "$HERE/out")"
    fi
fi

exec java -cp "$CP_JAR$SEP$CP_OUT" XmpOracle "$@"
