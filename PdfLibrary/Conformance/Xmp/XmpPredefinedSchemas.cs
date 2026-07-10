using System.Text.RegularExpressions;

namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// The XMP-2005 / ISO 19005 predefined-schema catalogue for PDF/A-2 and PDF/A-3, backing the
/// clause 6.6.2.3.1 rules (<c>pdfa-xmp-property-predefined</c> and <c>pdfa-xmp-property-type</c>).
///
/// <para>This is the "predefined schemas" definition every conformant PDF/A packet is measured
/// against: the set of (namespace URI, property name) pairs the standard blesses, plus the value
/// type each is required to carry. The (namespace, property, type) data is functional schema data
/// taken from ISO 19005-2/-3 Annex B — the same tables veraPDF uses — reproduced here so Focal's
/// verdict tracks the reference validator.</para>
///
/// <para>The map is built exactly the way the reference builds its PDF/A-2/3 predefined definition
/// (basic schemas first, then the -2/-3 additions), in the "no closed-choice" mode: restricted
/// simple fields register under their <em>base</em> type (e.g. an EXIF integer-choice field is a
/// plain <c>integer</c>). That is deliberately the most permissive interpretation of each field, so
/// the type check here can only ever accept a superset of what the reference accepts — it never
/// rejects a value the reference would pass. The tighter closed-choice regexes are intentionally
/// NOT applied.</para>
/// </summary>
internal static class XmpPredefinedSchemas
{
    // ── Basic schemas (shared by PDF/A-1, -2 and -3) ────────────────────────────────────────────
    // Each "structure" array is {namespaceURI, name, type, name, type, ...} (stride 2 after ns).

    private static readonly string[] PdfaIdentificationCommon =
        { "http://www.aiim.org/pdfa/ns/id/", "part", "integer", "amd", "text" };

    private static readonly string[] DublinCoreCommon =
        { "http://purl.org/dc/elements/1.1/", "contributor", "bag propername", "coverage", "text", "creator", "seq propername", "date", "seq date", "description", "lang alt", "format", "mimetype", "identifier", "text", "language", "bag locale", "publisher", "bag propername", "relation", "bag text", "rights", "lang alt", "source", "text", "subject", "bag text", "title", "lang alt", "type", "bag text" };

    private static readonly string[] XmpBasicCommon =
        { "http://ns.adobe.com/xap/1.0/", "Advisory", "bag text", "BaseURL", "url", "CreateDate", "date", "CreatorTool", "agentname", "Identifier", "bag text", "MetadataDate", "date", "ModifyDate", "date", "Nickname", "text", "Thumbnails", "alt thumbnail" };

    private static readonly string[] XmpRightsCommon =
        { "http://ns.adobe.com/xap/1.0/rights/", "Certificate", "url", "Marked", "boolean", "Owner", "bag propername", "UsageTerms", "lang alt", "WebStatement", "url" };

    private static readonly string[] XmpMediaManagementCommon =
        { "http://ns.adobe.com/xap/1.0/mm/", "DerivedFrom", "resourceref", "DocumentID", "uri", "History", "seq resourceevent", "InstanceID", "uri", "ManagedFrom", "resourceref", "Manager", "agentname", "ManageTo", "uri", "ManageUI", "uri", "ManagerVariant", "text", "RenditionClass", "renditionclass", "RenditionParams", "text", "VersionID", "text", "Versions", "seq version", "LastURL", "url", "RenditionOf", "resourceref", "SaveID", "integer" };

    private static readonly string[] XmpBasicJobCommon =
        { "http://ns.adobe.com/xap/1.0/bj/", "JobRef", "bag job" };

    private static readonly string[] XmpPagedTextCommon =
        { "http://ns.adobe.com/xap/1.0/t/pg/", "MaxPageSize", "dimensions", "NPages", "integer" };

    private static readonly string[] AdobePdfCommon =
        { "http://ns.adobe.com/pdf/1.3/", "Keywords", "text", "PDFVersion", "text", "Producer", "agentname" };

    private static readonly string[] PhotoshopCommon =
        { "http://ns.adobe.com/photoshop/1.0/", "AuthorsPosition", "text", "CaptionWriter", "propername", "Category", "text", "City", "text", "Country", "text", "Credit", "text", "DateCreated", "date", "Headline", "text", "Instructions", "text", "Source", "text", "State", "text", "TransmissionReference", "text", "Urgency", "integer" };

