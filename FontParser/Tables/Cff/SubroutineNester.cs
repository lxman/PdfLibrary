using System.Collections.Generic;

namespace FontParser.Tables.Cff
{
    public static class SubroutineNester
    {
        private static readonly FixedStack<List<byte>> ByteStack = new FixedStack<List<byte>>();
        private static readonly FixedStack<int> Indices = new FixedStack<int>();

        static SubroutineNester()
        {
            ByteStack.Capacity = 11;
            Indices.Capacity = 11;
        }

        public static void Push(int index, List<byte> bytes)
        {
            var copy = new List<byte>(bytes);
            ByteStack.Push(copy);
            Indices.Push(index);
        }

        public static (int index, List<byte> bytes) Pop()
        {
            return (Indices.Pop(), ByteStack.Pop());
        }
    }
}