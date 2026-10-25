using System;
using System.Collections.Generic;
using System.Linq;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher
{
    public static partial class Msl
    {
        private const string WeaponsTableName = "gml_GlobalScript_table_weapons";
        private const string EquipmentTableName = "gml_GlobalScript_table_equipment";

        /// <summary>Fallback grammatical gender: masculine in every gendered language.</summary>
        private static readonly string[] DefaultGenders =
        {
            "He", "", "", "He", "He", "He", "He", "He", "He", "", "", "",
        };

        /// <summary>
        /// Reads the weapon table row that <paramref name="id"/> points at (its <c>name</c> column,
        /// or a unique prefix of it) and fills the language rows from the equipment table.
        /// </summary>
        public static Weapon GetWeapon(string id)
        {
            string row = GetWeaponRow(id);
            string key = row.Split(';')[0];

            List<int> equipmentRows = FindEquipmentRows(key);
            if (equipmentRows.Count < 2)
            {
                throw new InvalidOperationException(
                    $"Weapon '{key}' has fewer than two rows in {EquipmentTableName}; the name/description pair is incomplete.");
            }

            List<string> names = LanguageValues(MslContext.WeaponDescriptions[equipmentRows[0]]);
            List<string> descriptions = LanguageValues(MslContext.WeaponDescriptions[equipmentRows[1]]);
            Log.Information("Found weapon: {0}", key);
            return new Weapon(row, descriptions, names);
        }

        /// <summary>
        /// Writes <paramref name="weapon"/> over the row <paramref name="id"/> points at. Columns the
        /// <see cref="Weapon"/> type does not model are left untouched, and the equipment-table name
        /// and description rows are updated with <see cref="Weapon.NameList"/> /
        /// <see cref="Weapon.WeaponDescriptions"/>.
        /// </summary>
        public static void SetWeapon(string id, Weapon weapon)
        {
            List<string> weapons = MslContext.Weapons;
            int index = FindWeaponRowIndex(weapons, id);
            string oldKey = weapons[index].Split(';')[0];

            string[] header = weapons[0].Split(';');
            string[] cells = weapons[index].Split(';');
            Dictionary<string, string> values = weapon.AsColumnValues();

            // An unset Name/ID must not blank the row; SetWeapon is also used to tweak stats only.
            if (string.IsNullOrEmpty(weapon.Name)) values.Remove("name");
            if (string.IsNullOrEmpty(weapon.ID)) values.Remove("id");

            for (int i = 0; i < header.Length && i < cells.Length; i++)
            {
                if (values.TryGetValue(header[i], out string? value))
                {
                    cells[i] = value;
                }
            }

            weapons[index] = string.Join(";", cells);
            string key = cells[0];
            if (MslContext.HasData)
            {
                GameTableCodec.SetTable(weapons, WeaponsTableName);
            }

            if (key != oldKey)
            {
                RenameEquipmentKey(oldKey, key);
                if (MslContext.HasData)
                {
                    GameTableCodec.SetTable(MslContext.WeaponDescriptions, EquipmentTableName);
                }
            }

            SetEquipmentLines(key, weapon);
            Log.Information("Successfully set weapon: {0}", key);
        }

        /// <summary>
        /// Adds a weapon to the game: one row in the weapons table plus the three equipment-table
        /// rows (name, description, grammatical gender). The <c>name</c> column is the key the
        /// equipment table is joined on, so it must be unique.
        /// </summary>
        /// <param name="weapon">Weapon to add; <see cref="Weapon.Name"/> is required.</param>
        public static void AddWeapon(Weapon weapon)
        {
            if (string.IsNullOrWhiteSpace(weapon.Name))
            {
                throw new ArgumentException("Weapon.Name is required (it is the equipment table key).", nameof(weapon));
            }

            if (!AppendWeaponRow(weapon))
            {
                return;
            }

            AddEquipmentLines(weapon);
            Log.Information("Injected weapon {0}", weapon.Name);
        }

        /// <summary>
        /// Appends a weapon row to the weapons table without touching the equipment table. Returns
        /// false when the name is already taken.
        /// </summary>
        internal static bool AppendWeaponRow(Weapon weapon)
        {
            List<string> table = ThrowIfNull(GameTableCodec.GetTable(WeaponsTableName));
            string key = weapon.Name ?? "";
            if (table.Skip(1).Any(row => row.Split(';')[0] == key))
            {
                Log.Warning("Weapon {0} is already present in {1}; the row was not added again.", key, WeaponsTableName);
                return false;
            }

            string[] header = table[0].Split(';');
            table.Add(BuildWeaponRow(header, weapon));
            GameTableCodec.SetTable(table, WeaponsTableName);
            return true;
        }

        /// <summary>
        /// Builds a table row for <paramref name="weapon"/> from the live table header, so the row
        /// always has the same number of cells as the table (unknown columns stay empty).
        /// </summary>
        internal static string BuildWeaponRow(string[] header, Weapon weapon)
        {
            Dictionary<string, string> values = weapon.AsColumnValues();
            values["name"] = weapon.Name ?? "";
            if (string.IsNullOrEmpty(weapon.ID))
            {
                values["id"] = "";
            }

            string[] cells = new string[header.Length];
            for (int i = 0; i < header.Length; i++)
            {
                cells[i] = values.TryGetValue(header[i], out string? value) ? value : "";
            }

            return string.Join(";", cells);
        }

        private static void AddEquipmentLines(Weapon weapon)
        {
            List<string> table = ThrowIfNull(GameTableCodec.GetTable(EquipmentTableName));
            string key = weapon.Name!;
            if (table.Any(row => row.Split(';')[0] == key))
            {
                Log.Warning("Equipment rows for {0} already exist; they were not added again.", key);
                return;
            }

            table.Add(LocalizationLine(key, weapon.NameList));
            table.Add(LocalizationLine(key, weapon.WeaponDescriptions));
            table.Add(GenderLine(key, weapon.Genders));
            GameTableCodec.SetTable(table, EquipmentTableName);
        }

        private static void SetEquipmentLines(string key, Weapon weapon)
        {
            List<int> rows = FindEquipmentRows(key);
            if (rows.Count < 2)
            {
                Log.Warning("Weapon {0} has fewer than two equipment rows; the name/description lines were not updated.", key);
                return;
            }

            // A Weapon used only to tweak numbers has no localized text ("None" placeholders);
            // leave the vanilla name/description rows alone in that case.
            bool hasNames = HasText(weapon.NameList);
            bool hasDescriptions = HasText(weapon.WeaponDescriptions);
            if (!hasNames && !hasDescriptions)
            {
                return;
            }

            List<string> table = MslContext.WeaponDescriptions;
            if (hasNames)
            {
                table[rows[0]] = LocalizationLine(key, weapon.NameList);
            }

            if (hasDescriptions)
            {
                table[rows[1]] = LocalizationLine(key, weapon.WeaponDescriptions);
            }

            if (MslContext.HasData)
            {
                GameTableCodec.SetTable(table, EquipmentTableName);
            }
        }

        private static bool HasText(Dictionary<ModLanguage, string>? values) =>
            values is not null && values.Values.Any(text => !string.IsNullOrEmpty(text) && text != "None");

        private static void RenameEquipmentKey(string oldKey, string newKey)
        {
            List<string> table = MslContext.WeaponDescriptions;
            for (int i = 0; i < table.Count; i++)
            {
                string[] cells = table[i].Split(';');
                if (cells[0] == oldKey)
                {
                    cells[0] = newKey;
                    table[i] = string.Join(";", cells);
                }
            }
        }

        /// <summary>Equipment rows are keyed by the weapon's <c>name</c> column.</summary>
        private static List<int> FindEquipmentRows(string key)
        {
            List<int> rows = new();
            List<string> table = MslContext.WeaponDescriptions;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].Split(';')[0] == key)
                {
                    rows.Add(i);
                }
            }

            return rows;
        }

        private static string GetWeaponRow(string id) => MslContext.Weapons[FindWeaponRowIndex(MslContext.Weapons, id)];

        private static int FindWeaponRowIndex(List<string> weapons, string id)
        {
            for (int i = 1; i < weapons.Count; i++)
            {
                if (weapons[i].Split(';')[0] == id)
                {
                    return i;
                }
            }

            for (int i = 1; i < weapons.Count; i++)
            {
                if (weapons[i].StartsWith(id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new KeyNotFoundException($"Weapon '{id}' was not found in {WeaponsTableName}.");
        }

        /// <summary>Language cells of an equipment row, padded/trimmed to one value per language.</summary>
        private static List<string> LanguageValues(string row)
        {
            string[] cells = row.Split(';');
            List<string> values = cells.Skip(1).Take(Enum.GetValues<ModLanguage>().Length).ToList();
            while (values.Count < Enum.GetValues<ModLanguage>().Length)
            {
                values.Add("");
            }

            return values;
        }

        private static string LocalizationLine(string key, Dictionary<ModLanguage, string>? values) =>
            key + ";" + JoinLanguages(values) + ";";

        private static string GenderLine(string key, Dictionary<ModLanguage, string>? values)
        {
            bool isEmpty = values is null || values.Values.All(string.IsNullOrEmpty);
            if (isEmpty)
            {
                return key + ";" + string.Join(";", DefaultGenders) + ";";
            }

            return key + ";" + JoinLanguages(values) + ";";
        }

        private static string JoinLanguages(Dictionary<ModLanguage, string>? values) =>
            string.Join(";", Enum.GetValues<ModLanguage>().Select(language =>
            {
                if (values is null || !values.TryGetValue(language, out string? text) || text is null || text == "None")
                {
                    return "";
                }

                return text;
            }));
    }
}