    private static readonly string[] TiffWithoutRestrictedFieldCommon =
        { "http://ns.adobe.com/tiff/1.0/", "ImageWidth", "integer", "ImageLength", "integer", "BitsPerSample", "seq integer", "SamplesPerPixel", "integer", "XResolution", "rational", "YResolution", "rational", "TransferFunction", "seq integer", "WhitePoint", "seq rational", "PrimaryChromaticities", "seq rational", "YCbCrCoefficients", "seq rational", "ReferenceBlackWhite", "seq rational", "DateTime", "date", "ImageDescription", "lang alt", "Make", "propername", "Model", "propername", "Software", "agentname", "Artist", "propername", "Copyright", "lang alt" };

    // Restricted simple fields: {namespaceURI, name, baseType, closedRegex, ...} (stride 3 after ns).
    // In the "no closed-choice" mode only name + baseType are used; the regex is intentionally ignored.
    private static readonly string[] TiffRestrictedFieldCommon =
        { "http://ns.adobe.com/tiff/1.0/", "Compression", "integer", "^[16]$", "PhotometricInterpretation", "integer", "^[26]$", "Orientation", "integer", "^[1-8]$", "PlanarConfiguration", "integer", "^[12]$", "YCbCrPositioning", "integer", "^[12]$", "ResolutionUnit", "integer", "^[23]$" };

    private static readonly string[] ExifWithoutRestrictedFieldCommon =
        { "http://ns.adobe.com/exif/1.0/", "CompressedBitsPerPixel", "rational", "PixelXDimension", "integer", "PixelYDimension", "integer", "UserComment", "lang alt", "RelatedSoundFile", "text", "DateTimeOriginal", "date", "DateTimeDigitized", "date", "ExposureTime", "rational", "FNumber", "rational", "SpectralSensitivity", "text", "ISOSpeedRatings", "seq integer", "OECF", "oecf/sfr", "ShutterSpeedValue", "rational", "ApertureValue", "rational", "BrightnessValue", "rational", "ExposureBiasValue", "rational", "MaxApertureValue", "rational", "SubjectDistance", "rational", "Flash", "flash", "FocalLength", "rational", "SubjectArea", "seq integer", "FlashEnergy", "rational", "SpatialFrequencyResponse", "oecf/sfr", "FocalPlaneXResolution", "rational", "FocalPlaneYResolution", "rational", "SubjectLocation", "seq integer", "ExposureIndex", "rational", "CFAPattern", "cfapattern", "DigitalZoomRatio", "rational", "FocalLengthIn35mmFilm", "integer", "DeviceSettingDescription", "devicesettings", "ImageUniqueID", "text", "GPSVersionID", "text", "GPSLatitude", "gpscoordinate", "GPSLongitude", "gpscoordinate", "GPSAltitude", "rational", "GPSTimeStamp", "date", "GPSSatellites", "text", "GPSDOP", "rational", "GPSSpeed", "rational", "GPSTrack", "rational", "GPSImgDirection", "rational", "GPSMapDatum", "text", "GPSDestLatitude", "gpscoordinate", "GPSDestLongitude", "gpscoordinate", "GPSDestBearing", "rational", "GPSDestDistance", "rational", "GPSProcessingMethod", "text", "GPSAreaInformation", "text" };

    private static readonly string[] ExifRestrictedFieldCommon =
        { "http://ns.adobe.com/exif/1.0/", "ExposureProgram", "integer", "^[0-8]$", "MeteringMode", "integer", "^([0-6]|255)$", "FocalPlaneResolutionUnit", "integer", "^[23]$", "SensingMethod", "integer", "^[1-8]$", "FileSource", "integer", "^3$", "SceneType", "integer", "^1$", "CustomRendered", "integer", "^[01]$", "ExposureMode", "integer", "^[0-2]$", "WhiteBalance", "integer", "^[01]$", "SceneCaptureType", "integer", "^[0-3]$", "GainControl", "integer", "^[0-4]$", "Contrast", "integer", "^[0-2]$", "Saturation", "integer", "^[0-2]$", "Sharpness", "integer", "^[0-2]$", "SubjectDistanceRange", "integer", "^[0-3]$", "GPSAltitudeRef", "integer", "^[01]$", "GPSStatus", "text", "^[AV]$", "GPSSpeedRef", "text", "^[KMN]$", "GPSTrackRef", "text", "^[TM]$", "GPSImgDirectionRef", "text", "^[TM]$", "GPSDestBearingRef", "text", "^[TM]$", "GPSDestDistanceRef", "text", "^[KMN]$", "GPSDifferential", "integer", "^[01]$" };

