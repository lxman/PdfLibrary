using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class MathConstantsTable
    {
        public short ScriptPercentScaleDown { get; }

        public short ScriptPercentScaleUp { get; }

        public ushort DelimitedSubFormulaMinHeight { get; }

        public ushort DisplayOperatorMinHeight { get; }

        public short RadicalDegreeBottomRaisePercent { get; }

        public MathValueRecord MathLeading { get; }

        public MathValueRecord AxisHeight { get; }

        public MathValueRecord AccentBaseHeight { get; }

        public MathValueRecord FlattenedAccentBaseHeight { get; }

        public MathValueRecord SubscriptShiftDown { get; }

        public MathValueRecord SubscriptTopMax { get; }

        public MathValueRecord SubscriptBaselineDropMin { get; }

        public MathValueRecord SuperscriptShiftUp { get; }

        public MathValueRecord SuperscriptShiftUpCramped { get; }

        public MathValueRecord SuperscriptBottomMin { get; }

        public MathValueRecord SuperscriptBaselineDropMax { get; }

        public MathValueRecord SubSuperscriptGapMin { get; }

        public MathValueRecord SuperscriptBottomMaxWithSubscript { get; }

        public MathValueRecord SpaceAfterScript { get; }

        public MathValueRecord UpperLimitGapMin { get; }

        public MathValueRecord UpperLimitBaselineRiseMin { get; }

        public MathValueRecord LowerLimitGapMin { get; }

        public MathValueRecord LowerLimitBaselineDropMin { get; }

        public MathValueRecord StackTopShiftUp { get; }

        public MathValueRecord StackTopDisplayStyleShiftUp { get; }

        public MathValueRecord StackBottomShiftDown { get; }

        public MathValueRecord StackBottomDisplayStyleShiftDown { get; }

        public MathValueRecord StackGapMin { get; }

        public MathValueRecord StackDisplayStyleGapMin { get; }

        public MathValueRecord StretchStackTopShiftUp { get; }

        public MathValueRecord StretchStackBottomShiftDown { get; }

        public MathValueRecord StretchStackGapAboveMin { get; }

        public MathValueRecord StretchStackGapBelowMin { get; }

        public MathValueRecord FractionNumeratorShiftUp { get; }

        public MathValueRecord FractionNumeratorDisplayStyleShiftUp { get; }

        public MathValueRecord FractionDenominatorShiftDown { get; }

        public MathValueRecord FractionDenominatorDisplayStyleShiftDown { get; }

        public MathValueRecord FractionNumeratorGapMin { get; }

        public MathValueRecord FractionNumDisplayStyleGapMin { get; }

        public MathValueRecord FractionRuleThickness { get; }

        public MathValueRecord FractionDenominatorGapMin { get; }

        public MathValueRecord FractionDenomDisplayStyleGapMin { get; }

        public MathValueRecord SkewedFractionHorizontalGap { get; }

        public MathValueRecord SkewedFractionVerticalGap { get; }

        public MathValueRecord OverbarVerticalGap { get; }

        public MathValueRecord OverbarRuleThickness { get; }

        public MathValueRecord OverbarExtraAscender { get; }

        public MathValueRecord UnderbarVerticalGap { get; }

        public MathValueRecord UnderbarRuleThickness { get; }

        public MathValueRecord UnderbarExtraDescender { get; }

        public MathValueRecord RadicalVerticalGap { get; }

        public MathValueRecord RadicalDisplayStyleVerticalGap { get; }

        public MathValueRecord RadicalRuleThickness { get; }

        public MathValueRecord RadicalExtraAscender { get; }

        public MathValueRecord RadicalKernBeforeDegree { get; }

        public MathValueRecord RadicalKernAfterDegree { get; }

        public MathConstantsTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ScriptPercentScaleDown = reader.ReadShort();
            ScriptPercentScaleUp = reader.ReadShort();
            DelimitedSubFormulaMinHeight = reader.ReadUShort();
            DisplayOperatorMinHeight = reader.ReadUShort();
            MathLeading = new MathValueRecord(reader, position);
            AxisHeight = new MathValueRecord(reader, position);
            AccentBaseHeight = new MathValueRecord(reader, position);
            FlattenedAccentBaseHeight = new MathValueRecord(reader, position);
            SubscriptShiftDown = new MathValueRecord(reader, position);
            SubscriptTopMax = new MathValueRecord(reader, position);
            SubscriptBaselineDropMin = new MathValueRecord(reader, position);
            SuperscriptShiftUp = new MathValueRecord(reader, position);
            SuperscriptShiftUpCramped = new MathValueRecord(reader, position);
            SuperscriptBottomMin = new MathValueRecord(reader, position);
            SuperscriptBaselineDropMax = new MathValueRecord(reader, position);
            SubSuperscriptGapMin = new MathValueRecord(reader, position);
            SuperscriptBottomMaxWithSubscript = new MathValueRecord(reader, position);
            SpaceAfterScript = new MathValueRecord(reader, position);
            UpperLimitGapMin = new MathValueRecord(reader, position);
            UpperLimitBaselineRiseMin = new MathValueRecord(reader, position);
            LowerLimitGapMin = new MathValueRecord(reader, position);
            LowerLimitBaselineDropMin = new MathValueRecord(reader, position);
            StackTopShiftUp = new MathValueRecord(reader, position);
            StackTopDisplayStyleShiftUp = new MathValueRecord(reader, position);
            StackBottomShiftDown = new MathValueRecord(reader, position);
            StackBottomDisplayStyleShiftDown = new MathValueRecord(reader, position);
            StackGapMin = new MathValueRecord(reader, position);
            StackDisplayStyleGapMin = new MathValueRecord(reader, position);
            StretchStackTopShiftUp = new MathValueRecord(reader, position);
            StretchStackBottomShiftDown = new MathValueRecord(reader, position);
            StretchStackGapAboveMin = new MathValueRecord(reader, position);
            StretchStackGapBelowMin = new MathValueRecord(reader, position);
            FractionNumeratorShiftUp = new MathValueRecord(reader, position);
            FractionNumeratorDisplayStyleShiftUp = new MathValueRecord(reader, position);
            FractionDenominatorShiftDown = new MathValueRecord(reader, position);
            FractionDenominatorDisplayStyleShiftDown = new MathValueRecord(reader, position);
            FractionNumeratorGapMin = new MathValueRecord(reader, position);
            FractionNumDisplayStyleGapMin = new MathValueRecord(reader, position);
            FractionRuleThickness = new MathValueRecord(reader, position);
            FractionDenominatorGapMin = new MathValueRecord(reader, position);
            FractionDenomDisplayStyleGapMin = new MathValueRecord(reader, position);
            SkewedFractionHorizontalGap = new MathValueRecord(reader, position);
            SkewedFractionVerticalGap = new MathValueRecord(reader, position);
            OverbarVerticalGap = new MathValueRecord(reader, position);
            OverbarRuleThickness = new MathValueRecord(reader, position);
            OverbarExtraAscender = new MathValueRecord(reader, position);
            UnderbarVerticalGap = new MathValueRecord(reader, position);
            UnderbarRuleThickness = new MathValueRecord(reader, position);
            UnderbarExtraDescender = new MathValueRecord(reader, position);
            RadicalVerticalGap = new MathValueRecord(reader, position);
            RadicalDisplayStyleVerticalGap = new MathValueRecord(reader, position);
            RadicalRuleThickness = new MathValueRecord(reader, position);
            RadicalExtraAscender = new MathValueRecord(reader, position);
            RadicalKernBeforeDegree = new MathValueRecord(reader, position);
            RadicalKernAfterDegree = new MathValueRecord(reader, position);
            RadicalDegreeBottomRaisePercent = reader.ReadShort();
        }
    }
}