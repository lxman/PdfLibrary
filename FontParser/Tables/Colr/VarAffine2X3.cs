using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class VarAffine2X3
    {
        public float Xx { get; }

        public float Yx { get; }

        public float Xy { get; }

        public float Yy { get; }

        public float Dx { get; }

        public float Dy { get; }

        public uint VarIndexBase { get; }

        public VarAffine2X3(BigEndianReader reader)
        {
            Xx = reader.ReadF16Dot16();
            Yx = reader.ReadF16Dot16();
            Xy = reader.ReadF16Dot16();
            Yy = reader.ReadF16Dot16();
            Dx = reader.ReadF16Dot16();
            Dy = reader.ReadF16Dot16();
            VarIndexBase = reader.ReadUInt32();
        }

        public new string ToString() => $"VarIndexBase: {VarIndexBase}, Xx: {Xx}, Xy {Xy}, Yx: {Yx}, Yy {Yy}, Dx: {Dx}, Dy {Dy}";
    }
}