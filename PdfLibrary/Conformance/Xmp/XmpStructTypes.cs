namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// The XMP structured value-type field tables from ISO 19005-2/-3 Annex B (the same data the reference
/// validator uses). Each <c>*_STRUCTURE</c> array is <c>{childNamespaceURI, fieldName, fieldType, …}</c>
/// (stride 2 after the namespace at index 0). A <c>*_RESTRICTED_FIELD_*</c> array is
/// <c>{fieldName, baseType, closedRegex, …}</c> (stride 3, no namespace) — in the "no closed-choice"
/// mode PdfLibrary builds, only <c>fieldName</c> + <c>baseType</c> are used and the regex is ignored, so a
/// restricted field degrades to its permissive base type. This is factual schema data, reproduced (not
/// copied from any implementation) so PdfLibrary's verdict tracks the reference.
/// </summary>
internal static class XmpStructTypes
{
    // ── Basic value types (shared by PDF/A-1, -2, -3) ───────────────────────────────────────────
    internal static readonly string[] Dimensions =
        { "http://ns.adobe.com/xap/1.0/sType/Dimensions#", "w", "real", "h", "real", "unit", "text" };

    internal static readonly string[] ThumbnailBase =
        { "http://ns.adobe.com/xap/1.0/g/img/", "height", "integer", "width", "integer", "image", "text" };
    internal static readonly string[] ThumbnailRestricted = { "format", "text", "^JPEG$" };

    internal static readonly string[] ResourceEvent =
        { "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#", "action", "text", "instanceID", "uri", "parameters", "text", "softwareAgent", "agentname", "when", "date" };

    internal static readonly string[] ResourceRef =
        { "http://ns.adobe.com/xap/1.0/sType/ResourceRef#", "instanceID", "uri", "documentID", "uri", "versionID", "text", "renditionClass", "renditionclass", "renditionParams", "text", "manager", "agentname", "managerVariant", "text", "manageTo", "uri", "manageUI", "uri" };

    internal static readonly string[] Version =
        { "http://ns.adobe.com/xap/1.0/sType/Version#", "comments", "text", "event", "resourceevent", "modifyDate", "date", "modifier", "propername", "version", "text" };

    internal static readonly string[] Job =
        { "http://ns.adobe.com/xap/1.0/sType/Job#", "name", "text", "id", "text", "url", "url" };

    internal static readonly string[] FlashBase =
        { "http://ns.adobe.com/exif/1.0/", "Fired", "boolean", "Function", "boolean", "RedEyeMode", "boolean" };
    internal static readonly string[] FlashRestricted = { "Return", "text", "^[023]$", "Mode", "text", "^[0-3]$" };

    internal static readonly string[] OecfSfr =
        { "http://ns.adobe.com/exif/1.0/", "Columns", "integer", "Rows", "integer", "Names", "seq text", "Values", "seq rational" };

    internal static readonly string[] CfaPattern =
        { "http://ns.adobe.com/exif/1.0/", "Columns", "integer", "Rows", "integer", "Values", "seq integer" };

    internal static readonly string[] DeviceSettings =
        { "http://ns.adobe.com/exif/1.0/", "Columns", "integer", "Rows", "integer", "Settings", "seq text" };

    // ── PDF/A-2 and -3 additional value types ───────────────────────────────────────────────────
    internal static readonly string[] ColorantBase =
        { "http://ns.adobe.com/xap/1.0/g/", "swatchName", "text" };
    internal static readonly string[] ColorantRestricted =
        { "mode", "text", "^(CMYK|RGB|LAB)$", "type", "text", "^(PROCESS|SPOT)$", "cyan", "real", "^[+]?(\\d{1,2}(\\.\\d*)?|\\d{0,2}\\.\\d+|100(\\.0*)?)$", "magenta", "real", "^[+]?(\\d{1,2}(\\.\\d*)?|\\d{0,2}\\.\\d+|100(\\.0*)?)$", "yellow", "real", "^[+]?(\\d{1,2}(\\.\\d*)?|\\d{0,2}\\.\\d+|100(\\.0*)?)$", "black", "real", "^[+]?(\\d{1,2}(\\.\\d*)?|\\d{0,2}\\.\\d+|100(\\.0*)?)$", "red", "integer", "^[+]?([01]?[0-9]{1,2}|2[0-4][0-9]|25[0-5])$", "green", "integer", "^[+]?([01]?[0-9]{1,2}|2[0-4][0-9]|25[0-5])$", "blue", "integer", "^[+]?([01]?[0-9]{1,2}|2[0-4][0-9]|25[0-5])$", "L", "real", "^[+]?(\\d{1,2}(\\.\\d*)?|\\d{0,2}\\.\\d+|100(\\.0*)?)$", "A", "integer", "^([+-]?[0]?[0-9]{1,2}|[+-]?1[01][0-9]|[+-]?12[0-7]|-128)$", "B", "integer", "^([+-]?[0]?[0-9]{1,2}|[+-]?1[01][0-9]|[+-]?12[0-7]|-128)$" };

    internal static readonly string[] Font =
        { "http://ns.adobe.com/xap/1.0/sType/Font#", "fontName", "text", "fontFamily", "text", "fontFace", "text", "fontType", "text", "versionString", "text", "composite", "boolean", "fontFileName", "text", "childFontFiles", "seq text" };

    internal static readonly string[] BeatSpliceStretch =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "useFileBeatsMarker", "boolean", "riseInDecibel", "real", "riseInTimeDuration", "time" };

    internal static readonly string[] MarkerBase =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "startTime", "any", "duration", "any", "comment", "text", "name", "text", "location", "uri", "target", "text" };
    internal static readonly string[] MarkerRestricted = { "type", "text", "^(Chapter|Cue|Beat|Track|Index)$" };

    internal static readonly string[] Media =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "path", "uri", "track", "text", "startTime", "time", "duration", "time", "managed", "boolean", "webStatement", "uri" };

    internal static readonly string[] ProjectLinkBase =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "path", "uri" };
    internal static readonly string[] ProjectLinkRestricted = { "type", "text", "^(movie|still|audio|custom)$" };

    internal static readonly string[] ResampleStretchBase =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/" };
    internal static readonly string[] ResampleStretchRestricted = { "quality", "text", "^(High|Medium|Low)$" };

    internal static readonly string[] Time =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "value", "integer", "scale", "rational" };

    internal static readonly string[] TimecodeBase =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/" };
    internal static readonly string[] TimecodeRestricted =
        { "timeValue", "text", "^\\d{2}((:\\d{2}){3}|(;\\d{2}){3})$", "timeFormat", "text", "^(24|25|2997Drop|2997NonDrop|30|50|5994Drop|5994NonDrop|60|23976)(Timecode)$" };

    internal static readonly string[] TimeScaleStretchBase =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "frameSize", "real", "frameOverlappingPercentage", "real" };
    internal static readonly string[] TimeScaleStretchRestricted = { "quality", "text", "^(High|Medium|Low)$" };
}
