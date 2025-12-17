using System.Collections.Generic;

namespace ImageLibrary.Jp2.Tier2;

/// <summary>
/// Tag tree for encoding/decoding inclusion information and zero bit-planes.
/// Used in JPEG2000 packet headers to efficiently signal code-block metadata.
/// </summary>
internal class TagTree
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _numLevels;
    private readonly int[][] _values;
    private readonly int[][] _states;

    /// <summary>
    /// Creates a new tag tree with the specified dimensions.
    /// </summary>
    /// <param name="width">Number of columns (code-blocks in x direction).</param>
    /// <param name="height">Number of rows (code-blocks in y direction).</param>
    public TagTree(int width, int height)
    {
        _width = width;
        _height = height;

        // Calculate number of levels
        int w = width;
        int h = height;
        _numLevels = 0;
        while (w > 1 || h > 1)
        {
            _numLevels++;
            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
        }
        _numLevels++; // Include the root level

        // Allocate arrays for each level
        _values = new int[_numLevels][];
        _states = new int[_numLevels][];

        w = width;
        h = height;
        for (var level = 0; level < _numLevels; level++)
        {
            int size = w * h;
            _values[level] = new int[size];
            _states[level] = new int[size];

            // Initialize values to int.MaxValue (unknown)
            for (var i = 0; i < size; i++)
            {
                _values[level][i] = int.MaxValue;
                _states[level][i] = 0;
            }

            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
            if (w == 0) w = 1;
            if (h == 0) h = 1;
        }
    }

    /// <summary>
    /// Resets the tag tree state for a new layer.
    /// </summary>
    public void Reset()
    {
        for (var level = 0; level < _numLevels; level++)
        {
            for (var i = 0; i < _states[level].Length; i++)
            {
                _states[level][i] = 0;
            }
        }
    }

    /// <summary>
    /// Sets the value at a leaf node.
    /// </summary>
    public void SetValue(int x, int y, int value)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;

        int index = y * _width + x;
        _values[0][index] = value;

        // Update parent nodes
        int w = _width;
        int h = _height;
        int px = x;
        int py = y;

        for (var level = 0; level < _numLevels - 1; level++)
        {
            int parentX = px >> 1;
            int parentY = py >> 1;
            int parentW = (w + 1) >> 1;
            int parentIndex = parentY * parentW + parentX;

            // Parent value is minimum of children
            var minVal = int.MaxValue;
            for (int cy = parentY * 2; cy <= parentY * 2 + 1 && cy < h; cy++)
            {
                for (int cx = parentX * 2; cx <= parentX * 2 + 1 && cx < w; cx++)
                {
                    int childIndex = cy * w + cx;
                    if (_values[level][childIndex] < minVal)
                        minVal = _values[level][childIndex];
                }
            }

            _values[level + 1][parentIndex] = minVal;

            px = parentX;
            py = parentY;
            w = parentW;
            h = (h + 1) >> 1;
            if (h == 0) h = 1;
        }
    }

    /// <summary>
    /// Decodes a value from the bitstream for the specified leaf node.
    /// Returns true if the value is less than or equal to the threshold.
    /// </summary>
    public bool Decode(BitReader reader, int x, int y, int threshold)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return false;

        int leafIndex = y * _width + x;

        // Already decoded this leaf?
        if (_values[0][leafIndex] <= threshold)
            return true;

        // Traverse from root to leaf
        var stack = new Stack<(int level, int x, int y)>();
        int px = x, py = y;
        int w = _width, h = _height;

        // Build path from leaf to root
        for (var level = 0; level < _numLevels; level++)
        {
            stack.Push((level, px, py));
            px >>= 1;
            py >>= 1;
        }

        // Pop stack and decode from root to leaf
        var currentMin = 0;
        w = 1;
        h = 1;

        while (stack.Count > 0)
        {
            (int level, int cx, int cy) = stack.Pop();

            // Calculate width at this level
            int levelW = _width;
            int levelH = _height;
            for (var l = 0; l < level; l++)
            {
                levelW = (levelW + 1) >> 1;
                levelH = (levelH + 1) >> 1;
            }
            if (level > 0)
            {
                levelW = (_width + (1 << level) - 1) >> level;
                levelH = (_height + (1 << level) - 1) >> level;
            }
            else
            {
                levelW = _width;
                levelH = _height;
            }

            int index = cy * levelW + cx;
            if (index >= _values[level].Length)
                continue;

            // Start from the state (last decoded value + 1 for this node)
            int state = _states[level][index];
            if (state > currentMin)
                currentMin = state;

            // If already know the value is > threshold, skip
            if (_values[level][index] <= threshold)
            {
                currentMin = _values[level][index];
                continue;
            }

            // Decode bits until we get a 1 or exceed threshold
            while (currentMin <= threshold)
            {
                if (reader.ReadBit() == 1)
                {
                    _values[level][index] = currentMin;
                    break;
                }
                currentMin++;
            }

            _states[level][index] = currentMin;

            if (_values[level][index] > threshold)
            {
                return false;
            }
        }

        return _values[0][leafIndex] <= threshold;
    }

    /// <summary>
    /// Decodes a value without threshold checking.
    /// Returns the decoded value for the specified leaf node.
    /// </summary>
    public int DecodeValue(BitReader reader, int x, int y)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return 0;

        int leafIndex = y * _width + x;

        // Already decoded?
        if (_values[0][leafIndex] < int.MaxValue)
            return _values[0][leafIndex];

        // Build path from leaf to root
        var path = new List<(int level, int index, int levelW)>();
        int px = x, py = y;

        for (var level = 0; level < _numLevels; level++)
        {
            int levelW = _width >> level;
            if (levelW == 0) levelW = 1;
            int levelH = _height >> level;
            if (levelH == 0) levelH = 1;

            // Recalculate properly
            levelW = _width;
            levelH = _height;
            for (var l = 0; l < level; l++)
            {
                levelW = (levelW + 1) >> 1;
                levelH = (levelH + 1) >> 1;
            }

            int index = py * levelW + px;
            if (index < _values[level].Length)
            {
                path.Add((level, index, levelW));
            }
            px >>= 1;
            py >>= 1;
        }

        // Decode from root to leaf
        var currentMin = 0;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            (int level, int index, int levelW) = path[i];

            int state = _states[level][index];
            if (state > currentMin)
                currentMin = state;

            if (_values[level][index] < int.MaxValue)
            {
                currentMin = _values[level][index];
                continue;
            }

            // Decode until we hit a 1 bit
            while (reader.ReadBit() == 0)
            {
                currentMin++;
            }

            _values[level][index] = currentMin;
            _states[level][index] = currentMin + 1;
        }

        return _values[0][leafIndex];
    }
}