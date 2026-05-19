namespace ICCSharp.Tags;

/// <summary>
/// A single localized string record from a multiLocalizedUnicodeType tag.
/// LanguageCode is ISO 639-1 (two ASCII characters); CountryCode is ISO 3166 (two ASCII).
/// Either may be empty for "unspecified".
/// </summary>
public readonly record struct LocalizedString(string LanguageCode, string CountryCode, string Text);
