using System.Collections.Generic;

namespace FontParser.Tables.Cff
{
    /// <summary>
    /// Per-parse save/restore stack for nested CFF subroutine (callsubr/callgsubr) calls.
    /// An instance is owned by a single <see cref="CharStringParser"/> and must NOT be shared
    /// across threads — each glyph parse creates its own parser and therefore its own nester.
    /// (Previously a static class, which silently corrupted output when two threads decoded
    /// CFF glyphs concurrently.)
    /// </summary>
    public class SubroutineNester
    {
        private readonly FixedStack<List<byte>> _byteStack = new();
        private readonly FixedStack<int> _indices = new();

        public SubroutineNester()
        {
            _byteStack.Capacity = 11;
            _indices.Capacity = 11;
        }

        public void Push(int index, List<byte> bytes)
        {
            var copy = new List<byte>(bytes);
            _byteStack.Push(copy);
            _indices.Push(index);
        }

        public (int index, List<byte> bytes) Pop()
        {
            return (_indices.Pop(), _byteStack.Pop());
        }
    }
}