    // ── PDF/A-2 and -3 specific additions ───────────────────────────────────────────────────────

    private static readonly string[] PdfaIdentificationRestrictedFieldDiffer23 =
        { "http://www.aiim.org/pdfa/ns/id/", "conformance", "text", "^[AUB]$" };

    private static readonly string[] PhotoshopDiffer23 =
        { "http://ns.adobe.com/photoshop/1.0/", "SupplementalCategories", "bag text" };

    private static readonly string[] ExifWithoutRestrictedFieldDiffer23 =
        { "http://ns.adobe.com/exif/1.0/", "ExifVersion", "text", "FlashpixVersion", "text", "GPSMeasureMode", "text" };

    private static readonly string[] ExifRestrictedFieldDiffer23 =
        { "http://ns.adobe.com/exif/1.0/", "ColorSpace", "integer", "^1|65535$", "LightSource", "integer", "^[0-4]|9|1[0-5]|1[7-9]|2[0-4]|255$" };

    private static readonly string[] PdfaIdentificationSpecified23 =
        { "http://www.aiim.org/pdfa/ns/id/", "corr", "text" };

    private static readonly string[] XmpBasicSpecified23 =
        { "http://ns.adobe.com/xap/1.0/", "Label", "text", "Rating", "real" };

    private static readonly string[] XmpPagedTextSpecified23 =
        { "http://ns.adobe.com/xap/1.0/t/pg/", "Fonts", "bag font", "Colorants", "seq colorant", "PlateNames", "seq text" };

