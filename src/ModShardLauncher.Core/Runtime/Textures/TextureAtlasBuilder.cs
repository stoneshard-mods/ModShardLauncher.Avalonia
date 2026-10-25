using SkiaSharp;

namespace ModShardLauncher.Runtime.Textures;

public enum SplitType
{
    Horizontal,
    Vertical,
}

public enum BestFitHeuristic
{
    Area,
    MaxOneAxis,
}

/// <summary>A rectangle of the atlas. Mutable because the packing algorithm splits it in place.</summary>
public sealed class TextureNode
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public TextureSource? Texture;
    public SplitType SplitType;
}

public sealed class TextureAtlas
{
    public int Width;
    public int Height;
    public List<TextureNode> Nodes = new();
}

/// <summary>
/// Shelf-less binary tree packer ported from the original TextureLoader. It keeps the exact same
/// heuristics and split order so patched texture pages remain identical to the WPF build.
/// </summary>
public sealed class TextureAtlasBuilder
{
    private readonly int _atlasSize;
    private readonly int _padding;
    private readonly BestFitHeuristic _heuristic;

    public TextureAtlasBuilder(int atlasSize = 2048, int padding = 2, BestFitHeuristic heuristic = BestFitHeuristic.Area)
    {
        _atlasSize = atlasSize;
        _padding = padding;
        _heuristic = heuristic;
    }

    public List<TextureAtlas> Build(IReadOnlyList<TextureSource> textures)
    {
        List<TextureAtlas> atlases = new();
        List<TextureSource> remaining = textures.ToList();

        while (remaining.Count > 0)
        {
            TextureAtlas atlas = new() { Width = _atlasSize, Height = _atlasSize };
            List<TextureSource> leftovers = LayoutAtlas(remaining, atlas);

            if (leftovers.Count == 0)
            {
                // Try to shrink the final atlas as much as possible, then step back once.
                while (leftovers.Count == 0)
                {
                    atlas.Width /= 2;
                    atlas.Height /= 2;
                    leftovers = LayoutAtlas(remaining, atlas);
                }

                atlas.Width *= 2;
                atlas.Height *= 2;
                leftovers = LayoutAtlas(remaining, atlas);
            }

            atlases.Add(atlas);
            remaining = leftovers;
        }

        return atlases;
    }

    /// <summary>Rasterizes the atlas; empty nodes get the original debug magenta fill.</summary>
    public static SKBitmap Compose(TextureAtlas atlas)
    {
        SKBitmap image = new(atlas.Width, atlas.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(image);
        canvas.Clear(SKColors.Transparent);

        foreach (TextureNode node in atlas.Nodes)
        {
            SKRect destination = new(node.X, node.Y, node.X + node.Width, node.Y + node.Height);
            if (node.Texture is not null)
            {
                canvas.DrawBitmap(node.Texture.Bitmap, destination);
            }
            else
            {
                using SKPaint paint = new() { Color = SKColors.DarkMagenta };
                canvas.DrawRect(destination, paint);
            }
        }

        return image;
    }

    private List<TextureSource> LayoutAtlas(List<TextureSource> textures, TextureAtlas atlas)
    {
        List<TextureNode> freeList = new();
        atlas.Nodes = new List<TextureNode>();
        List<TextureSource> texturesLeft = textures.ToList();

        TextureNode root = new()
        {
            X = 0,
            Y = 0,
            Width = atlas.Width,
            Height = atlas.Height,
            SplitType = SplitType.Horizontal,
        };
        freeList.Add(root);

        while (freeList.Count > 0 && texturesLeft.Count > 0)
        {
            TextureNode node = freeList[0];
            freeList.RemoveAt(0);

            TextureSource? bestFit = FindBestFitForNode(node, texturesLeft);
            if (bestFit is not null)
            {
                if (node.SplitType == SplitType.Horizontal)
                {
                    HorizontalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }
                else
                {
                    VerticalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }

                node.Texture = bestFit;
                node.Width = bestFit.Width;
                node.Height = bestFit.Height;
                texturesLeft.Remove(bestFit);
            }

            atlas.Nodes.Add(node);
        }

        return texturesLeft;
    }

    private TextureSource? FindBestFitForNode(TextureNode node, List<TextureSource> textures)
    {
        TextureSource? bestFit = null;
        float nodeArea = node.Width * node.Height;
        float maxCriteria = 0.0f;

        foreach (TextureSource texture in textures)
        {
            switch (_heuristic)
            {
                case BestFitHeuristic.MaxOneAxis:
                    if (MaxOneAxisFit(node, texture, out float ratio) && ratio > maxCriteria)
                    {
                        maxCriteria = ratio;
                        bestFit = texture;
                    }

                    break;

                case BestFitHeuristic.Area:
                    if (AreaFit(node, texture, nodeArea, out float coverage) && coverage > maxCriteria)
                    {
                        maxCriteria = coverage;
                        bestFit = texture;
                    }

                    break;
            }
        }

        return bestFit;
    }

    private static bool MaxOneAxisFit(TextureNode node, TextureSource texture, out float ratio)
    {
        if (texture.Width <= node.Width && texture.Height <= node.Height)
        {
            float widthRatio = texture.Width / (float)node.Width;
            float heightRatio = texture.Height / (float)node.Height;
            ratio = widthRatio > heightRatio ? widthRatio : heightRatio;
            return true;
        }

        ratio = 0.0f;
        return false;
    }

    private static bool AreaFit(TextureNode node, TextureSource texture, float nodeArea, out float coverage)
    {
        if (texture.Width <= node.Width && texture.Height <= node.Height)
        {
            coverage = texture.Width * texture.Height / nodeArea;
            return true;
        }

        coverage = 0.0f;
        return false;
    }

    private void HorizontalSplit(TextureNode toSplit, int width, int height, List<TextureNode> freeList)
    {
        TextureNode right = new()
        {
            X = toSplit.X + width + _padding,
            Y = toSplit.Y,
            Width = toSplit.Width - width - _padding,
            Height = height,
            SplitType = SplitType.Vertical,
        };
        TextureNode bottom = new()
        {
            X = toSplit.X,
            Y = toSplit.Y + height + _padding,
            Width = toSplit.Width,
            Height = toSplit.Height - height - _padding,
            SplitType = SplitType.Horizontal,
        };

        if (right.Width > 0 && right.Height > 0)
        {
            freeList.Add(right);
        }

        if (bottom.Width > 0 && bottom.Height > 0)
        {
            freeList.Add(bottom);
        }
    }

    private void VerticalSplit(TextureNode toSplit, int width, int height, List<TextureNode> freeList)
    {
        TextureNode right = new()
        {
            X = toSplit.X + width + _padding,
            Y = toSplit.Y,
            Width = toSplit.Width - width - _padding,
            Height = toSplit.Height,
            SplitType = SplitType.Vertical,
        };
        TextureNode bottom = new()
        {
            X = toSplit.X,
            Y = toSplit.Y + height + _padding,
            Width = width,
            Height = toSplit.Height - height - _padding,
            SplitType = SplitType.Horizontal,
        };

        if (right.Width > 0 && right.Height > 0)
        {
            freeList.Add(right);
        }

        if (bottom.Width > 0 && bottom.Height > 0)
        {
            freeList.Add(bottom);
        }
    }
}
