using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
public partial class Msl
{
    public enum WeaponsTier
    {
        [EnumMember(Value = "1")]
        Tier1,
        [EnumMember(Value = "2")]
        Tier2,
        [EnumMember(Value = "3")]
        Tier3,
        [EnumMember(Value = "4")]
        Tier4,
        [EnumMember(Value = "5")]
        Tier5
    }
    
    /// <summary>
    /// <para>Enum used in Weapons table. </para>
    /// Defines the slot the weapon can be equipped in.
    /// </summary>
    public enum WeaponsSlot
    {
        sword,
        axe,
        mace,
        dagger,
        [EnumMember(Value = "2hsword")]
        twohandedsword,
        spear,
        [EnumMember(Value = "2haxe")]
        twohandedaxe,
        [EnumMember(Value = "2hmace")]
        twohandedmace,
        bow,
        crossbow,
        sling,
        [EnumMember(Value = "2hStaff")]
        twohandedstaff,
        chain,
        lute
    }

    /// <summary>
    /// <para>Enum used in Weapons table. </para>
    /// Defines the rarity of the weapon.
    /// </summary>
    public enum WeaponsRarity
    {
        Common,
        Unique
        // Legendary // Doesn't seem to be used post-RtR 
    }

    /// <summary>
    /// <para>Enum used in Weapons table. </para>
    /// Defines the material the weapon is made out of.
    /// </summary>
    public enum WeaponsMaterial
    {
        wood,
        metal,
        leather
    }

    /// <summary>
    /// <para>Enum used in Weapons table. </para>
    /// Defines the rarity of the weapon in description.
    /// </summary>
    public enum WeaponsTags
    {
        // SPECIAL
        unique,
        magic,
        special,
        [EnumMember(Value = "special exc")]
        specialexc,
        WIP,
        
        // ALDOR
        aldor,
        [EnumMember(Value = "aldor common")]
        aldorcommon,
        [EnumMember(Value = "aldor uncommon")]
        aldoruncommon,
        [EnumMember(Value = "aldor rare")]
        aldorrare,
        [EnumMember(Value = "aldor magic")]
        aldormagic,
        
        // FOREIGN
        fjall,
        elven,
        skadia,
        nistra
    }
    
