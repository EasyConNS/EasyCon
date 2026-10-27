namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>二值化之后的掩码处理：去除孤立杂点、只保留轮廓。</summary>
public sealed partial class InkMask
{
    /// <summary>
    /// 去掉总点数不超过 <paramref name="maxSize"/> 的墨点块（八方向相连算同一块）。
    /// 抖动后浅色区域零散的点基本都是这种小块，去掉后线段数与耗时明显下降；0 表示不处理。
    /// </summary>
    public InkMask RemoveSpecks(int maxSize)
    {
        if (maxSize <= 0)
            return this;
        bool[] ink = (bool[])_ink.Clone();
        bool[] seen = new bool[_ink.Length];
        List<int> block = [];
        Stack<int> pending = new();
        for (int start = 0; start < ink.Length; start++)
        {
            if (!_ink[start] || seen[start])
                continue;
            block.Clear();
            seen[start] = true;
            pending.Push(start);
            while (pending.Count > 0)
            {
                int i = pending.Pop();
                block.Add(i);
                int row = i / Width;
                int col = i % Width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int r = row + dy;
                        int c = col + dx;
                        if (r < 0 || r >= Height || c < 0 || c >= Width)
                            continue;
                        int j = r * Width + c;
                        if (_ink[j] && !seen[j])
                        {
                            seen[j] = true;
                            pending.Push(j);
                        }
                    }
                }
            }
            if (block.Count <= maxSize)
                foreach (int i in block)
                    ink[i] = false;
        }
        return new InkMask(Width, Height, ink);
    }

    /// <summary>
    /// 只保留轮廓：上下左右任一侧是空白（或画布边缘）的墨点才保留，实心区域的内部被挖空。
    /// 线条和单点本身就是轮廓，保持不变。
    /// </summary>
    public InkMask Outline()
    {
        bool[] ink = new bool[_ink.Length];
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                int i = row * Width + col;
                if (!_ink[i])
                    continue;
                bool inside = row > 0 && _ink[i - Width]
                    && row < Height - 1 && _ink[i + Width]
                    && col > 0 && _ink[i - 1]
                    && col < Width - 1 && _ink[i + 1];
                ink[i] = !inside;
            }
        }
        return new InkMask(Width, Height, ink);
    }
}