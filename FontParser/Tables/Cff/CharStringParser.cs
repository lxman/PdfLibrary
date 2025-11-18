using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace FontParser.Tables.Cff
{
    public class CharStringParser
    {
        private readonly FixedStack<float> _stack = new FixedStack<float>();
        private float? _width = null;
        private int _nStems = 0;
        private float _x = 0;
        private float _y = 0;
        private List<byte> _bytes;
        private readonly List<List<byte>> _globalSubroutines;
        private readonly List<List<byte>> _localSubroutines;
        private readonly FixedStack<List<byte>> _byteStack = new FixedStack<List<byte>>();
        private readonly int _nominalWidthX;
        private readonly int _globalOffset;
        private readonly int _localOffset;
        private readonly List<byte> _originalBytes;
        private readonly Dictionary<int, float> _transients = new Dictionary<int, float>();
        private bool _drawing;

        public CharStringParser(
            int capacity,
            List<byte> bytes,
            List<List<byte>> globalSubroutines,
            List<List<byte>> localSubroutines,
            int nominalWidthX)
        {
            _stack.Capacity = capacity;
            _byteStack.Capacity = 11;
            _bytes = bytes;
            _originalBytes = bytes;
            _globalSubroutines = globalSubroutines;
            _localSubroutines = localSubroutines;
            _nominalWidthX = nominalWidthX;
            _globalOffset = ComputeOffset(_globalSubroutines.Count);
            _localOffset = ComputeOffset(_localSubroutines.Count);
        }

        public List<string> Parse()
        {
            var random = new Random();
            var output = new StringBuilder();
            var stackIndex = 0;
            var endChar = false;
            while (!endChar && stackIndex < _bytes.Count)
            {
                byte b = _bytes[stackIndex++];
                switch (b >= 0x20)
                {
                    case true when b < 0xF7:
                        _stack.Push(b - 0x8B);
                        break;

                    case true when b < 0xFB:
                        {
                            byte b0 = _bytes[stackIndex++];
                            _stack.Push(((b - 0xF7) * 0x100) + b0 + 0x6C);
                            break;
                        }
                    case true when b < 0xFF:
                        {
                            byte b0 = _bytes[stackIndex++];
                            _stack.Push((-(b - 0xFB) * 0x100) - b0 - 0x6C);
                            break;
                        }
                    case true when b == 0xFF:
                    case false when b == 0:
                        {
                            byte b0 = _bytes[stackIndex++];
                            byte b1 = _bytes[stackIndex++];
                            byte b2 = _bytes[stackIndex++];
                            byte b3 = _bytes[stackIndex++];
                            float val = BinaryPrimitives.ReadInt32BigEndian(new[] { b0, b1, b2, b3 });
                            _stack.Push(val / 0x10000);
                            break;
                        }
                    case false:
                        // Parse operator
                        bool phase;
                        int index;
                        PointF c1;
                        PointF c2;

                        switch (b)
                        {
                            case 0x01:
                            case 0x03:
                            case 0x12:
                            case 0x17:
                                StemCalculation();
                                break;

                            case 0x04:
                                if (_stack.Count > 1) WidthCalculation();
                                _y += _stack.PopBottom();
                                if (_drawing) output.AppendLine("EndFigure");
                                output.AppendLine($"MoveTo x: {_x} y: {_y}");
                                _drawing = true;
                                _stack.Clear();
                                break;

                            case 0x05:
                                while (_stack.Count >= 2)
                                {
                                    _x += _stack.PopBottom();
                                    _y += _stack.PopBottom();
                                    output.AppendLine($"LineTo x: {_x} y: {_y}");
                                }
                                _stack.Clear();
                                break;

                            case 0x06:
                            case 0x07:
                                phase = b == 0x06;
                                while (_stack.Count >= 1)
                                {
                                    if (phase)
                                    {
                                        _x += _stack.PopBottom();
                                    }
                                    else
                                    {
                                        _y += _stack.PopBottom();
                                    }
                                    output.AppendLine($"LineTo x: {_x} y: {_y}");
                                    phase = !phase;
                                }
                                _stack.Clear();
                                break;

                            case 0x08:
                                while (_stack.Count > 0)
                                {
                                    output.AppendLine(GetCubicBezier(ref _x, ref _y));
                                }
                                _stack.Clear();
                                break;

                            case 0x0A:
                                index = Convert.ToInt32(_stack.Pop()) + _localOffset;
                                if (index < _localSubroutines.Count)
                                {
                                    SubroutineNester.Push(stackIndex, _bytes);
                                    _bytes = _localSubroutines[index];
                                    if (_bytes.Count > 0)
                                    {
                                        output.AppendLine(string.Join(Environment.NewLine, Parse()));
                                    }
                                    (int index, List<byte> bytes) lState = SubroutineNester.Pop();
                                    stackIndex = lState.index;
                                    _bytes = lState.bytes;
                                }
                                break;

                            case 0x0B:
                                // TODO: Implement return
                                break;

                            case 0x0E:
                                // TODO: Implement CFF2
                                if (_stack.Count > 0)
                                {
                                    WidthCalculation();
                                }

                                output.Append("ClosePath");
                                endChar = true;
                                break;

                            case 0x13:
                            case 0x14:
                                StemCalculation();
                                stackIndex += (_nStems + 7) >> 3;
                                break;

                            case 0x15:
                                if (_stack.Count > 2)
                                {
                                    WidthCalculation();
                                }

                                _x += _stack.PopBottom();
                                _y += _stack.PopBottom();
                                if (_drawing) output.AppendLine("EndFigure");
                                output.AppendLine($"MoveTo x: {_x} y: {_y}");
                                _drawing = true;
                                _stack.Clear();
                                break;

                            case 0x16:
                                if (_stack.Count > 1)
                                {
                                    WidthCalculation();
                                }
                                _x += _stack.PopBottom();
                                if (_drawing) output.AppendLine("EndFigure");
                                output.AppendLine($"MoveTo x: {_x} y: {_y}");
                                _drawing = true;
                                _stack.Clear();
                                break;

                            case 0x18:
                                while (_stack.Count >= 8)
                                {
                                    output.AppendLine(GetCubicBezier(ref _x, ref _y));
                                }
                                _x += _stack.PopBottom();
                                _y += _stack.PopBottom();
                                output.AppendLine($"LineTo x: {_x} y: {_y}");
                                break;

                            case 0x19:
                                while (_stack.Count >= 8)
                                {
                                    _x += _stack.PopBottom();
                                    _y += _stack.PopBottom();
                                    output.AppendLine($"LineTo x: {_x} y: {_y}");
                                }
                                c1 = new PointF(_x + _stack.PopBottom(), _y + _stack.PopBottom());
                                c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                _x = c2.X + _stack.PopBottom();
                                _y = c2.Y + _stack.PopBottom();
                                output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {_x}, {_y}");
                                _stack.Clear();
                                break;

                            case 0x1A:
                                if (_stack.Count % 2 != 0)
                                {
                                    _x += _stack.PopBottom();
                                }

                                while (_stack.Count >= 4)
                                {
                                    c1 = new PointF(_x, _y + _stack.PopBottom());
                                    c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                    _x = c2.X;
                                    _y = c2.Y + _stack.PopBottom();
                                    output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {_x}, {_y}");
                                }
                                _stack.Clear();
                                break;

                            case 0x1B:
                                if (_stack.Count % 2 != 0)
                                {
                                    _y += _stack.PopBottom();
                                }

                                while (_stack.Count >= 4)
                                {
                                    c1 = new PointF(_x + _stack.PopBottom(), _y);
                                    c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                    _x = c2.X + _stack.PopBottom();
                                    _y = c2.Y;
                                    output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {_x}, {_y}");
                                }
                                _stack.Clear();
                                break;

                            case 0x1C:
                                var data = new byte[2];
                                data[0] = _bytes[stackIndex++];
                                data[1] = _bytes[stackIndex++];
                                _stack.Push(BinaryPrimitives.ReadInt16BigEndian(data));
                                break;

                            case 0x1D:
                                SubroutineNester.Push(stackIndex, _bytes);
                                index = Convert.ToInt32(_stack.Pop()) + _globalOffset;
                                _bytes = _globalSubroutines[index];
                                if (_bytes.Count > 0)
                                {
                                    output.AppendLine(string.Join(Environment.NewLine, Parse()));
                                }

                                (int index, List<byte> bytes) gState = SubroutineNester.Pop();
                                stackIndex = gState.index;
                                _bytes = gState.bytes;
                                break;

                            case 0x1E:
                            case 0x1F:
                                phase = b == 0x1F;
                                while (_stack.Count >= 4)
                                {
                                    if (phase)
                                    {
                                        c1 = new PointF(_x + _stack.PopBottom(), _y);
                                        c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                        _y = c2.Y + _stack.PopBottom();
                                        _x = c2.X + (_stack.Count == 1 ? _stack.PopBottom() : 0);
                                    }
                                    else
                                    {
                                        c1 = new PointF(_x, _y + _stack.PopBottom());
                                        c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                        _x = c2.X + _stack.PopBottom();
                                        _y = c2.Y + (_stack.Count == 1 ? _stack.PopBottom() : 0);
                                    }
                                    output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {_x}, {_y}");
                                    phase = !phase;
                                }
                                _stack.Clear();
                                break;

                            case 0x0C:
                                bool aValue;
                                bool bValue;
                                b = _bytes[stackIndex++];
                                if (b >= 0x26)
                                {
                                    throw new ArgumentOutOfRangeException();
                                }

                                switch (b)
                                {
                                    case 0x03:
                                        aValue = _stack.Pop() != 0;
                                        bValue = _stack.Pop() != 0;
                                        _stack.Push(aValue && bValue ? 1 : 0);
                                        break;

                                    case 0x04:
                                        aValue = _stack.Pop() != 0;
                                        bValue = _stack.Pop() != 0;
                                        _stack.Push(aValue || bValue ? 1 : 0);
                                        break;

                                    case 0x05:
                                        aValue = _stack.Pop() != 0;
                                        _stack.Push(aValue ? 1 : 0);
                                        break;

                                    case 0x09:
                                        _stack.Push(System.Math.Abs(_stack.Pop()));
                                        break;

                                    case 0x0A:
                                        _stack.Push(_stack.Pop() + _stack.Pop());
                                        break;

                                    case 0x0B:
                                        _stack.Push(_stack.Pop() - _stack.Pop());
                                        break;

                                    case 0x0C:
                                        _stack.Push(_stack.Pop() / _stack.Pop());
                                        break;

                                    case 0x0E:
                                        _stack.Push(-_stack.Pop());
                                        break;

                                    case 0x0F:
                                        _stack.Push(System.Math.Abs(_stack.Pop() - _stack.Pop()) < float.MinValue ? 1 : 0);
                                        break;

                                    case 0x12:
                                        _stack.Pop();
                                        break;

                                    case 0x14:
                                        float val = _stack.Pop();
                                        var idx = Convert.ToInt32(_stack.Pop());
                                        _transients[idx] = val;
                                        break;

                                    case 0x15:
                                        idx = Convert.ToInt32(_stack.Pop());
                                        _stack.Push(_transients[idx]);
                                        _transients.Remove(idx);
                                        break;

                                    case 0x16:
                                        float comp1 = _stack.Pop();
                                        float comp2 = _stack.Pop();
                                        float val1 = _stack.Pop();
                                        float val2 = _stack.Pop();
                                        _stack.Push(val1 <= val2 ? comp1 : comp2);
                                        break;

                                    case 0x17:
                                        _stack.Push(Convert.ToSingle(random.NextDouble()));
                                        break;

                                    case 0x18:
                                        _stack.Push(_stack.Pop() * _stack.Pop());
                                        break;

                                    case 0x1A:
                                        _stack.Push(Convert.ToSingle(System.Math.Sqrt(_stack.Pop())));
                                        break;

                                    case 0x1B:
                                        comp1 = _stack.Pop();
                                        _stack.Push(comp1);
                                        _stack.Push(comp1);
                                        break;

                                    case 0x1C:
                                        comp1 = _stack.Pop();
                                        comp2 = _stack.Pop();
                                        _stack.Push(comp1);
                                        _stack.Push(comp2);
                                        break;

                                    case 0x1D:
                                        idx = Convert.ToInt32(_stack.Pop());
                                        idx = idx < 0 ? 0 : idx > _stack.Count - 1 ? _stack.Count - 1 : idx;
                                        _stack.Push(_stack.ElementAt(idx));
                                        break;

                                    case 0x1E:
                                        var n = Convert.ToInt32(_stack.Pop());
                                        float j = _stack.Pop();
                                        _stack.Roll(Convert.ToInt32(j), n);
                                        break;

                                    case 0x22:
                                        c1 = new PointF(_x + _stack.PopBottom(), _y);
                                        c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                        var c3 = new PointF(c2.X + _stack.PopBottom(), c2.Y);
                                        var c4 = new PointF(c3.X + _stack.PopBottom(), c3.Y);
                                        var c5 = new PointF(c4.X + _stack.PopBottom(), c4.Y);
                                        var c6 = new PointF(c5.X + _stack.PopBottom(), c5.Y);
                                        _x = c6.X;
                                        _y = c6.Y;
                                        output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {c3}");
                                        output.AppendLine($"CubicBezierTo p1: {c4} p2: {c5} p3: {c6}");
                                        break;

                                    case 0x23:
                                        output.AppendLine($"CubicBezierTo p1: {_stack.PopBottom()}, {_stack.PopBottom()} p2: {_stack.PopBottom()}, {_stack.PopBottom()} p3: {_stack.PopBottom()}, {_stack.PopBottom()}");
                                        output.AppendLine($"CubicBezierTo p1: {_stack.PopBottom()}, {_stack.PopBottom()} p2: {_stack.PopBottom()}, {_stack.PopBottom()} p3: {_stack.PopBottom()}, {_stack.PopBottom()}");
                                        _stack.Clear();
                                        break;

                                    case 0x24:
                                        c1 = new PointF(_x + _stack.PopBottom(), _y + _stack.PopBottom());
                                        c2 = new PointF(c1.X + _stack.PopBottom(), c1.Y + _stack.PopBottom());
                                        c3 = new PointF(c2.X + _stack.PopBottom(), c2.Y);
                                        c4 = new PointF(c3.X + _stack.PopBottom(), c3.Y);
                                        c5 = new PointF(c4.X + _stack.PopBottom(), c4.Y + _stack.PopBottom());
                                        c6 = new PointF(c5.X + _stack.PopBottom(), c5.Y);
                                        _x = c6.X;
                                        _y = c6.Y;
                                        output.AppendLine($"CubicBezierTo p1: {c1} p2: {c2} p3: {c3}");
                                        output.AppendLine($"CubicBezierTo p1: {c4} p2: {c5} p3: {c6}");
                                        _stack.Clear();
                                        break;

                                    case 0x25:
                                        break;
                                }
                                break;
                        }
                        break;

                    default:
                        break;
                }
            }
            List<string> toReturn = output.ToString().Split(Environment.NewLine).ToList();
            toReturn.RemoveAll(s => s.Length == 0);
            return toReturn;
        }

        private string GetCubicBezier(ref float x, ref float y)
        {
            x += _stack.PopBottom();
            y += _stack.PopBottom();
            var p1 = new PointF(x, y);
            x += _stack.PopBottom();
            y += _stack.PopBottom();
            var p2 = new PointF(x, y);
            x += _stack.PopBottom();
            y += _stack.PopBottom();
            var p3 = new PointF(x, y);
            return $"CubicBezierTo p1: {p1} p2: {p2} p3: {p3}";
        }

        private static int ComputeOffset(int count)
        {
            return count < 1240
                ? 107
                : count < 33900
                    ? 1131
                    : 32768;
        }

        private void StemCalculation()
        {
            if (_stack.Count % 2 > 0) WidthCalculation();
            _nStems += _stack.Count / 2;
            _stack.Clear();
        }

        private void WidthCalculation()
        {
            _width ??= _stack.PopBottom() + _nominalWidthX;
        }
    }
}