using System;
using System.Collections.Generic;
using Serilog;
using Newtonsoft.Json;
using System.IO;

namespace ModShardLauncher
{
    public class ItemsTable
    {
        public string[] ListItems { get; }
        public int[] ListRarity { get; }
        public int[] ListDurability { get; }
        public ItemsTable()
        {
            ListItems = Array.Empty<string>();
            ListRarity = Array.Empty<int>();
            ListDurability = Array.Empty<int>();
        }
        public ItemsTable(string[] listItems, int[] listRarity, int[] listDurability)
        {
            if (listItems.Length != listRarity.Length || listItems.Length != listDurability.Length)
            {
                throw new ArgumentException($"Error in ItemsTable constructor, {listItems}, {listRarity} and {listDurability} must have the same length.");
            } 
            ListItems = listItems;
            ListRarity = listRarity;
            ListDurability = listDurability;
        }
    }
    public class RandomItemsTable
    {
        public int[] ListWeight { get; }
        public ItemsTable ItemsTable { get; }
        public RandomItemsTable(int[] listWeight, ItemsTable itemsTable)
        {
            if (listWeight.Length != itemsTable.ListItems.Length)
            {
                throw new ArgumentException($"Error in RandomItemsTable constructor, {listWeight}, and {itemsTable} elements must have the same length.");
            } 
            ListWeight = listWeight;
            ItemsTable = itemsTable;
        }
        public RandomItemsTable(int[] listWeight, string[] listItems, int[] listRarity, int[] listDurability): this(listWeight, new ItemsTable(listItems, listRarity, listDurability)) { }
    }
    public class ReferenceTable
    {
        public string DefaultTable { get; }
        public Dictionary<int, string> Ids { get; }
        public Dictionary<int, string> Tiers { get; }
        public ReferenceTable(string defaultTable, Dictionary<int, string> ids, Dictionary<int, string> tiers)
        {
            DefaultTable = defaultTable;
            Ids = ids;
            Tiers = tiers;
        }
    }
    public class LootTable
    {
        public ItemsTable GuaranteedItems { get; }
        public int RandomLootMin { get; }
        public int RandomLootMax { get; }
        public int EmptyWeight { get; }
        public RandomItemsTable RandomItemsTable { get; }
        public LootTable(ItemsTable guaranteedItems, int randomLootMin, int randomLootMax, int emptyWeight, RandomItemsTable randomItemsTable)
        {
            GuaranteedItems = guaranteedItems;
            RandomLootMin = randomLootMin;
            RandomLootMax = randomLootMax;
            EmptyWeight = emptyWeight;
            RandomItemsTable = randomItemsTable;
        }
    }
    public static class LootTableRegistry
    {
        internal static Dictionary<string, ReferenceTable> ReferenceTables = new();
        internal static Dictionary<string, LootTable> LootTables = new();
        public static void ResetLootTables()
        {
            ReferenceTables.Clear();
            LootTables.Clear();
        }
        public static void SaveLootTables(string DirPath)
        {
            try
            {
                if (LootTables.Count > 0)
                {
                    File.WriteAllText(Path.Combine(DirPath, "loot_table.json"), JsonConvert.SerializeObject(LootTables));
                    Log.Information("Successfully saving the loot table json.");
                }
                if (ReferenceTables.Count > 0)
                {
                    File.WriteAllText(Path.Combine(DirPath, "reference_table.json"), JsonConvert.SerializeObject(ReferenceTables));
                    Log.Information("Successfully saving the reference table json.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Saving Loot table failed for {0}", DirPath);
            }
        }
        public static void InjectLootScripts()
        {
            if (LootTables.Count == 0 && ReferenceTables.Count == 0)  return;

            Msl.AddInnerFunction("scr_msl_resolve_items");
            Msl.AddInnerFunction("scr_msl_resolve_refence_table");
            Msl.AddInnerFunction("scr_msl_resolve_guaranteed_items");
            Msl.AddInnerFunction("scr_msl_resolve_random_items");
            Msl.AddInnerFunction("scr_msl_resolve_loot_table");

            const string containerScript = "gml_Object_c_container_Other_10";
            const string containerAnchor = "script_execute";

            // The loot injection was written for an older game build where containers
            // dispatched loot via `script_execute` inside c_container_Other_10, and
            // units dropped loot in o_unit_Destroy_0. The current build rearchitected
            // loot into per-type scr_loot_* scripts and removed that anchor, so the
            // injection no longer applies. Skip gracefully (instead of letting
            // MatchFrom throw and aborting the whole patch) until the loot module is
            // rearchitected for the current build. On the legacy build the anchor is
            // present, so the behaviour here is unchanged.
            if (!ScriptExists(containerScript))
            {
                Log.Warning("Loot injection skipped: '{Script}' not found; the loot module needs rearchitecting for the current game build.", containerScript);
                return;
            }
            var container = Msl.LoadGML(containerScript);
            if (!container.ienumerable.Any(line => line.Contains(containerAnchor)))
            {
                Log.Warning("Loot injection skipped: anchor '{Anchor}' not found in '{Script}'; the loot module needs rearchitecting for the current game build.", containerAnchor, containerScript);
                return;
            }

            container
                .MatchFrom(containerAnchor)
                .InsertBelow("scr_msl_resolve_loot_table(other, 0)")
                .Save();

            const string unitScript = "gml_Object_o_unit_Destroy_0";
            if (ScriptExists(unitScript))
            {
                Msl.LoadGML(unitScript)
                    .MatchAll()
                    .InsertBelow("scr_msl_resolve_loot_table(self, 1)")
                    .Save();
            }
            else
            {
                Log.Warning("Unit loot injection skipped: '{Script}' not found.", unitScript);
            }
        }

        private static bool ScriptExists(string codeName) =>
            MslContext.Data.Code.Any(c => c.Name?.Content == codeName);
    }
    public static partial class Msl
    {
        public static void AddLootTable(string lootTableID, ItemsTable guaranteedItems, int randomLootMin, int randomLootMax, int emptyWeight, RandomItemsTable randomItemsTable)
        {
            LootTable lootTable = new(guaranteedItems, randomLootMin, randomLootMax, emptyWeight, randomItemsTable);
            LootTableRegistry.LootTables.Add(lootTableID, lootTable);
            Log.Information("Adding LootTable {0}", lootTableID);
        }
        public static void AddReferenceTable(string nameObject, string table)
        {
            LootTableRegistry.ReferenceTables.Add(nameObject, new ReferenceTable(table, new Dictionary<int, string>(), new Dictionary<int, string>()));
            Log.Information("Adding ReferenceTable {0} for {1}", table, nameObject);
        }
        public static void AddReferenceTable(string nameObject, string table, Dictionary<int, string>? ids, Dictionary<int, string>? tiers)
        {
            LootTableRegistry.ReferenceTables.Add(nameObject, new ReferenceTable(table, ids ?? new Dictionary<int, string>(), tiers ?? new Dictionary<int, string>()));
            Log.Information("Adding ReferenceTable {0} for {1}", table, nameObject);
        }
        public static void AddReferenceTableForMultipleObjects(string table, params string[] nameObjects)
        {
            foreach(string nameObject in nameObjects)
            {
                Msl.AddReferenceTable(nameObject, table);
            }
        }
    }
}