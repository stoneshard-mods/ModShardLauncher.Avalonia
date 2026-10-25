using ModShardLauncher;
using ModShardLauncher.Mods;

namespace ModShardLauncherTest;

/// <summary>
/// GetWeapon / CloneDefaults / SetWeapon against synthetic tables: the weapon table row format is
/// the game's table row, so these can be exercised without loading a data.win.
/// </summary>
public class WeaponApiTests : IDisposable
{
    private static readonly Weapon VanillaBlade = new()
    {
        Name = "Homemade Blade",
        Tier = "1",
        ID = "sword09",
        Slot = "sword",
        Subtype = "sword",
        Rare = "Common",
        Material = "metal",
        Price = 100,
        MaxDuration = 55,
        SlashingDamage = 16,
        HitChance = 3,
        Balance = 1,
        tags = "aldor",
    };

    private readonly List<string> _weaponsBackup = ModLoader.Weapons;
    private readonly List<string> _equipmentBackup = ModLoader.WeaponDescriptions;

    public WeaponApiTests()
    {
        ModLoader.Weapons = new List<string>
        {
            WeaponTestData.Header,
            Msl.BuildWeaponRow(WeaponTestData.Cells, VanillaBlade),
        };
        ModLoader.WeaponDescriptions = new List<string>
        {
            WeaponTestData.EquipmentLine("Homemade Blade", "Самодельный клинок", "Homemade Sword", "土刀"),
            WeaponTestData.EquipmentLine("Homemade Blade", "desc-ru", "desc-en", "desc-zh"),
            "Homemade Blade;He;;;It;He;She;She;She;He;;;;",
        };
    }

    public void Dispose()
    {
        ModLoader.Weapons = _weaponsBackup;
        ModLoader.WeaponDescriptions = _equipmentBackup;
    }

    [Fact]
    public void GetWeapon_ReadsTheRowAndTheLanguageLines()
    {
        Weapon weapon = Msl.GetWeapon("Homemade Blade");

        Assert.Equal("Homemade Blade", weapon.Name);
        Assert.Equal("sword09", weapon.ID);
        Assert.Equal("1", weapon.Tier);
        Assert.Equal("sword", weapon.Subtype);
        Assert.Equal(100, weapon.Price);
        Assert.Equal(16, weapon.SlashingDamage);
        Assert.Equal("土刀", weapon.NameList[ModLanguage.Chinese]);
        Assert.Equal("desc-zh", weapon.WeaponDescriptions[ModLanguage.Chinese]);
    }

    [Fact]
    public void CloneDefaults_CopiesStatsAndText_ButKeepsItsOwnNameAndId()
    {
        Weapon clone = new() { Name = "MyFirstWeapon", ID = "sword90" };

        clone.CloneDefaults("Homemade Blade");

        Assert.Equal("MyFirstWeapon", clone.Name);
        Assert.Equal("sword90", clone.ID);
        Assert.Equal(100, clone.Price);
        Assert.Equal(16, clone.SlashingDamage);
        // Copying the stats does not copy the localized name/description (as documented).
        Assert.Equal("None", clone.NameList[ModLanguage.Chinese]);
        Assert.Equal("None", clone.WeaponDescriptions[ModLanguage.Chinese]);
    }

    [Fact]
    public void SetWeapon_UpdatesStatsAndKeepsTheRowAligned()
    {
        Weapon blade = Msl.GetWeapon("Homemade Blade");
        blade.SlashingDamage = 40;
        blade.Price = 999;

        Msl.SetWeapon("Homemade Blade", blade);

        string[] cells = ModLoader.Weapons[1].Split(';');
        Assert.Equal(84, cells.Length);
        Assert.Equal("Homemade Blade", cells[WeaponTestData.Column("name")]);
        Assert.Equal("sword09", cells[WeaponTestData.Column("id")]);
        Assert.Equal("40", cells[WeaponTestData.Column("Slashing_Damage")]);
        Assert.Equal("999", cells[WeaponTestData.Column("Price")]);
        // A column the caller never touched keeps its original (empty) value.
        Assert.Equal("", cells[WeaponTestData.Column("Armor_Piercing")]);
    }

    [Fact]
    public void SetWeapon_SyncsTheEquipmentLines()
    {
        Weapon blade = Msl.GetWeapon("Homemade Blade");
        blade.NameList[ModLanguage.Chinese] = "我的神剑咖喱棒";

        Msl.SetWeapon("Homemade Blade", blade);

        Assert.Contains("我的神剑咖喱棒", ModLoader.WeaponDescriptions[0]);
        Assert.Equal("Homemade Blade;He;;;It;He;She;She;She;He;;;;", ModLoader.WeaponDescriptions[2]);
    }

    [Fact]
    public void SetWeapon_DoesNotBlankNameOrIdWhenTheyAreUnset()
    {
        Weapon patch = new() { SlashingDamage = 25 };
        string originalNameLine = ModLoader.WeaponDescriptions[0];
        string originalDescriptionLine = ModLoader.WeaponDescriptions[1];

        Msl.SetWeapon("Homemade Blade", patch);

        string[] cells = ModLoader.Weapons[1].Split(';');
        Assert.Equal("Homemade Blade", cells[WeaponTestData.Column("name")]);
        Assert.Equal("sword09", cells[WeaponTestData.Column("id")]);
        Assert.Equal("25", cells[WeaponTestData.Column("Slashing_Damage")]);
        // A stat-only weapon must not wipe the vanilla localized text either.
        Assert.Equal(originalNameLine, ModLoader.WeaponDescriptions[0]);
        Assert.Equal(originalDescriptionLine, ModLoader.WeaponDescriptions[1]);
    }

    [Fact]
    public void GetWeapon_ThrowsForAnUnknownName()
    {
        Assert.Throws<KeyNotFoundException>(() => Msl.GetWeapon("No Such Weapon"));
    }
}
