using Serilog;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Reads and writes the semicolon separated lookup tables that Stoneshard stores as
/// <c>gml_GlobalScript_table_*</c> script bodies (a flat list of string pushes).
/// </summary>
public static class GameTableCodec
{
    /// <summary>Loads the two tables that are commonly queried by the API (weapons / equipment).</summary>
    public static void Initialize()
    {
        // Storage lives on the legacy ModLoader fields so old mods observe the same lists.
        ModLoader.Weapons = Msl.ThrowIfNull(GetTable("gml_GlobalScript_table_weapons"));
        ModLoader.WeaponDescriptions = Msl.ThrowIfNull(GetTable("gml_GlobalScript_table_equipment"));
    }

    /// <summary>Returns the table stored in the script <paramref name="name"/>, or null when it does not exist.</summary>
    public static List<string>? GetTable(string name)
    {
        try
        {
            // A table that was already read or written in this run is served from its draft, so a
            // batched write is visible to the next reader instead of the not-yet-compiled script.
            if (TableDraftStore.TryGetRows(name, out List<string> tracked))
            {
                return tracked;
            }

            UndertaleCode table = CodeIndex.Find(name)
                ?? MslContext.Data.Code.First(t => t.Name.Content == name);
            List<string> rows = table.Instructions
                .Where(AssemblyWrapper.IsPushString)
                .Select(i => (i.Value as UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>)!.Resource.Content)
                .Reverse()
                .ToList();

            TableDraftStore.Track(name, table, rows);
            return new List<string>(rows);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong");
            throw;
        }
    }

    /// <summary>
    /// Queues <paramref name="table"/> as the new content of the script <paramref name="name"/>.
    /// The script is recompiled once per patch batch instead of once per call (see
    /// <see cref="TableDraftStore"/>); reads of the script flush first, so the table is never
    /// observable in a stale state through the public API.
    /// The legacy snapshot lists (<see cref="ModLoader.Weapons"/> / <see cref="ModLoader.WeaponDescriptions"/>)
    /// are pointed at the list that was just queued, so <see cref="Msl.SetWeapon"/> and friends never
    /// observe a stale copy of the table.
    /// </summary>
    public static void SetTable(List<string> table, string name)
    {
        TableDraftStore.Set(name, table);
        SyncSnapshot(table, name);
        Log.Debug("Queued table write for {0}", name);
    }

    /// <summary>Keeps the public table snapshots in step with what was written to the data file.</summary>
    private static void SyncSnapshot(List<string> table, string name)
    {
        if (name == "gml_GlobalScript_table_weapons")
        {
            ModLoader.Weapons = table;
        }
        else if (name == "gml_GlobalScript_table_equipment")
        {
            ModLoader.WeaponDescriptions = table;
        }
    }
}
