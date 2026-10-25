using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
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
            UndertaleCode table = MslContext.Data.Code.First(t => t.Name.Content == name);
            return table.Instructions
                .Where(AssemblyWrapper.IsPushString)
                .Select(i => (i.Value as UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>)!.Resource.Content)
                .Reverse()
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong");
            throw;
        }
    }

    /// <summary>
    /// Replaces the table stored in the script <paramref name="name"/>. The legacy snapshot lists
    /// (<see cref="ModLoader.Weapons"/> / <see cref="ModLoader.WeaponDescriptions"/>) are pointed at
    /// the list that was just written, so <see cref="Msl.SetWeapon"/> and friends never observe a
    /// stale copy of the table.
    /// </summary>
    public static void SetTable(List<string> table, string name)
    {
        string serialized = JsonConvert.SerializeObject(table).Replace("\n", "");
        UndertaleCode target = MslContext.Data.Code.First(t => t.Name.Content == name);
        GlobalDecompileContext context = new(MslContext.Data, false);
        string text = Decompiler.Decompile(target, context);
        text = Regex.Replace(text, "\\[.*\\]", serialized);
        target.ReplaceGML(text, MslContext.Data);

        SyncSnapshot(table, name);
        Log.Information("Successfully set table: {0}", name);
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
