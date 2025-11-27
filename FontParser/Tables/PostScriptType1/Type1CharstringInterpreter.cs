using System;
using System.Collections.Generic;
using System.Drawing;
using FontParser.Tables.Cff;

namespace FontParser.Tables.PostScriptType1
{
    /// <summary>
    /// Interprets Type 1 charstring bytecode and converts it to glyph outlines
    /// </summary>
    public class Type1CharstringInterpreter
    {
        private readonly List<List<byte>> _subrs;
        private readonly Stack<float> _stack = new Stack<float>();
        private float _currentX;
        private float _currentY;
        private GlyphOutline _outline;
        private bool _pathStarted;
        private float _pathStartX;
        private float _pathStartY;

        // For flex hints
        private readonly List<PointF> _flexPoints = new List<PointF>();
        private bool _inFlex;

        public Type1CharstringInterpreter(List<List<byte>> subrs = null)
        {
            _subrs = subrs;
        }

        /// <summary>
        /// Interpret a decrypted charstring and return the glyph outline
        /// </summary>
        public GlyphOutline Interpret(byte[] charstring)
        {
            _stack.Clear();
            _currentX = 0;
            _currentY = 0;
            _outline = new GlyphOutline();
            _pathStarted = false;
            _flexPoints.Clear();
            _inFlex = false;

            try
            {
                Execute(charstring);
            }
            catch
            {
                // If interpretation fails, return what we have
            }

            UpdateBoundingBox();
            return _outline;
        }

        private void Execute(byte[] data)
        {
            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i++];

