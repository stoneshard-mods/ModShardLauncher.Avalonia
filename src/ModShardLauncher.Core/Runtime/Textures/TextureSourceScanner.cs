using ModShardLauncher.Mods;
using Serilog;
using SkiaSharp;

namespace ModShardLauncher.Runtime.Textures;

/// <summary>A PNG packed inside a mod package, decoded and ready to be laid out in an atlas.</summary>
public sealed class TextureSource
{
    public string Name = string.Empty;
    public byte[] Data = Array.Empty<byte>();
    public int Width;
    public int Height;
    public SKBitmap Bitmap = null!;
}

/// <summary>Finds every PNG inside a mod package (except its icon) and decodes it.</summary>
public static class TextureSourceScanner
{
    public static List<TextureSource> Scan(ModFile modFile, int maxSize)
    {
        List<TextureSource> result = new();

        foreach (FileChunk fileChunk in modFile.Files)
        {
            if (!fileChunk.name.EndsWith("png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // icon.png belongs to the mod card, not to the game's texture pages.
            if (fileChunk.name == modFile.Name + "\\" + "icon.png")
            {
                continue;
            }

            try
            {
                byte[] bytes = modFile.GetFile(fileChunk.name);
                SKBitmap bitmap = SKBitmap.Decode(bytes)
                    ?? throw new BadImageFormatException($"Cannot load the image {fileChunk.name}");

                if (bitmap.Width > maxSize || bitmap.Height > maxSize)
                {
                    bitmap.Dispose();
                    throw new BadImageFormatException($"Cannot load the image {fileChunk.name}");
                }

                result.Add(new TextureSource
                {
                    Name = fileChunk.name.Split("\\")[^1],
                    Data = bytes,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    Bitmap = bitmap,
                });
                Log.Information("Successfully load texture {0}", fileChunk.name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Something went wrong");
            }
        }

        return result;
    }
}
