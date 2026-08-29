using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>One external-file stream whose payload is already embedded in the document and can be
/// internalized without consulting the host file system or a network resource.</summary>
public sealed record StreamExternalFileRepairCandidate(
    int ObjectNumber,
    int? EmbeddedFileObjectNumber,
    IReadOnlyList<string> RemovedKeys);

/// <summary>One stream-external-file finding outside the deliberately narrow repair boundary.</summary>
public sealed record StreamExternalFileRefusal(int ObjectNumber, string Reason);

/// <summary>Read-only classification of every stream carrying /F, /FFilter, or /FDecodeParms.</summary>
public sealed record StreamExternalFileRepairPreview(
    IReadOnlyList<StreamExternalFileRepairCandidate> Candidates,
    IReadOnlyList<StreamExternalFileRefusal> Refused);

/// <summary>One stream whose external-file keys were removed. A null embedded-file object means the
/// removed keys were orphan /FFilter and/or /FDecodeParms entries and no payload bytes changed.</summary>
public sealed record StreamExternalFileRepair(
    int ObjectNumber,
    int? EmbeddedFileObjectNumber,
    IReadOnlyList<string> RemovedKeys);

/// <summary>What <see cref="PdfDocumentEditor.RepairStreamExternalFiles"/> applied and refused.</summary>
public sealed record StreamExternalFileRepairReport(
    IReadOnlyList<StreamExternalFileRepair> Applied,
    IReadOnlyList<StreamExternalFileRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ExternalFileKey = new("F");
    private static readonly PdfName ExternalFilterKey = new("FFilter");
    private static readonly PdfName ExternalDecodeParmsKey = new("FDecodeParms");
    private static readonly PdfName InternalFilterKey = new("Filter");
    private static readonly PdfName InternalDecodeParmsKey = new("DecodeParms");
    private static readonly PdfName FileSpecTypeKey = new("Type");
    private static readonly PdfName EmbeddedFilesKey = new("EF");
    private static readonly PdfName PermissionsKey = new("Perms");
    private static readonly PdfName DocMdpKey = new("DocMDP");
    private static readonly string[] FileNameKeys = ["F", "UF", "DOS", "Mac", "Unix"];

    private sealed record StreamExternalFileClassification(
        PdfStream Stream,
        PdfStream? EmbeddedPayload,
        byte[]? EmbeddedPayloadBytes,
        IReadOnlyList<PdfName> RemovedKeys,
        StreamExternalFileRefusal? Refusal)
    {
        public bool IsCandidate => Refusal is null && RemovedKeys.Count > 0;
    }

    /// <summary>
    /// Classifies every clause 6.1.7.1 external-file-key finding without mutation.
    ///
    /// <para>Orphan /FFilter and /FDecodeParms entries are safe deletions: ISO 32000 applies them only
    /// to file data selected by /F. A real /F is repairable only when it is an indirect /Filespec whose
    /// /EF dictionary maps every represented file-name form to the same indirect embedded-file stream.
    /// In that case the embedded file's decoded bytes are the external file bytes, so they can be copied
    /// into the target stream and /FFilter and /FDecodeParms can be moved verbatim to /Filter and
    /// /DecodeParms. A path, URL, platform-dependent mapping, undecodable embedded stream, signature, or
    /// DocMDP condition refuses rather than causing ambient file/network access or guessing a payload.</para>
    /// </summary>
    public StreamExternalFileRepairPreview PreviewStreamExternalFileRepairs()
    {
        var candidates = new List<StreamExternalFileRepairCandidate>();
        var refused = new List<StreamExternalFileRefusal>();
        foreach (PdfStream stream in EnumerateExternalFileStreams())
        {
            StreamExternalFileClassification classified = ClassifyStreamExternalFile(stream);
            if (classified.IsCandidate)
            {
                candidates.Add(new StreamExternalFileRepairCandidate(
                    stream.ObjectNumber,
                    classified.EmbeddedPayload?.ObjectNumber,
                    [.. classified.RemovedKeys.Select(key => "/" + key.Value)]));
            }
            else if (classified.Refusal is not null)
            {
                refused.Add(classified.Refusal);
            }
        }

        return new StreamExternalFileRepairPreview(candidates, refused);
    }

    /// <summary>Applies the same current-document classification used by the preview, restricted to the
    /// supplied object numbers. The write never reads an external path or URL.</summary>
    public StreamExternalFileRepairReport RepairStreamExternalFiles(IReadOnlySet<int>? objectNumbers = null)
    {
        var applied = new List<StreamExternalFileRepair>();
        var refused = new List<StreamExternalFileRefusal>();

        foreach (PdfStream stream in EnumerateExternalFileStreams())
        {
            if (objectNumbers is not null && !objectNumbers.Contains(stream.ObjectNumber)) continue;

            StreamExternalFileClassification classified = ClassifyStreamExternalFile(stream);
            if (classified.Refusal is not null)
            {
                refused.Add(classified.Refusal);
                continue;
            }
            if (!classified.IsCandidate) continue;

            if (classified.EmbeddedPayloadBytes is not null)
            {
                // /Filter and /DecodeParms describe only the ignored in-object bytes while /F is present.
                // Replace them with the external-file equivalents before activating the copied payload.
                if (stream.Dictionary.TryGetValue(ExternalFilterKey, out PdfObject externalFilter))
                    stream.Dictionary[InternalFilterKey] = externalFilter;
                else
                    stream.Dictionary.Remove(InternalFilterKey);

                if (stream.Dictionary.TryGetValue(ExternalDecodeParmsKey, out PdfObject externalParms))
                    stream.Dictionary[InternalDecodeParmsKey] = externalParms;
                else
                    stream.Dictionary.Remove(InternalDecodeParmsKey);

                stream.Data = classified.EmbeddedPayloadBytes;
            }

            foreach (PdfName key in classified.RemovedKeys)
                stream.Dictionary.Remove(key);

            applied.Add(new StreamExternalFileRepair(
                stream.ObjectNumber,
                classified.EmbeddedPayload?.ObjectNumber,
                [.. classified.RemovedKeys.Select(key => "/" + key.Value)]));
        }

        return new StreamExternalFileRepairReport(applied, refused);
    }

    private IEnumerable<PdfStream> EnumerateExternalFileStreams()
    {
        _document.MaterializeAllObjects();
        foreach (PdfObject value in _document.Objects.Values)
            if (value is PdfStream { IsIndirect: true } stream
                && (stream.Dictionary.ContainsKey(ExternalFileKey)
                    || stream.Dictionary.ContainsKey(ExternalFilterKey)
                    || stream.Dictionary.ContainsKey(ExternalDecodeParmsKey)))
                yield return stream;
    }

    private StreamExternalFileClassification ClassifyStreamExternalFile(PdfStream stream)
    {
        List<PdfName> present =
        [
            .. new[] { ExternalFileKey, ExternalFilterKey, ExternalDecodeParmsKey }
                .Where(stream.Dictionary.ContainsKey)
        ];
        if (present.Count == 0)
            return new StreamExternalFileClassification(stream, null, null, [], null);

        if (HasExternalFileSignatureProtection())
            return RefuseExternalFile(stream,
                "The external-file stream was left unchanged because the document carries a signed "
              + "signature or DocMDP permission. Pellucid performs a full rewrite and does not claim to "
              + "preserve that protection.");

        if (!stream.Dictionary.ContainsKey(ExternalFileKey))
        {
            // With no /F there is no external file data for these two keys to filter or parameterize.
            return new StreamExternalFileClassification(stream, null, null, present, null);
        }

        PdfObject? rawFileSpec = stream.Dictionary.Get("F");
        if (rawFileSpec is not PdfIndirectReference
            || ResolveObject(rawFileSpec) is not PdfDictionary fileSpec
            || ResolveObject(fileSpec.Get("Type")) is not PdfName { Value: "Filespec" })
            return RefuseExternalFile(stream,
                "This stream selects an external file through /F. Pellucid will not read a host path or "
              + "URL during repair, and removing /F would activate the stream's currently ignored bytes "
              + "instead of the external payload.");

        if (ResolveObject(fileSpec.Get("EF")) is not PdfDictionary embeddedFiles)
            return RefuseExternalFile(stream,
                "This stream's /F file specification has no embedded /EF payload. Pellucid will not read "
              + "a host path or URL during repair, and the ignored in-object bytes are not a proven substitute.");

        PdfStream? embeddedPayload = null;
        var representedNames = 0;
        foreach (string keyName in FileNameKeys)
        {
            PdfObject? fileName = ResolveObject(fileSpec.Get(keyName));
            PdfObject? embedded = ResolveObject(embeddedFiles.Get(keyName));
            if (fileName is null && embedded is null) continue;
            representedNames++;

            if (fileName is null || embedded is not PdfStream { IsIndirect: true } candidate)
                return RefuseExternalFile(stream,
                    $"The /F file specification's /{keyName} name and embedded /EF /{keyName} payload do "
                  + "not form a complete indirect pair, so Pellucid cannot prove which file bytes a reader uses.");

            if (embeddedPayload is not null && embeddedPayload.ObjectNumber != candidate.ObjectNumber)
                return RefuseExternalFile(stream,
                    "The /F file specification maps platform or Unicode names to different embedded payloads. "
                  + "Pellucid will not choose one platform's bytes for every reader.");
            embeddedPayload = candidate;
        }

        if (representedNames == 0 || embeddedPayload is null)
            return RefuseExternalFile(stream,
                "The /F file specification's /EF dictionary does not provide a matched embedded payload "
              + "for /F, /UF, /DOS, /Mac, or /Unix.");

        if (embeddedFiles.Keys.Any(key => !FileNameKeys.Contains(key.Value, StringComparer.Ordinal)))
            return RefuseExternalFile(stream,
                "The /F file specification's /EF dictionary contains an unrecognized payload selector, so "
              + "Pellucid cannot prove that every reader selects the same embedded bytes.");

        if (embeddedPayload.ObjectNumber == stream.ObjectNumber)
            return RefuseExternalFile(stream,
                "The stream's embedded-file specification resolves back to the stream itself; Pellucid will "
              + "not internalize a cyclic payload reference.");

        string? embeddedType = (ResolveObject(embeddedPayload.Dictionary.Get("Type")) as PdfName)?.Value;
        if (embeddedType is not null and not "EmbeddedFile")
            return RefuseExternalFile(stream,
                $"The /EF payload is typed /{embeddedType}, not /EmbeddedFile, so it is not a proven file payload.");

        if (embeddedPayload.Dictionary.ContainsKey(ExternalFileKey)
            || embeddedPayload.Dictionary.ContainsKey(ExternalFilterKey)
            || embeddedPayload.Dictionary.ContainsKey(ExternalDecodeParmsKey))
            return RefuseExternalFile(stream,
                "The selected /EF payload is itself external-file-backed. Pellucid will not follow a nested "
              + "external chain or perform ambient file/network access.");

        if (!ExternalFilterCanBecomeInternal(stream, out string? filterReason))
            return RefuseExternalFile(stream, filterReason!);

        byte[] payloadBytes;
        try
        {
            payloadBytes = embeddedPayload.GetDecodedData(_document.Decryptor);
        }
        catch (Exception exception)
        {
            return RefuseExternalFile(stream,
                $"The selected embedded-file payload could not be decoded ({exception.GetType().Name}), so "
              + "Pellucid cannot prove the file bytes to internalize.");
        }

        return new StreamExternalFileClassification(stream, embeddedPayload, payloadBytes, present, null);
    }

    private bool ExternalFilterCanBecomeInternal(PdfStream stream, out string? reason)
    {
        reason = null;
        PdfObject? rawFilter = ResolveObject(stream.Dictionary.Get("FFilter"));
        if (rawFilter is null) return true;

        IReadOnlyList<PdfObject> entries = rawFilter is PdfArray array ? [.. array] : [rawFilter];
        for (var index = 0; index < entries.Count; index++)
        {
            if (ResolveObject(entries[index]) is not PdfName filter)
            {
                reason = "The embedded external payload has a malformed /FFilter entry. Pellucid cannot "
                       + "activate a filter chain whose decoding semantics are not defined.";
                return false;
            }

            PdfObject? parms = ExternalDecodeParmsAt(stream, index);
            bool permitted = PermittedFilters.Contains(filter.Value)
                             || (filter.Value == "Crypt" && IsIdentityCrypt(parms));
            if (permitted) continue;

            reason = $"The embedded external payload uses /{filter.Value} through /FFilter. Internalizing "
                   + "it would create a stream filter PDF/A does not permit, so Pellucid leaves this "
                   + "stream unchanged instead of introducing a new conformance finding.";
            return false;
        }

        return true;
    }

    private PdfObject? ExternalDecodeParmsAt(PdfStream stream, int index)
    {
        PdfObject? raw = ResolveObject(stream.Dictionary.Get("FDecodeParms"));
        return raw switch
        {
            PdfArray array => index < array.Count ? ResolveObject(array[index]) : null,
            _ => index == 0 ? raw : null,
        };
    }

    private StreamExternalFileClassification RefuseExternalFile(PdfStream stream, string reason) =>
        new(stream, null, null, [], new StreamExternalFileRefusal(stream.ObjectNumber, reason));

    private bool HasExternalFileSignatureProtection()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        if (context.Resolve(context.Catalog?.Dictionary.Get(PermissionsKey)) is PdfDictionary permissions
            && permissions.ContainsKey(DocMdpKey))
            return true;

        return context.Document.Objects.Values.Any(value => ContainsExternalFileSignature(context, value));
    }

    private static bool ContainsExternalFileSignature(ConformanceContext context, PdfObject? value)
    {
        switch (value)
        {
            case PdfStream stream:
                return ContainsExternalFileSignature(context, stream.Dictionary);
            case PdfDictionary dictionary:
                if (context.ResolveName(dictionary.Get(FileSpecTypeKey)) == "Sig"
                    && (dictionary.Get("ByteRange") is not null || dictionary.Get("Contents") is not null))
                    return true;
                return dictionary.Values.Any(child => ContainsExternalFileSignature(context, child));
            case PdfArray array:
                return array.Any(child => ContainsExternalFileSignature(context, child));
            default:
                // Every indirect object is visited separately. Resolving references here would recurse
                // through ordinary page-tree cycles.
                return false;
        }
    }
}