                if (b >= 32)
                {
                    // Number encoding
                    float value = DecodeNumber(data, ref i, b);
                    _stack.Push(value);
                }
                else if (b == 12)
                {
                    // Two-byte escape sequence
                    if (i >= data.Length) break;
                    byte b2 = data[i++];
                    ExecuteEscapeCommand(b2);
                }
                else
                {
                    // Single-byte command
                    ExecuteCommand(b);
                }
            }
        }

        private float DecodeNumber(byte[] data, ref int i, byte firstByte)
        {
            if (firstByte >= 32 && firstByte <= 246)
            {
                return firstByte - 139;
            }
            else if (firstByte >= 247 && firstByte <= 250)
            {
                byte b1 = data[i++];
                return (firstByte - 247) * 256 + b1 + 108;
            }
            else if (firstByte >= 251 && firstByte <= 254)
            {
                byte b1 = data[i++];
                return -(firstByte - 251) * 256 - b1 - 108;
            }
            else if (firstByte == 255)
            {
                // 4-byte signed integer (fixed point 16.16 in some implementations, but typically integer in Type1)
                int value = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
                i += 4;
                return value;
            }
            return 0;
        }

        private void ExecuteCommand(byte cmd)
        {
            switch (cmd)
            {
                case 1: // hstem
                    _stack.Clear();
                    break;

                case 3: // vstem
                    _stack.Clear();
                    break;

                case 4: // vmoveto
                    if (_stack.Count >= 1)
                    {
                        float dy = Pop();
                        _currentY += dy;
                        MoveTo(_currentX, _currentY);
                    }
                    break;

                case 5: // rlineto
                    if (_stack.Count >= 2)
                    {
                        float dy = Pop();
                        float dx = Pop();
                        _currentX += dx;
                        _currentY += dy;
                        LineTo(_currentX, _currentY);
                    }
                    break;

                case 6: // hlineto
                    if (_stack.Count >= 1)
                    {
                        float dx = Pop();
                        _currentX += dx;
                        LineTo(_currentX, _currentY);
                    }
                    break;

                case 7: // vlineto
                    if (_stack.Count >= 1)
                    {
                        float dy = Pop();
                        _currentY += dy;
                        LineTo(_currentX, _currentY);
                    }
                    break;

                case 8: // rrcurveto
                    if (_stack.Count >= 6)
                    {
                        float dy3 = Pop();
                        float dx3 = Pop();
                        float dy2 = Pop();
                        float dx2 = Pop();
                        float dy1 = Pop();
                        float dx1 = Pop();

                        float c1x = _currentX + dx1;
                        float c1y = _currentY + dy1;
                        float c2x = c1x + dx2;
                        float c2y = c1y + dy2;
                        _currentX = c2x + dx3;
                        _currentY = c2y + dy3;

                        CurveTo(c1x, c1y, c2x, c2y, _currentX, _currentY);
                    }
                    break;

                case 9: // closepath
                    ClosePath();
                    break;

                case 10: // callsubr
                    if (_stack.Count >= 1 && _subrs != null)
                    {
                        int subrIndex = (int)Pop();
                        if (subrIndex >= 0 && subrIndex < _subrs.Count)
                        {
                            byte[] subrData = _subrs[subrIndex].ToArray();
                            Execute(subrData);
                        }
                    }
                    break;

                case 11: // return
                    // Return from subroutine - handled by call stack
                    break;

                case 13: // hsbw (horizontal sidebearing and width)
                    if (_stack.Count >= 2)
                    {
                        float wx = Pop();
                        float sbx = Pop();
                        _currentX = sbx;
                        _currentY = 0;
                        _outline.Width = wx;
                    }
                    break;

                case 14: // endchar
                    if (_pathStarted)
                    {
                        ClosePath();
                    }
                    break;

                case 21: // rmoveto
                    if (_stack.Count >= 2)
                    {
                        float dy = Pop();
                        float dx = Pop();
                        _currentX += dx;
                        _currentY += dy;
                        MoveTo(_currentX, _currentY);
                    }
                    break;

                case 22: // hmoveto
                    if (_stack.Count >= 1)
                    {
                        float dx = Pop();
                        _currentX += dx;
                        MoveTo(_currentX, _currentY);
                    }
                    break;

                case 30: // vhcurveto
                    if (_stack.Count >= 4)
                    {
                        float dx3 = Pop();
                        float dy2 = Pop();
                        float dx2 = Pop();
                        float dy1 = Pop();

                        float c1x = _currentX;
                        float c1y = _currentY + dy1;
                        float c2x = c1x + dx2;
                        float c2y = c1y + dy2;
                        _currentX = c2x + dx3;
                        _currentY = c2y;

                        CurveTo(c1x, c1y, c2x, c2y, _currentX, _currentY);
                    }
                    break;

                case 31: // hvcurveto
                    if (_stack.Count >= 4)
                    {
                        float dy3 = Pop();
                        float dy2 = Pop();
                        float dx2 = Pop();
                        float dx1 = Pop();

                        float c1x = _currentX + dx1;
                        float c1y = _currentY;
                        float c2x = c1x + dx2;
                        float c2y = c1y + dy2;
                        _currentX = c2x;
                        _currentY = c2y + dy3;

                        CurveTo(c1x, c1y, c2x, c2y, _currentX, _currentY);
                    }
                    break;

                default:
                    // Unknown command - clear stack
                    _stack.Clear();
                    break;
            }
        }

        private void ExecuteEscapeCommand(byte cmd)
        {
            switch (cmd)
            {
                case 0: // dotsection (hint control, ignore)
                    break;

                case 1: // vstem3 (3 vertical stem hints)
                    _stack.Clear();
                    break;

                case 2: // hstem3 (3 horizontal stem hints)
                    _stack.Clear();
                    break;

                case 6: // seac (standard encoding accented character)
                    // This creates composite characters - complex to implement fully
                    // For now, clear stack
                    _stack.Clear();
                    break;

                case 7: // sbw (sidebearing and width, 4 args)
                    if (_stack.Count >= 4)
                    {
                        float wy = Pop();
                        float wx = Pop();
                        float sby = Pop();
                        float sbx = Pop();
                        _currentX = sbx;
                        _currentY = sby;
                        _outline.Width = wx;
                        // Note: wy is vertical escapement, typically 0 for horizontal writing
                    }
                    break;

                case 12: // div
                    if (_stack.Count >= 2)
                    {
                        float b = Pop();
                        float a = Pop();
                        _stack.Push(b != 0 ? a / b : 0);
                    }
                    break;

                case 16: // callothersubr
                    // OtherSubrs are for flex hints and other special features
                    // Pop the subr number and argument count
                    if (_stack.Count >= 2)
                    {
                        int subrNum = (int)Pop();
                        int numArgs = (int)Pop();

                        // Handle known OtherSubrs
                        switch (subrNum)
                        {
                            case 0: // End flex
                                if (_flexPoints.Count >= 7 && _stack.Count >= 3)
                                {
                                    // Flex endpoint
                                    float epY = Pop();
                                    float epX = Pop();
                                    float flexDepth = Pop();

                                    // Create curves from flex points
                                    // _flexPoints should have 7 points for flex
                                    if (_flexPoints.Count >= 6)
                                    {
                                        // Two cubic bezier curves
                                        CurveTo(_flexPoints[1].X, _flexPoints[1].Y,
                                               _flexPoints[2].X, _flexPoints[2].Y,
                                               _flexPoints[3].X, _flexPoints[3].Y);
                                        CurveTo(_flexPoints[4].X, _flexPoints[4].Y,
                                               _flexPoints[5].X, _flexPoints[5].Y,
                                               _currentX + epX, _currentY + epY);
                                        _currentX += epX;
                                        _currentY += epY;
                                    }
                                }
                                _flexPoints.Clear();
                                _inFlex = false;
                                break;

                            case 1: // Start flex
                                _flexPoints.Clear();
                                _inFlex = true;
                                break;

                            case 2: // Add flex point
                                _flexPoints.Add(new PointF(_currentX, _currentY));
                                break;

                            case 3: // Replace hints (hint replacement)
                                // Push 3 back onto stack for pop
                                _stack.Push(3);
                                break;

                            default:
                                // Unknown OtherSubr - pop args
                                for (int j = 0; j < numArgs && _stack.Count > 0; j++)
                                    Pop();
                                break;
                        }
                    }
                    break;

                case 17: // pop
                    // Pop value from PostScript stack to Type1 stack
                    // In our implementation, just leave stack as-is since we don't have separate stacks
                    break;

                case 33: // setcurrentpoint
                    if (_stack.Count >= 2)
                    {
                        float y = Pop();
                        float x = Pop();
                        _currentX = x;
                        _currentY = y;
                    }
                    break;

                default:
                    // Unknown escape command
                    _stack.Clear();
                    break;
            }
        }

        private float Pop()
        {
            return _stack.Count > 0 ? _stack.Pop() : 0;
        }

        private void MoveTo(float x, float y)
        {
            if (_pathStarted)
            {
                // Implicit closepath before moveto in some interpretations
                // But typically Type1 uses explicit closepath
            }
            _outline.Commands.Add(new MoveToCommand(x, y));
            _pathStarted = true;
            _pathStartX = x;
            _pathStartY = y;
        }

        private void LineTo(float x, float y)
        {
            if (!_pathStarted)
            {
                MoveTo(_currentX, _currentY);
            }
            _outline.Commands.Add(new LineToCommand(x, y));
        }

        private void CurveTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            if (!_pathStarted)
            {
                MoveTo(_currentX, _currentY);
            }
            _outline.Commands.Add(new CubicBezierCommand(c1x, c1y, c2x, c2y, x, y));
        }

        private void ClosePath()
        {
            if (_pathStarted)
            {
                _outline.Commands.Add(new ClosePathCommand());
                _pathStarted = false;
            }
        }

        private void UpdateBoundingBox()
        {
            if (_outline == null || _outline.Commands.Count == 0)
                return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var cmd in _outline.Commands)
            {
                if (cmd is MoveToCommand m)
                {
                    UpdateBounds(m.Point.X, m.Point.Y, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (cmd is LineToCommand l)
                {
                    UpdateBounds(l.Point.X, l.Point.Y, ref minX, ref minY, ref maxX, ref maxY);
                }
                else if (cmd is CubicBezierCommand c)
                {
                    UpdateBounds(c.Control1.X, c.Control1.Y, ref minX, ref minY, ref maxX, ref maxY);
                    UpdateBounds(c.Control2.X, c.Control2.Y, ref minX, ref minY, ref maxX, ref maxY);
                    UpdateBounds(c.EndPoint.X, c.EndPoint.Y, ref minX, ref minY, ref maxX, ref maxY);
                }
            }

            if (minX != float.MaxValue)
            {
                _outline.MinX = minX;
                _outline.MinY = minY;
                _outline.MaxX = maxX;
                _outline.MaxY = maxY;
            }
        }

        private static void UpdateBounds(float x, float y, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }
}
