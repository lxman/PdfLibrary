#!/usr/bin/env bash
#
# capture.sh — regenerate the veraPDF verdict snapshot (the committed "answer key").
#
# Runs ONLY on the designated capture machine (the Mac; see README.md). Drives the
# veraPDF CLI over each corpus profile folder, then parses the MRR reports into
# PdfLibrary.Tests/Conformance/parity/verapdf-verdicts.json. Re-run only when the
# veraPDF version or the corpus changes — otherwise the committed snapshot stands.
#
# Requirements on the capture host:
#   - veraPDF CLI on PATH            (brew install verapdf — pins the 1.30.2 bottle)
#   - a Java 11–17 runtime           (veraPDF's JAXB profile loader misbehaves on JDK 21+;
#                                      a pinned Temurin 17 JRE under ./jdk17 is used if present)
#   - the veraPDF-corpus checkout    (sibling ../veraPDF-corpus, or set VERAPDF_CORPUS)
#   - .NET SDK 10                    (to run the MRR->JSON parser)
#
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
CORPUS="${VERAPDF_CORPUS:-$(cd "$REPO_ROOT/.." && pwd)/veraPDF-corpus}"
OUT_DIR="$HERE/out"
SNAPSHOT="$REPO_ROOT/PdfLibrary.Tests/Conformance/parity/verapdf-verdicts.json"

# Pin Java 17 if the bundled JRE is present (keeps output warning-free + reproducible).
if [ -x "$HERE/jdk17/Contents/Home/bin/java" ]; then
  export JAVA_HOME="$HERE/jdk17/Contents/Home"
  echo "using pinned JAVA_HOME=$JAVA_HOME"
fi

command -v verapdf >/dev/null 2>&1 || { echo "error: verapdf not on PATH (brew install verapdf)"; exit 1; }
[ -d "$CORPUS" ] || { echo "error: corpus not found at $CORPUS (set VERAPDF_CORPUS)"; exit 1; }

# veraPDF flavour code -> corpus profile folder. A case function keeps this portable to
# macOS's stock bash 3.2 (no associative arrays).
FLAVOURS="2b 2u 3b ua1"
folder_for() {
  case "$1" in
    2b)  echo "PDF_A-2b" ;;
    2u)  echo "PDF_A-2u" ;;
    3b)  echo "PDF_A-3b" ;;
    ua1) echo "PDF_UA-1" ;;
  esac
}

mkdir -p "$OUT_DIR" "$(dirname "$SNAPSHOT")"
echo "corpus:   $CORPUS"
echo "verapdf:  $(command -v verapdf)  ($(verapdf --version 2>/dev/null | grep -i '^veraPDF' | head -1))"
echo

for f in $FLAVOURS; do
  folder="$(folder_for "$f")"
  dir="$CORPUS/$folder"
  if [ ! -d "$dir" ]; then echo "warn: missing $dir — skipping $f"; continue; fi
  echo ">>> validating $f over $folder ..."
  # veraPDF exits non-zero when any file is non-compliant — expected, so ignore it.
  # --maxfailures -1 keeps every failed rule in the report (needed for the coverage matrix).
  verapdf -f "$f" --format mrr --maxfailures -1 --recurse "$dir" > "$OUT_DIR/$f.mrr.xml" 2>/dev/null || true
  echo "    wrote $OUT_DIR/$f.mrr.xml ($(wc -c < "$OUT_DIR/$f.mrr.xml" | tr -d ' ') bytes)"
done

CORPUS_COMMIT="$(git -C "$CORPUS" rev-parse --short HEAD 2>/dev/null || echo unknown)"
echo
echo ">>> parsing MRR -> $SNAPSHOT (corpus @ $CORPUS_COMMIT)"
dotnet run --project "$HERE/MrrToVerdicts/MrrToVerdicts.csproj" -c Release -- \
  "$OUT_DIR" "$SNAPSHOT" "$CORPUS_COMMIT"

echo
echo "done. review the snapshot diff before committing:"
echo "  git -C $REPO_ROOT diff --stat -- PdfLibrary.Tests/Conformance/parity/"