    private static readonly string[] XmpDynamicMediaWithoutRestrictedFieldSpecified23 =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "projectRef", "projectlink", "videoFrameRate", "text", "videoFrameSize", "dimensions", "videoPixelAspectRatio", "rational", "videoAlphaUnityIsTransparent", "boolean", "videoAlphaPremultipleColor", "colorant", "videoCompressor", "text", "audioSampleRate", "integer", "audioCompressor", "text", "speakerPlacement", "text", "fileDataRate", "rational", "tapeName", "text", "altTapeName", "text", "startTimecode", "timecode", "altTimecode", "timecode", "duration", "time", "scene", "text", "shotName", "text", "shotDate", "date", "shotLocation", "text", "logComment", "text", "markers", "seq marker", "contributedMedia", "bag media", "absPeakAudioFilePath", "uri", "relativePeakAudioFilePath", "uri", "videoModDate", "date", "audioModDate", "date", "metadataModDate", "date", "artist", "text", "album", "text", "trackNumber", "integer", "genre", "text", "copyright", "text", "releaseDate", "date", "composer", "text", "engineer", "text", "tempo", "real", "instrument", "text", "introTime", "time", "outCue", "time", "relativeTimestamp", "time", "loop", "boolean", "numberOfBeats", "real", "timeScaleParams", "timescalestretch", "resampleParams", "resamplestretch", "beatSpliceParams", "beatsplicestretch" };

    private static readonly string[] XmpDynamicMediaRestrictedFieldSpecified23 =
        { "http://ns.adobe.com/xmp/1.0/DynamicMedia/", "videoPixelDepth", "text", "^(8Int|16Int|32Int|32Float)$", "videoColorSpace", "text", "^(sRGB|CCIR-601|CCIR-709)$", "videoAlphaMode", "text", "^(straight|pre-multiplied)$", "videoFieldOrder", "text", "^(Upper|Lower|Progressive)$", "pullDown", "text", "^(WSSW|SSWWW|SWWWS|WWWSS|WWSSW)(_24p)?$", "audioSampleType", "text", "^(8Int|16Int|32Int|32Float)$", "audioChannelType", "text", "^(Mono|Stereo|5\\.1|7\\.1)$", "key", "text", "^([ACDFG]#?|[BE])$", "stretchMode", "text", "^(Fixed length|Time-Scale|Resample|Beat Splice|Hybrid)$", "timeSignature", "text", "^([2-57]/4|[69]/8|12/8|other)$", "scaleType", "text", "^(Major|Minor|Both|Neither)$" };

    private static readonly string[] CameraRawWithoutRestrictedFieldSpecified23 =
        { "http://ns.adobe.com/camera-raw-settings/1.0/", "AutoBrightness", "boolean", "AutoContrast", "boolean", "AutoExposure", "boolean", "AutoShadows", "boolean", "BlueHue", "integer", "BlueSaturation", "integer", "Brightness", "integer", "CameraProfile", "text", "ChromaticAberrationB", "integer", "ChromaticAberrationR", "integer", "ColorNoiseReduction", "integer", "Contrast", "integer", "CropTop", "real", "CropLeft", "real", "CropBottom", "real", "CropRight", "real", "CropAngle", "real", "CropWidth", "real", "CropHeight", "real", "CropUnits", "integer", "Exposure", "real", "GreenHue", "integer", "GreenSaturation", "integer", "HasCrop", "boolean", "HasSettings", "boolean", "LuminanceSmoothing", "integer", "RawFileName", "text", "RedHue", "integer", "RedSaturation", "integer", "Saturation", "integer", "Shadows", "integer", "ShadowTint", "integer", "Sharpness", "integer", "Temperature", "integer", "Tint", "integer", "ToneCurveName", "text", "Version", "text", "VignetteAmount", "integer", "VignetteMidpoint", "integer" };

    private static readonly string[] CameraRawRestrictedFieldSpecified23 =
        { "http://ns.adobe.com/camera-raw-settings/1.0/", "WhiteBalance", "text", "^(As Shot|Auto|Daylight|Cloudy|Shade|Tungsten|Fluorescent|Flash|Custom)$" };

    private static readonly string[] AuxSpecified23 =
        { "http://ns.adobe.com/exif/1.0/aux/", "Lens", "text", "SerialNumber", "text" };

    // ── Built map: (namespaceURI, localName) → value type string ────────────────────────────────

    private static readonly Dictionary<(string Ns, string Name), string> Definitions = Build();

    private static Dictionary<(string, string), string> Build()
    {
        var map = new Dictionary<(string, string), string>();

        // createBasicSchemasDefinition
        RegisterStructure(map, PdfaIdentificationCommon);
        RegisterStructure(map, DublinCoreCommon);
        RegisterStructure(map, XmpBasicCommon);
        RegisterStructure(map, XmpRightsCommon);
        RegisterStructure(map, XmpMediaManagementCommon);
        RegisterStructure(map, XmpBasicJobCommon);
        RegisterStructure(map, XmpPagedTextCommon);
        RegisterStructure(map, AdobePdfCommon);
        RegisterStructure(map, PhotoshopCommon);
        RegisterStructure(map, TiffWithoutRestrictedFieldCommon);
        RegisterStructure(map, ExifWithoutRestrictedFieldCommon);
        RegisterRestrictedSimple(map, TiffRestrictedFieldCommon);
        RegisterRestrictedSimple(map, ExifRestrictedFieldCommon);
        // Closed seq-choice fields degrade (no-closed mode) to their base array type.
        TryRegister(map, "http://ns.adobe.com/tiff/1.0/", "YCbCrSubSampling", "seq integer");
        TryRegister(map, "http://ns.adobe.com/exif/1.0/", "ComponentsConfiguration", "seq integer");

        // createPredefinedPDFA_2_3SchemasDefinition additions
        RegisterStructure(map, PdfaIdentificationSpecified23);
        RegisterStructure(map, XmpBasicSpecified23);
        RegisterStructure(map, XmpPagedTextSpecified23);
        RegisterStructure(map, XmpDynamicMediaWithoutRestrictedFieldSpecified23);
        RegisterStructure(map, PhotoshopDiffer23);
        RegisterStructure(map, CameraRawWithoutRestrictedFieldSpecified23);
        RegisterStructure(map, ExifWithoutRestrictedFieldDiffer23);
        RegisterStructure(map, AuxSpecified23);
        RegisterRestrictedSimple(map, PdfaIdentificationRestrictedFieldDiffer23);
        RegisterRestrictedSimple(map, XmpDynamicMediaRestrictedFieldSpecified23);
        RegisterRestrictedSimple(map, CameraRawRestrictedFieldSpecified23);
        RegisterRestrictedSimple(map, ExifRestrictedFieldDiffer23);
        // camera-raw ToneCurve is a restricted "seq text" field → base type "seq text".
        TryRegister(map, "http://ns.adobe.com/camera-raw-settings/1.0/", "ToneCurve", "seq text");

        return map;
    }

    // {ns, name, type, name, type, ...}
    private static void RegisterStructure(Dictionary<(string, string), string> map, string[] structure)
    {
        for (int i = 1; i < structure.Length; i += 2)
            TryRegister(map, structure[0], structure[i], structure[i + 1]);
    }

    // {ns, name, baseType, regex, name, baseType, regex, ...}; base type only (no-closed mode).
    private static void RegisterRestrictedSimple(Dictionary<(string, string), string> map, string[] structure)
    {
        for (int i = 1; i < structure.Length; i += 3)
            TryRegister(map, structure[0], structure[i], structure[i + 1]);
    }

    // First registration wins, mirroring the reference's registerProperty (ignores later duplicates).
    private static void TryRegister(Dictionary<(string, string), string> map, string ns, string name, string type) =>
        map.TryAdd((ns, name), type);

    /// <summary>True when the reference predefines a property with this namespace URI and local name.</summary>
    public static bool IsPredefined(string namespaceUri, string localName) =>
        Definitions.ContainsKey((namespaceUri, localName));

    /// <summary>The predefined value type string (e.g. <c>"integer"</c>, <c>"seq integer"</c>, <c>"lang alt"</c>),
    /// or null when the property is not predefined.</summary>
    public static string? TypeOf(string namespaceUri, string localName) =>
        Definitions.TryGetValue((namespaceUri, localName), out string? type) ? type : null;

    // ── Simple-type value validation ────────────────────────────────────────────────────────────
    // Regexes reproduced from ISO 19005-2/-3 Annex B (the same set veraPDF uses). Wrapped as
    // \A(?:…)\z so that IsMatch demands a whole-string match — the semantics of Java's Matcher.matches(),
    // which the reference uses. Only the four unambiguously-restrictive simple types are checked; the
    // "matches anything" types (text/propername/agentname/rational/renditionclass/locale), date (an
    // ISO-8601 parse in the reference, not a regex), uri/url (no value constraint in the reference) and
    // gpscoordinate (a part-dependent regex) are deliberately left unchecked.

    private static Regex Anchored(string javaRegex) =>
        new(@"\A(?:" + javaRegex + @")\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Boolean = Anchored("^True$|^False$");
    private static readonly Regex Integer = Anchored(@"^[+-]?\d+$");
    private static readonly Regex Real = Anchored(@"^[+-]?\d+\.?\d*|[+-]?\d*\.?\d+$");
    private static readonly Regex MimeType = Anchored(@"^[-\w+\.]+/[-\w+\.]+$");

    /// <summary>
    /// Returns the whole-string matcher for a restrictive simple type name, or null for a type whose
    /// value is unconstrained (or not safely checkable). Only <c>boolean/integer/real/mimetype</c> are
    /// restrictive.
    /// </summary>
    public static Regex? RestrictiveMatcher(string type) => type switch
    {
        "boolean" => Boolean,
        "integer" => Integer,
        "real" => Real,
        "mimetype" => MimeType,
        _ => null,
    };

    /// <summary>
    /// True when <paramref name="value"/> is an acceptable spelling of the restrictive simple type
    /// <paramref name="matcher"/>. A trimmed retry keeps whitespace-padded (pretty-printed) values from
    /// being reported — the check only ever accepts a superset of the reference, never less.
    /// </summary>
    public static bool ValueMatches(Regex matcher, string? value)
    {
        if (value is null) return true;
        return matcher.IsMatch(value) || matcher.IsMatch(value.Trim());
    }
}
