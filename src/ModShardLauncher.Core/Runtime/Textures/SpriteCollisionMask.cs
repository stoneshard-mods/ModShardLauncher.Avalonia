using System.Collections;
using SkiaSharp;

namespace ModShardLauncher.Runtime.Textures;

/// <summary>
/// Builds the 1-bit collision mask Stoneshard expects. The bit scrambling is a faithful port of
/// the original GDI+ implementation so existing sprites keep identical masks.
/// </summary>
public static class SpriteCollisionMask
{
    public static byte[] Build(SKBitmap atlas, int originX, int originY, int width, int height)
    {
        int correctedWidth = (width + 7) / 8 * 8;
        BitArray masking = new(correctedWidth * height);
        BitArray transposed = new(correctedWidth * height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                masking[y * correctedWidth + x] = atlas.GetPixel(originX + x, originY + y).Alpha > 0;
            }
        }

        for (int i = 0; i < masking.Length; i += 8)
        {
            for (int j = 0; j < 8; j++)
            {
                transposed[j + i] = masking[-(j - 7) + i];
            }
        }

        byte[] bytes = new byte[masking.Length / 8];
        transposed.CopyTo(bytes, 0);
        return bytes;
    }
}
