using System.Collections.Generic;

namespace FontParser.Tables.Proprietary.Panose
{
    public class PanoseInterpreter
    {
        private static readonly Dictionary<int, string> FamilyKindValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Latin Text",
            [3] = "Latin Hand Written",
            [4] = "Latin Decorative",
            [5] = "Latin Symbol"
        };

        private static readonly Dictionary<int, List<string>> Descriptions = new Dictionary<int, List<string>>
        {
            [2] = new List<string> { "Serif Style", "Weight", "Proportion", "Contrast", "Stroke Variation", "Arm Style", "Letterform", "Midline", "X-Height" },
            [3] = new List<string> { "Tool Kind", "Weight", "Spacing", "Aspect Ratio", "Contrast", "Topology", "Form", "Finials", "X-Ascent" },
            [4] = new List<string> { "Class", "Weight", "Aspect", "Contrast", "Serif Variant", "Treatment", "Lining", "Topology", "Range of Characters" },
            [5] = new List<string> { "Kind", "Weight", "Spacing", "Aspect Ratio and Contrast", "Aspect Ratio of Character 94", "Aspect Ratio of Character 119",
                "Aspect Ratio of Character 157", "Aspect Ratio of Character 163", "Aspect Ratio of Character 211" }
        };

        private static readonly Dictionary<int, string> SerifStyleValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Cove",
            [3] = "Obtuse Cove",
            [4] = "Square Cove",
            [5] = "Obtuse Square Cove",
            [6] = "Square",
            [7] = "Thin",
            [8] = "Oval",
            [9] = "Exaggerated",
            [10] = "Triangle",
            [11] = "Normal Sans",
            [12] = "Obtuse Sans",
            [13] = "Perpendicular Sans",
            [14] = "Flared",
            [15] = "Rounded"
        };

        private static readonly Dictionary<int, string> WeightValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Very Light",
            [3] = "Light",
            [4] = "Thin",
            [5] = "Book",
            [6] = "Medium",
            [7] = "Demi",
            [8] = "Bold",
            [9] = "Heavy",
            [10] = "Black",
            [11] = "Extra Black (Nord)"
        };

        private static readonly Dictionary<int, string> ProportionValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Old Style",
            [3] = "Modern",
            [4] = "Even Width",
            [5] = "Extended",
            [6] = "Condensed",
            [7] = "Very Extended",
            [8] = "Very Condensed",
            [9] = "Monospaced"
        };

        private static readonly Dictionary<int, string> StrokeVariationValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "No Variation",
            [3] = "Gradual/Diagonal",
            [4] = "Gradual/Transitional",
            [5] = "Gradual/Vertical",
            [6] = "Gradual/Horizontal",
            [7] = "Rapid/Vertical",
            [8] = "Rapid/Horizontal",
            [9] = "Instant/Vertical",
            [10] = "Instant/Horizontal"
        };

        private static readonly Dictionary<int, string> ArmStyleValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Straight Arms/Horizontal",
            [3] = "Straight Arms/Wedge",
            [4] = "Straight Arms/Vertical",
            [5] = "Straight Arms/Single Serif",
            [6] = "Straight Arms/Double Serif",
            [7] = "Non-Straight/Horizontal",
            [8] = "Non-Straight/Wedge",
            [9] = "Non-Straight/Vertical",
            [10] = "Non-Straight/Single Serif",
            [11] = "Non-Straight/Double Serif",
        };

        private static readonly Dictionary<int, string> LetterformValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Normal/Contact",
            [3] = "Normal/Weighted",
            [4] = "Normal/Boxed",
            [5] = "Normal/Flattened",
            [6] = "Normal/Rounded",
            [7] = "Normal/Off Center",
            [8] = "Normal/Square",
            [9] = "Oblique/Contact",
            [10] = "Oblique/Weighted",
            [11] = "Oblique/Boxed",
            [12] = "Oblique/Flattened",
            [13] = "Oblique/Rounded",
            [14] = "Oblique/Off Center",
            [15] = "Oblique/Square"
        };

        private static readonly Dictionary<int, string> MidlineValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Standard/Trimmed",
            [3] = "Standard/Pointed",
            [4] = "Standard/Serifed",
            [5] = "High/Trimmed",
            [6] = "High/Pointed",
            [7] = "High/Serifed",
            [8] = "Constant/Trimmed",
            [9] = "Constant/Pointed",
            [10] = "Constant/Serifed",
            [11] = "Low/Trimmed",
            [12] = "Low/Pointed",
            [13] = "Low/Serifed"
        };

        private static readonly Dictionary<int, string> ToolKindValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Flat Nib",
            [3] = "Pressure Point",
            [4] = "Engraved",
            [5] = "Ball (Round Cap)",
            [6] = "Brush",
            [7] = "Rough",
            [8] = "Felt Pen/Brush Tip",
            [9] = "Wild Brush - Drips a lot"
        };

        private static readonly Dictionary<int, string> SpacingValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Proportional Spaced",
            [3] = "Monospaced"
        };

        private static readonly Dictionary<int, string> AspectRatioValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Very Condensed",
            [3] = "Condensed",
            [4] = "Normal",
            [5] = "Expanded",
            [6] = "Very Expanded"
        };

        private static readonly Dictionary<int, string> ContrastValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "None",
            [3] = "Very Low",
            [4] = "Low",
            [5] = "Medium Low",
            [6] = "Medium",
            [7] = "Medium High",
            [8] = "High",
            [9] = "Very High",
            [10] = "Horizontal Low",
            [11] = "Horizontal Medium",
            [12] = "Horizontal High",
            [13] = "Broken"
        };

        private static readonly Dictionary<int, string> TopologyValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Roman Disconnected",
            [3] = "Roman Trailing",
            [4] = "Roman Connected",
            [5] = "Cursive Disconnected",
            [6] = "Cursive Trailing",
            [7] = "Cursive Connected",
            [8] = "Blackletter Disconnected",
            [9] = "Blackletter Trailing",
            [10] = "Blackletter Connected"
        };

        private static readonly Dictionary<int, string> FormValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Upright / No Wrapping",
            [3] = "Upright / Some Wrapping",
            [4] = "Upright / More Wrapping",
            [5] = "Upright / Extreme Wrapping",
            [6] = "Oblique / No Wrapping",
            [7] = "Oblique / Some Wrapping",
            [8] = "Oblique / More Wrapping",
            [9] = "Oblique / Extreme Wrapping",
            [10] = "Exaggerated / No Wrapping",
            [11] = "Exaggerated / Some Wrapping",
            [12] = "Exaggerated / More Wrapping",
            [13] = "Exaggerated / Extreme Wrapping"
        };

        private static readonly Dictionary<int, string> FinialsValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "None / No loops",
            [3] = "None / Closed loops",
            [4] = "None / Open loops",
            [5] = "Sharp / No Loops",
            [6] = "Sharp / Closed Loops",
            [7] = "Sharp / Open Loops",
            [8] = "Tapered / No Loops",
            [9] = "Tapered / Closed Loops",
            [10] = "Tapered / Open Loops",
            [11] = "Round / No Loops",
            [12] = "Round / Closed Loops",
            [13] = "Round / Open Loops"
        };

        private static readonly Dictionary<int, string> XAscentValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Very Low",
            [3] = "Low",
            [4] = "Medium",
            [5] = "High",
            [6] = "Very High"
        };

        private static readonly Dictionary<int, string> XHeightValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Constant/Small",
            [3] = "Constant/Standard",
            [4] = "Constant/Large",
            [5] = "Ducking/Small",
            [6] = "Ducking/Standard",
            [7] = "Ducking/Large"
        };

        private static readonly Dictionary<int, string> ClassValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Derivative",
            [3] = "Non-standard Topology",
            [4] = "Non-standard Elements",
            [5] = "Non-standard Aspect",
            [6] = "Initials",
            [7] = "Cartoon",
            [8] = "Picture Stems",
            [9] = "Ornamented",
            [10] = "Text and Background",
            [11] = "Collage",
            [12] = "Montage"
        };

        private static readonly Dictionary<int, string> AspectValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Super Condensed",
            [3] = "Very Condensed",
            [4] = "Condensed",
            [5] = "Normal",
            [6] = "Extended",
            [7] = "Very Extended",
            [8] = "Super Extended",
            [9] = "Monospaced"
        };

        private static readonly Dictionary<int, string> SerifVariantValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Cove",
            [3] = "Obtuse Cove",
            [4] = "Square Cove",
            [5] = "Obtuse Square Cove",
            [6] = "Square",
            [7] = "Thin",
            [8] = "Oval",
            [9] = "Exaggerated",
            [10] = "Triangle",
            [11] = "Normal Sans",
            [12] = "Obtuse Sans",
            [13] = "Perpendicular Sans",
            [14] = "Flared",
            [15] = "Rounded",
            [16] = "Script"
        };

        private static readonly Dictionary<int, string> TreatmentValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "None - Standard Solid Fill",
            [3] = "White / No Fill",
            [4] = "Patterned Fill",
            [5] = "Complex Fill",
            [6] = "Shaped Fill",
            [7] = "Drawn / Distressed"
        };

        private static readonly Dictionary<int, string> LiningValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "None",
            [3] = "Inline",
            [4] = "Outline",
            [5] = "Engraved (Multiple Lines)",
            [6] = "Shadow",
            [7] = "Relief",
            [8] = "Backdrop"
        };

        private static readonly Dictionary<int, string> TopologyLdValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Standard",
            [3] = "Square",
            [4] = "Multiple Segment",
            [5] = "Deco (E.M.S.) Waco midlines",
            [6] = "Uneven Weighting",
            [7] = "Diverse Arms",
            [8] = "Diverse Forms",
            [9] = "Lombardic Forms",
            [10] = "Upper Case in Lower Case",
            [11] = "Implied Topology",
            [12] = "Horseshoe E and A",
            [13] = "Cursive",
            [14] = "Blackletter",
            [15] = "Swash Variance"
        };

        private static readonly Dictionary<int, string> RangeOfCharactersValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Extended Collection",
            [3] = "Literals",
            [4] = "No Lower Case",
            [5] = "Small Caps"
        };

        private static readonly Dictionary<int, string> KindValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "Montages",
            [3] = "Pictures",
            [4] = "Shapes",
            [5] = "Scientific",
            [6] = "Music",
            [7] = "Expert",
            [8] = "Patterns",
            [9] = "Boarders",
            [10] = "Icons",
            [11] = "Logos",
            [12] = "Industry specific"
        };

        private static readonly Dictionary<int, string> AspectCharXxValues = new Dictionary<int, string>
        {
            [0] = "Any",
            [1] = "No Fit",
            [2] = "No Width",
            [3] = "Exceptionally Wide",
            [4] = "Super Wide",
            [5] = "Very Wide",
            [6] = "Wide",
            [7] = "Normal",
            [8] = "Narrow",
            [9] = "Very Narrow"
        };

        private static byte[]? _values;

        public PanoseInterpreter(byte[] values)
        {
            _values = values;
        }

        public string GetValue(byte value)
        {
            if (_values is null) return string.Empty;
            byte familyKind = _values[0];
            if (familyKind > 5 || value > 9) return string.Empty;
            if (familyKind < 2) return $"{value} - {_values[value]}";
            if (value == 0) return $"Family Kind - {FamilyKindValues[_values[value]]}";
            string name = Descriptions[familyKind][value - 1];
            byte target = _values[value];
            switch (name)
            {
                case "Serif Style":
                    return $"{name} - {SerifStyleValues[target]}";

                case "Weight":
                    return $"{name} - {WeightValues[target]}";

                case "Proportion":
                    return $"{name} - {ProportionValues[target]}";

                case "Tool Kind":
                    return $"{name} - {ToolKindValues[target]}";

                case "Spacing":
                    return $"{name} - {SpacingValues[target]}";

                case "Aspect Ratio":
                    return $"{name} - {AspectRatioValues[target]}";

                case "Contrast":
                    return $"{name} - {ContrastValues[target]}";

                case "Topology":
                    switch (familyKind)
                    {
                        case 3:
                            return $"{name} - {TopologyValues[target]}";

                        case 4:
                            return $"{name} - {TopologyLdValues[target]}";
                    }
                    break;

                case "Form":
                    return $"{name} - {FormValues[target]}";

                case "Finials":
                    return $"{name} - {FinialsValues[target]}";

                case "X-Ascent":
                    return $"{name} - {XAscentValues[target]}";

                case "X-Height":
                    return $"{name} - {XHeightValues[target]}";

                case "Class":
                    return $"{name} - {ClassValues[target]}";

                case "Aspect":
                    return $"{name} - {AspectValues[target]}";

                case "Serif Variant":
                    return $"{name} - {SerifVariantValues[target]}";

                case "Treatment":
                    return $"{name} - {TreatmentValues[target]}";

                case "Lining":
                    return $"{name} - {LiningValues[target]}";

                case "Range of Characters":
                    return $"{name} - {RangeOfCharactersValues[target]}";

                case "Kind":
                    return $"{name} - {KindValues[target]}";

                case "Stroke Variation":
                    return $"{name} - {StrokeVariationValues[target]}";

                case "Arm Style":
                    return $"{name} - {ArmStyleValues[target]}";

                case "Letterform":
                    return $"{name} - {LetterformValues[target]}";

                case "Midline":
                    return $"{name} - {MidlineValues[target]}";
            }

            if (name.StartsWith("Aspect Ratio of Character"))
            {
                return $"{name} - {AspectCharXxValues[target]}";
            }
            return string.Empty;
        }
    }
}