using System.Text.RegularExpressions;
using ModShardLauncher.Mods;
using Serilog;
using SkiaSharp;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace ModShardLauncher.Runtime.Textures;

/// <summary>
/// Turns the PNGs of a mod package into embedded textures, texture page items, sprites and
/// collision masks. This is the SkiaSharp replacement of the old System.Drawing TextureLoader.
/// </summary>
public sealed class TextureInjector
{
    private const int AtlasSize = 2048;
    private const int Padding = 2;

    private static readonly Regex SpriteFrameRegex = new(@"^(.+?)(?:_(-*\d+))*$", RegexOptions.Compiled);

    private readonly TextureAtlasBuilder _builder;

    public TextureInjector(TextureAtlasBuilder? builder = null)
    {
        _builder = builder ?? new TextureAtlasBuilder(AtlasSize, Padding);
    }

    /// <summary>Injects every texture of <paramref name="mod"/> into the currently attached game data.</summary>
    public void Inject(ModFile mod)
    {
        using PatchTimings.Scope timing = PatchTimings.Measure($"textures:{mod.Name}");

        List<TextureSource> sources = TextureSourceScanner.Scan(mod, AtlasSize);
        try
        {
            foreach (TextureAtlas atlas in _builder.Build(sources))
            {
                InjectAtlas(mod, atlas);
            }
        }
        finally
        {
            foreach (TextureSource source in sources)
            {
                source.Bitmap.Dispose();
            }
        }
    }

    private void InjectAtlas(ModFile mod, TextureAtlas atlas)
    {
        using SKBitmap composed = TextureAtlasBuilder.Compose(atlas);
        byte[] png = EncodePng(composed);

        UndertaleEmbeddedTexture embeddedTexture = new()
        {
            Name = StringPool.Intern(mod.Name),
        };
        embeddedTexture.TextureData.Image = GMImage.FromPng(png);
        MslContext.Data.EmbeddedTextures.Add(embeddedTexture);

        foreach (TextureNode node in atlas.Nodes)
        {
            if (node.Texture is null)
            {
                continue;
            }

            UndertaleTexturePageItem pageItem = Msl.CreateTexureItem(
                embeddedTexture,
                new RectTexture((ushort)node.X, (ushort)node.Y, (ushort)node.Width, (ushort)node.Height),
                new RectTexture(0, 0, (ushort)node.Width, (ushort)node.Height),
                new BoundingData<ushort>((ushort)node.Width, (ushort)node.Height));
            MslContext.Data.TexturePageItems.Add(pageItem);

            // Frame numbers are encoded in the file name: sprite_2.png -> sprite, frame 2.
            string stripped = Path.GetFileNameWithoutExtension(node.Texture.Name);
            string spriteName;
            int frame;
            try
            {
                System.Text.RegularExpressions.Match parts = SpriteFrameRegex.Match(stripped);
                spriteName = parts.Groups[1].Value;
                int.TryParse(parts.Groups[2].Value, out frame);
            }
            catch
            {
                continue;
            }

            UndertaleSprite? sprite = SpriteIndex.Find(spriteName);
            UndertaleSprite.TextureEntry textureEntry = new() { Texture = pageItem };

            if (sprite is null)
            {
                AddNewSprite(composed, node, spriteName, frame, textureEntry);
                continue;
            }

            // The sprite exists: write the texture at the requested frame, padding missing frames.
            if (frame > sprite.Textures.Count - 1)
            {
                while (frame > sprite.Textures.Count - 1)
                {
                    sprite.Textures.Add(textureEntry);
                }

                continue;
            }

            sprite.Textures[frame] = textureEntry;
        }
    }

    private static void AddNewSprite(SKBitmap atlas, TextureNode node, string spriteName, int frame,
        UndertaleSprite.TextureEntry textureEntry)
    {
        UndertaleSprite newSprite = Msl.CreateSpriteNoCollisionMasks(
            spriteName,
            new MarginData(0, node.Height - 1, 0, node.Width - 1),
            new OriginData(0, 0),
            new BoundingData<uint>((uint)node.Width, (uint)node.Height));

        // Reserve the frames before the one we are about to write.
        for (int i = 0; i < frame; i++)
        {
            newSprite.Textures.Add(null!);
        }

        newSprite.CollisionMasks.Add(newSprite.NewMaskEntry());

        byte[] mask = SpriteCollisionMask.Build(atlas, node.X, node.Y, node.Width, node.Height);
        for (int i = 0; i < mask.Length; i++)
        {
            newSprite.CollisionMasks[0].Data[i] = mask[i];
        }

        newSprite.Textures.Add(textureEntry);
        MslContext.Data.Sprites.Add(newSprite);
        Log.Information("Successfully added sprite {0}", spriteName);
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
