namespace FontParser.Tables.Cff.Type1
{
    public class CffDictEntry
    {
        public string Name { get; }

        public OperandKind OperandKind { get; }

        public object Operand { get; set; }

        public CffDictEntry(string name, OperandKind operandKind, object operand)
        {
            Name = name;
            OperandKind = operandKind;
            Operand = operand;
        }

#if DEBUG

        public override string ToString()
        {
            return $"{Name} {OperandKind} {Operand}";
        }

#endif
    }
}