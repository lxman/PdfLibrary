using PdfLibrary.Builder;

namespace PdfLibrary.Editing.Forms;

/// <summary>One radio-button option: which page, where, and its on-state name (the /AP /N key
/// that selecting this option sets, e.g. "A"). On-state names must be unique within a group
/// and must not be "Off".</summary>
public readonly record struct PdfRadioOptionPlacement(int PageIndex, PdfRect Rect, string OnState);
