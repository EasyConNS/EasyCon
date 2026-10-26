using EasyCon2.Avalonia.Models;

namespace EasyCon2.Avalonia.DrawingBoard;

/// <summary>预览上的叠加显示：绘制过程中的游戏画面估计、光标位置，以及框选的绘制区域。</summary>
internal static class PreviewOverlay
{
    /// <summary>画笔模式下还没画到的墨点。</summary>
    internal static readonly (byte R, byte G, byte B) PendingInk = (190, 190, 190);
    /// <summary>橡皮擦模式下还没擦到的点（此时游戏里仍是黑色）。</summary>
    internal static readonly (byte R, byte G, byte B) PendingErase = (110, 110, 110);
    internal static readonly (byte R, byte G, byte B) Cursor = (230, 30, 30);
    internal static readonly (byte R, byte G, byte B) Region = (255, 140, 0);

    /// <summary>
    /// 按当前进度估计游戏里的画面：画笔模式下已画的墨点为黑、待画为浅灰、其余为白；
    /// 橡皮擦模式下（画布事先涂黑）已擦的点为白、待擦为深灰、其余为黑。再用红点与红框标出光标。
    /// </summary>
    /// <param name="strokes">要走笔的点（画笔模式是墨点，橡皮擦模式是要擦的点）。</param>
    /// <param name="done">已经走过笔的点，与 <paramref name="strokes"/> 同尺寸、按行展开。</param>
    public static PixelImage RenderProgress(InkMask strokes, bool[] done, bool eraser, (int Row, int Col)? cursor)
    {
        int width = strokes.Width;
        int height = strokes.Height;
        byte[] rgba = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int i = row * width + col;
                bool stroke = strokes[col, row];
                (byte R, byte G, byte B) color = (eraser, stroke, done[i]) switch
                {
                    (false, true, true) => (0, 0, 0),
                    (false, true, false) => PendingInk,
                    (false, false, _) => (255, 255, 255),
                    (true, true, true) => (255, 255, 255),
                    (true, true, false) => PendingErase,
                    (true, false, _) => (0, 0, 0),
                };
                Set(rgba, width, col, row, color);
            }
        }

        if (cursor is { } at)
        {
            // 光标点本身，加一圈 5×5 空心框，缩小显示时也看得见
            Set(rgba, width, at.Col, at.Row, Cursor);
            for (int d = -2; d <= 2; d++)
            {
                SetClipped(rgba, width, height, at.Col + d, at.Row - 2, Cursor);
                SetClipped(rgba, width, height, at.Col + d, at.Row + 2, Cursor);
                SetClipped(rgba, width, height, at.Col - 2, at.Row + d, Cursor);
                SetClipped(rgba, width, height, at.Col + 2, at.Row + d, Cursor);
            }
        }
        return new PixelImage(width, height, rgba);
    }

    /// <summary>在区域边界的一圈像素上叠一层半透明橙色，内容仍可辨认；不修改原图。</summary>
    public static PixelImage WithRegionOutline(PixelImage image, int startCol, int startRow, int colCount, int rowCount)
    {
        byte[] rgba = image.Rgba.ToArray();
        int endCol = Math.Min(image.Width, startCol + colCount) - 1;
        int endRow = Math.Min(image.Height, startRow + rowCount) - 1;
        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                if (row != startRow && row != endRow && col != startCol && col != endCol)
                    continue;
                int i = (row * image.Width + col) * 4;
                rgba[i] = Blend(rgba[i], Region.R);
                rgba[i + 1] = Blend(rgba[i + 1], Region.G);
                rgba[i + 2] = Blend(rgba[i + 2], Region.B);
                rgba[i + 3] = 255;
            }
        }
        return new PixelImage(image.Width, image.Height, rgba);
    }

    /// <summary>橙色占 70%。</summary>
    private static byte Blend(byte under, byte over) => (byte)((under * 3 + over * 7) / 10);

    private static void SetClipped(byte[] rgba, int width, int height, int col, int row, (byte R, byte G, byte B) color)
    {
        if (col >= 0 && col < width && row >= 0 && row < height)
            Set(rgba, width, col, row, color);
    }

    private static void Set(byte[] rgba, int width, int col, int row, (byte R, byte G, byte B) color)
    {
        int i = (row * width + col) * 4;
        rgba[i] = color.R;
        rgba[i + 1] = color.G;
        rgba[i + 2] = color.B;
        rgba[i + 3] = 255;
    }
}