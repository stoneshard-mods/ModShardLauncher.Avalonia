using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Name index for <see cref="UndertaleData.Code"/>.
/// <para>
/// <c>IList&lt;T&gt;.ByName</c> and the <c>First(t =&gt; t.Name.Content == name)</c> calls it replaces
/// are linear scans of ~34k entries (0.77 ms each on the Stoneshard data file), and the API resolves a
/// code entry on every <c>LoadGML</c>, table access and new event.
/// </para>
/// <para>
/// The lookup keeps the library's "first entry with this name wins" semantics and is rebuilt whenever
/// the code list changes size. A rebuild is needed rather than a tail-only append because the compiler
/// inserts anonymous child entries in the middle of the list.
/// </para>
/// </summary>
internal static class CodeIndex
{
    private static readonly object Gate = new();
    private static Dictionary<string, UndertaleCode>? _map;
    private static int _indexedCount = -1;

    /// <summary>The first code entry named <paramref name="name"/>, or null when there is none.</summary>
    internal static UndertaleCode? Find(string name)
    {
        lock (Gate)
        {
            SyncCore();
            return _map!.TryGetValue(name, out UndertaleCode? code) ? code : null;
        }
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            _map = null;
            _indexedCount = -1;
        }
    }

    private static void SyncCore()
    {
        if (!MslContext.HasData)
        {
            throw new InvalidOperationException("No game data (data.win) is currently loaded.");
        }

        IList<UndertaleCode> codes = MslContext.Data.Code;
        if (_map is not null && _indexedCount == codes.Count)
        {
            return;
        }

        _map = new Dictionary<string, UndertaleCode>(codes.Count, StringComparer.Ordinal);
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i]?.Name?.Content is string name)
            {
                _map.TryAdd(name, codes[i]);
            }
        }

        _indexedCount = codes.Count;
    }
}

/// <summary>
/// Name index for <see cref="UndertaleData.Sprites"/>; same reasoning as <see cref="CodeIndex"/>
/// (17k sprites, 0.66 ms per linear lookup, and texture injection resolves one per injected frame).
/// <para>
/// Sprites are only ever appended by this launcher, so growth is indexed incrementally and a shrink
/// rebuilds.
/// </para>
/// </summary>
internal static class SpriteIndex
{
    private static readonly object Gate = new();
    private static Dictionary<string, UndertaleSprite>? _map;
    private static int _indexedCount = -1;

    /// <summary>The first sprite named <paramref name="name"/>, or null when there is none.</summary>
    internal static UndertaleSprite? Find(string name)
    {
        lock (Gate)
        {
            SyncCore();
            return _map!.TryGetValue(name, out UndertaleSprite? sprite) ? sprite : null;
        }
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            _map = null;
            _indexedCount = -1;
        }
    }

    private static void SyncCore()
    {
        if (!MslContext.HasData)
        {
            throw new InvalidOperationException("No game data (data.win) is currently loaded.");
        }

        IList<UndertaleSprite> sprites = MslContext.Data.Sprites;
        if (_map is null || _indexedCount > sprites.Count)
        {
            _map = new Dictionary<string, UndertaleSprite>(sprites.Count, StringComparer.Ordinal);
            _indexedCount = 0;
        }

        for (int i = _indexedCount; i < sprites.Count; i++)
        {
            if (sprites[i]?.Name?.Content is string name)
            {
                _map.TryAdd(name, sprites[i]);
            }
        }

        _indexedCount = sprites.Count;
    }
}
