using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Batches writes to the semicolon table scripts (<c>gml_GlobalScript_table_*</c>).
/// <para>
/// Writing a table used to recompile the whole script on every call, and the weapon API calls it
/// up to three times per weapon (weapons row, equipment name/description/gender, equipment rename),
/// so adding N weapons paid 3N full GML compiles of the two largest scripts in the game. The final
/// script text only depends on the final row list, so the writes are collected here and compiled
/// once per <see cref="GameTableCodec.SetTable"/> batch.
/// </para>
/// <para>
/// Ordering guarantees: a read of the affected code entry flushes first
/// (<see cref="FlushForCode"/> is called from <see cref="Msl.GetUMTCodeFromFile"/> and
/// <see cref="MslContext.DecompileText"/>), and <see cref="PatchRunner"/> flushes after every mod,
/// after the pipeline and before saving. A mod that inspects the raw <c>UndertaleData</c> instead
/// of using the API sees the previous content until the flush.
/// </para>
/// </summary>
internal static class TableDraftStore
{
    private sealed class Draft
    {
        public UndertaleCode Code = null!;
        public List<string> Rows = new();
        public bool Dirty;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Draft> Drafts = new(StringComparer.Ordinal);
    private static bool _flushing;

    /// <summary>Drops every draft; belongs to one loaded data file / one patch run.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            Drafts.Clear();
            _flushing = false;
        }
    }

    /// <summary>
    /// Copy of the rows currently known for <paramref name="name"/>, if the table was already read
    /// or written in this run. The copy keeps the previous contract of <see cref="GameTableCodec.GetTable"/>:
    /// callers own the list they get back and must call <see cref="GameTableCodec.SetTable"/> to persist it.
    /// </summary>
    internal static bool TryGetRows(string name, out List<string> rows)
    {
        lock (Gate)
        {
            if (Drafts.TryGetValue(name, out Draft? draft))
            {
                rows = new List<string>(draft.Rows);
                return true;
            }
        }

        rows = new List<string>();
        return false;
    }

    /// <summary>Remembers the rows read from <paramref name="code"/> so later writes reuse them.</summary>
    internal static void Track(string name, UndertaleCode code, List<string> rows)
    {
        lock (Gate)
        {
            if (Drafts.TryGetValue(name, out Draft? existing))
            {
                // A queued write is newer than the script text it was read from: never overwrite it.
                if (!existing.Dirty)
                {
                    existing.Code = code;
                    existing.Rows = rows;
                }

                return;
            }

            Drafts[name] = new Draft { Code = code, Rows = rows };
        }
    }

    /// <summary>Queues <paramref name="rows"/> as the new content of the table; nothing is compiled yet.</summary>
    internal static void Set(string name, List<string> rows)
    {
        lock (Gate)
        {
            if (!Drafts.TryGetValue(name, out Draft? draft))
            {
                // Same failure mode as the previous implementation: a missing table script throws.
                draft = new Draft
                {
                    Code = CodeIndex.Find(name) ?? MslContext.Data.Code.First(t => t.Name?.Content == name),
                };
                Drafts[name] = draft;
            }

            // Intern the row strings when the write is queued (not at compile time): the previous
            // implementation compiled after every single write, so the string table received the
            // new rows in the order the mod produced them. Keeping that layout keeps the output
            // file of a batched run identical to the unbatched one up to string identity.
            InternRows(rows);

            draft.Rows = rows;
            draft.Dirty = true;
        }
    }

    /// <summary>
    /// Adds the row strings the string table does not contain yet, at the moment the write is
    /// queued instead of at compile time.
    /// <para>
    /// The string pool has to be updated when the write is queued, not when the batch is flushed:
    /// without this a row added by mod A would only become visible to mod B's string lookups after
    /// the flush. The insertion order and multiplicity also matter, because they decide the string
    /// table layout of the output file: the GML compiler interns a script's literals while it emits
    /// the array pushes (so <b>reverse</b> row order) and creates one entry per literal, without
    /// deduplicating literals of the same write (a table whose name and description row are
    /// textually identical produced two entries). Reproducing both keeps a batched run's string
    /// table identical to the previous one-write-one-compile implementation.
    /// </para>
    /// <para>
    /// <see cref="MslContext.Data"/>'s string pool is a flat list (<c>MakeString</c> is a linear
    /// search), so the "already present?" check goes through <see cref="StringPool"/>.
    /// </para>
    /// </summary>
    private static void InternRows(List<string> rows)
    {
        if (!MslContext.HasData || rows.Count == 0)
        {
            return;
        }

        // Presence is decided before this batch appends anything: the compiler created one entry per
        // literal occurrence, so two identical rows of the same write both got their own entry.
        List<string>? missing = null;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            string? row = rows[i];
            if (row is null || StringPool.Contains(row))
            {
                continue;
            }

            (missing ??= new List<string>()).Add(row);
        }

        if (missing is null)
        {
            return;
        }

        IList<UndertaleString> strings = MslContext.Data.Strings;
        foreach (string row in missing)
        {
            strings.Add(new UndertaleString(row));
        }
    }

    /// <summary>Flushes the draft of <paramref name="code"/>, if that entry has a pending table write.</summary>
    internal static void FlushForCode(UndertaleCode? code)
    {
        if (code is null)
        {
            return;
        }

        lock (Gate)
        {
            if (_flushing || Drafts.Count == 0)
            {
                return;
            }

            foreach ((string name, Draft draft) in Drafts)
            {
                if (draft.Dirty && ReferenceEquals(draft.Code, code))
                {
                    Write(name, draft);
                    return;
                }
            }
        }
    }

    /// <summary>Compiles every pending table once. Cheap when nothing is dirty.</summary>
    internal static void FlushAll()
    {
        lock (Gate)
        {
            if (_flushing || Drafts.Count == 0)
            {
                return;
            }

            foreach ((string name, Draft draft) in Drafts)
            {
                if (draft.Dirty)
                {
                    Write(name, draft);
                }
            }
        }
    }

    /// <summary>
    /// Applies one table: the array literal is rebuilt at the instruction level when the script has
    /// the expected shape, otherwise the table is compiled from its decompiled text (the previous
    /// behaviour). The <c>table:&lt;name&gt;</c> timing stage measures both paths.
    /// </summary>
    private static void Write(string name, Draft draft)
    {
        if (!MslContext.HasData)
        {
            draft.Dirty = false;
            return;
        }

        _flushing = true;
        try
        {
            using PatchTimings.Scope timing = PatchTimings.Measure($"table:{name}");

            if (!TableInstructionWriter.TryReplace(draft.Code, draft.Rows))
            {
                // Fallback: decompile the script as it is now and recompile it with the new array.
                Log.Debug("Table {Name} has an unexpected shape; compiling it from GML instead", name);
                string text = MslContext.DecompileText(draft.Code);
                string serialized = JsonConvert.SerializeObject(draft.Rows).Replace("\n", "");
                string replaced = Regex.Replace(text, "\\[.*\\]", serialized);
                GmlCompiler.ReplaceGml(draft.Code, replaced);
            }

            MslContext.InvalidateDecompiledText(draft.Code);
            draft.Dirty = false;
            Log.Information("Successfully set table: {0}", name);
        }
        finally
        {
            _flushing = false;
        }
    }
}