    public static void InjectTableWeapons(
        // Would love to use a hook but devs fucked up the table's categories' names
        string name,
        WeaponsTier Tier,
        string id,
        WeaponsSlot Slot,
        WeaponsRarity rarity,
        WeaponsMaterial Mat,
        WeaponsTags tags,
        byte Rng = 1,
        int? Price = null,
        byte Markup = 1,
        short? MaxDuration = null,
        short? Armor_Piercing = null,
        short? Armor_Damage = null,
        short? Bodypart_Damage = null,
        short? Slashing_Damage = null,
        short? Piercing_Damage = null,
        short? Blunt_Damage = null,
        short? Rending_Damage = null,
        short? Fire_Damage = null,
        short? Shock_Damage = null,
        short? Poison_Damage = null,
        short? Caustic_Damage = null,
        short? Frost_Damage = null,
        short? Arcane_Damage = null,
        short? Unholy_Damage = null,
        short? Sacred_Damage = null,
        short? Psionic_Damage = null,
        short? FMB = null,
        short? Hit_Chance = null,
        short? CRT = null,
        short? CRTD = null,
        short? CTA = null,
        short? PRR = null,
        short? Block_Power = null,
        short? Block_Recovery = null,
        byte? Bleeding_Chance = null,
        byte? Daze_Chance = null,
        byte? Stun_Chance = null,
        byte? Knockback_Chance = null,
        byte? Immob_Chance = null,
        byte? Stagger_Chance = null,
        short? MP = null,
        short? MP_Restoration = null,
        short? Cooldown_Reduction = null,
        short? Skills_Energy_Cost = null,
        short? Spells_Energy_Cost = null,
        short? Magic_Power = null,
        short? Miscast_Chance = null,
        short? Miracle_Chance = null,
        short? Miracle_Power = null,
        short? Bonus_Range = null,
        short? max_hp = null,
        short? Health_Restoration = null,
        short? Healing_Received = null,
        short? Crit_Avoid = null,
        short? Fatigue_Gain = null,
        short? Lifesteal = null,
        short? Manasteal = null,
        short? Damage_Received = null,
        short? Pyromantic_Power = null,
        short? Geomantic_Power = null,
        short? Venomantic_Power = null,
        short? Electroantic_Power = null,
        short? Cryomantic_Power = null,
        short? Arcanistic_Power = null,
        short? Astromantic_Power = null,
        short? Psimantic_Power = null,
        short? Balance = null, // Could be byte ?
        string? upgrade = null,
        bool fireproof = false,
        bool NoDrop = false
        )
    {
        // The row is built from the live table header (see Weapon.Columns) instead of a hard-coded
        // format string, so it always has the same number of cells as the table it is appended to.
        Weapon weapon = new()
        {
            Name = name,
            Tier = GetEnumMemberValue(Tier),
            ID = id,
            Slot = GetEnumMemberValue(Slot),
            Subtype = DefaultSubtype(Slot),
            Rare = rarity.ToString(),
            Material = Mat.ToString(),
            Price = Price ?? 0,
            Markup = Markup,
            MaxDuration = MaxDuration ?? 0,
            Rng = Rng,
            ArmorPiercing = Armor_Piercing ?? 0,
            ArmorDamage = Armor_Damage ?? 0,
            BodypartDamage = Bodypart_Damage ?? 0,
            SlashingDamage = Slashing_Damage ?? 0,
            PiercingDamage = Piercing_Damage ?? 0,
            BluntDamage = Blunt_Damage ?? 0,
            RendingDamage = Rending_Damage ?? 0,
            FireDamage = Fire_Damage ?? 0,
            ShockDamage = Shock_Damage ?? 0,
            PoisonDamage = Poison_Damage ?? 0,
            CausticDamage = Caustic_Damage ?? 0,
            FrostDamage = Frost_Damage ?? 0,
            ArcaneDamage = Arcane_Damage ?? 0,
            UnholyDamage = Unholy_Damage ?? 0,
            SacredDamage = Sacred_Damage ?? 0,
            PsionicDamage = Psionic_Damage ?? 0,
            FMB = FMB ?? 0,
            HitChance = Hit_Chance ?? 0,
            CRT = CRT ?? 0,
            CRTD = CRTD ?? 0,
            CTA = CTA ?? 0,
            PRR = PRR ?? 0,
            BlockPower = Block_Power ?? 0,
            BlockRecovery = Block_Recovery ?? 0,
            BleedingChance = Bleeding_Chance ?? 0,
            DazeChance = Daze_Chance ?? 0,
            StunChance = Stun_Chance ?? 0,
            KnockbackChance = Knockback_Chance ?? 0,
            ImmobChance = Immob_Chance ?? 0,
            StaggerChance = Stagger_Chance ?? 0,
            MP = MP ?? 0,
            MPRestoration = MP_Restoration ?? 0,
            CooldownReduction = Cooldown_Reduction ?? 0,
            SkillsEnergyCost = Skills_Energy_Cost ?? 0,
            SpellsEnergyCost = Spells_Energy_Cost ?? 0,
            MagicPower = Magic_Power ?? 0,
            MiscastChance = Miscast_Chance ?? 0,
            MiracleChance = Miracle_Chance ?? 0,
            MiraclePower = Miracle_Power ?? 0,
            BonusRange = Bonus_Range ?? 0,
            MaxHp = max_hp ?? 0,
            HealthRestoration = Health_Restoration ?? 0,
            HealingReceived = Healing_Received ?? 0,
            CritAvoid = Crit_Avoid ?? 0,
            FatigueGain = Fatigue_Gain ?? 0,
            Lifesteal = Lifesteal ?? 0,
            Manasteal = Manasteal ?? 0,
            DamageReceived = Damage_Received ?? 0,
            PyromanticPower = Pyromantic_Power ?? 0,
            GeomanticPower = Geomantic_Power ?? 0,
            VenomanticPower = Venomantic_Power ?? 0,
            ElectromanticPower = Electroantic_Power ?? 0,
            CryomanticPower = Cryomantic_Power ?? 0,
            ArcanisticPower = Arcanistic_Power ?? 0,
            AstromanticPower = Astromantic_Power ?? 0,
            PsimanticPower = Psimantic_Power ?? 0,
            Balance = Balance ?? 0,
            tags = GetEnumMemberValue(tags),
            Upgrade = upgrade ?? "",
            Fireproof = fireproof ? 1 : 0,
            NoDrop = NoDrop ? "1" : "",
        };

        if (AppendWeaponRow(weapon))
        {
            Log.Information("Injected Weapon {0} into table {1}", id, WeaponsTableName);
        }
    }

    /// <summary>
    /// Default <c>Subtype</c> for a slot: the subtype the most vanilla weapons of that slot use.
    /// Use <see cref="Msl.AddWeapon(Weapon)"/> when an exact subtype is needed.
    /// </summary>
    private static string DefaultSubtype(WeaponsSlot slot) => slot switch
    {
        WeaponsSlot.sword => "sword",
        WeaponsSlot.axe => "axe",
        WeaponsSlot.mace => "mace",
        WeaponsSlot.dagger => "dagger",
        WeaponsSlot.twohandedsword => "2hsword",
        WeaponsSlot.spear => "spear",
        WeaponsSlot.twohandedaxe => "poleaxe",
        WeaponsSlot.twohandedmace => "polehammer",
        WeaponsSlot.bow => "shortbow",
        WeaponsSlot.crossbow => "lightcrossbow",
        WeaponsSlot.sling => "sling",
        WeaponsSlot.twohandedstaff => "magestaff",
        _ => "",
    };
}