using ModShardLauncher;
using ModShardLauncher.Mods;

namespace ModShardLauncherTest;

/// <summary>Shared fixtures for the weapon API tests (no game data required).</summary>
internal static class WeaponTestData
{
    /// <summary>
    /// The <c>gml_GlobalScript_table_weapons</c> header of the game data this launcher targets
    /// (84 cells; the last one is empty because the header line ends with a ';').
    /// </summary>
    public const string Header =
        "name;Tier;id;Slot;Subtype;altGrip;rarity;Mat;Price;Markup;MaxDuration;Rng;;Armor_Piercing;" +
        "Armor_Damage;Bodypart_Damage;;Slashing_Damage;Piercing_Damage;Blunt_Damage;Rending_Damage;" +
        "Fire_Damage;Shock_Damage;Poison_Damage;Caustic_Damage;Frost_Damage;Arcane_Damage;Unholy_Damage;" +
        "Sacred_Damage;Psionic_Damage;;FMB;Hit_Chance;CRT;CRTD;CTA;PRR;Block_Power;Block_Recovery;;" +
        "Bleeding_Chance;Daze_Chance;Stun_Chance;Knockback_Chance;Immob_Chance;Stagger_Chance;;MP;" +
        "MP_Restoration;Cooldown_Reduction;Abilities_Energy_Cost;Skills_Energy_Cost;Spells_Energy_Cost;" +
        "Magic_Power;Miscast_Chance;Miracle_Chance;Miracle_Power;Bonus_Range;;max_hp;Health_Restoration;" +
        "Healing_Received;Crit_Avoid;Fatigue_Gain;Lifesteal;Manasteal;Damage_Received;;Pyromantic_Power;" +
        "Geomantic_Power;Venomantic_Power;Electromantic_Power;Cryomantic_Power;Arcanistic_Power;" +
        "Astromantic_Power;Psimantic_Power;;Balance;tags;upgrade;fireproof;NoDrop;audio;";

    public static string[] Cells => Header.Split(';');

    public static int Column(string name) => Array.IndexOf(Cells, name);

    /// <summary>One equipment row: key + one value per language + the trailing ';'.</summary>
    public static string EquipmentLine(string key, params string[] values)
    {
        string[] padded = new string[Enum.GetValues<ModLanguage>().Length];
        for (int i = 0; i < padded.Length; i++)
        {
            padded[i] = i < values.Length ? values[i] : "";
        }

        return key + ";" + string.Join(";", padded) + ";";
    }
}
