using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Content index for <see cref="UndertaleData.Strings"/>.
/// <para>
/// The library's <c>MakeString</c> is a linear scan of the whole pool: 122k entries and ~4.2 ms per
/// call on the Stoneshard data file. The API interns a string for every object, code entry, sprite,
/// room layer and texture page item it creates, so those scans add up quickly. This index answers the
/// same question in O(1) while keeping the library's exact semantics: the first entry with the
/// requested content wins, a missing content appends exactly one entry.
/// </para>
/// <para>
/// Only the pool's tail is tracked: entries appended by anyone (the library included) are picked up
/// by comparing the pool's count, so external additions can never be duplicated. In-place edits of an
/// existing entry's <c>Content</c> are not observed, which no code path in this launcher performs.
/// </para>
/// </summary>
internal static class StringPool
{
    private static readonly object Gate = new();
    private static Dictionary<string, int>? _index;
    private static Dictionary<string, int>? _lastIndex;
    private static int _indexedCount;

    /// <summary>Returns the pool entry for <paramref name="content"/>, appending one when missing.</summary>
    internal static UndertaleString Intern(string content) => Intern(content, out _);

    /// <summary>Same as <see cref="Intern(string)"/>, also reporting the pool index of the entry.</summary>
    internal static UndertaleString Intern(string content, out int index)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        lock (Gate)
        {
            SyncCore();
            if (_index!.TryGetValue(content, out index))
            {
                return MslContext.Data.Strings[index];
            }

            UndertaleString created = new(content);
            MslContext.Data.Strings.Add(created);
            index = MslContext.Data.Strings.Count - 1;
            _index[content] = index;
            _lastIndex![content] = index;
            _indexedCount = MslContext.Data.Strings.Count;
            return created;
        }
    }

    /// <summary>
    /// Returns the <b>last</b> pool entry with <paramref name="content"/> (appending one when missing).
    /// <para>
    /// That is how the GML compiler binds string literals: it prepares a content→entry map for the
    /// compile and a later duplicate overwrites the earlier one. Only needed where the exact
    /// instruction stream of a script has to match what a compile would produce (the table writer);
    /// everything else uses <see cref="Intern(string)"/>, which keeps the first entry, like
    /// <c>MakeString</c>.
    /// </para>
    /// </summary>
    internal static UndertaleString InternLast(string content, out int index)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        lock (Gate)
        {
            SyncCore();
            if (_lastIndex!.TryGetValue(content, out index))
            {
                return MslContext.Data.Strings[index];
            }

            UndertaleString created = new(content);
            MslContext.Data.Strings.Add(created);
            index = MslContext.Data.Strings.Count - 1;
            _index!.TryAdd(content, index);
            _lastIndex[content] = index;
            _indexedCount = MslContext.Data.Strings.Count;
            return created;
        }
    }

    /// <summary>Whether the pool already contains <paramref name="content"/> (no allocation).</summary>
    internal static bool Contains(string content)
    {
        if (content is null)
        {
            return false;
        }

        lock (Gate)
        {
            SyncCore();
            return _index!.ContainsKey(content);
        }
    }

    /// <summary>Drops the index; called when another data file is attached.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _index = null;
            _lastIndex = null;
            _indexedCount = 0;
        }
    }

    private static void SyncCore()
    {
        if (!MslContext.HasData)
        {
            throw new InvalidOperationException("No game data (data.win) is currently loaded.");
        }

        IList<UndertaleString> strings = MslContext.Data.Strings;
        if (_index is null || _lastIndex is null || _indexedCount > strings.Count)
        {
            _index = new Dictionary<string, int>(strings.Count, StringComparer.Ordinal);
            _lastIndex = new Dictionary<string, int>(strings.Count, StringComparer.Ordinal);
            _indexedCount = 0;
        }

        // Appended entries only: their pool position is their index. The first occurrence is what
        // MakeString's linear search returns; the last one is what the GML compiler binds literals to.
        for (int i = _indexedCount; i < strings.Count; i++)
        {
            if (strings[i]?.Content is string content)
            {
                _index.TryAdd(content, i);
                _lastIndex[content] = i;
            }
        }

        _indexedCount = strings.Count;
    }
}
