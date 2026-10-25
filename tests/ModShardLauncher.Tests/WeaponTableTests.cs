using ModShardLauncher;
using ModShardLauncher.Mods;

namespace ModShardLauncherTest;

/// <summary>
/// Locks the weapon table schema and the row the API builds for it: a row must always have the
/// same number of cells as the live table header, otherwise every column after the first missing
/// one shifts (that bug made <c>InjectTableWeapons</c> write 80 cells into an 84 cell table).
/// </summary>
public class WeaponTableTests
{
    private static string[] Header => WeaponTestData.Cells;

    private static int Column(string name) => WeaponTestData.Column(name);

    [Fact]
    public void Columns_MatchTheCurrentGameHeader()
    {
        Assert.Equal(84, Header.Length);
        Assert.Equal(Header, Weapon.Columns.ToArray());
    }

    [Fact]
    public void Weapon2String_EmitsOneCellPerColumn()
    {
        Weapon weapon = new()
        {
            Name = "MyFirstWeapon",
            ID = "sword90",
            Subtype = "saber",
            Price = 300,
            SlashingDamage = 25,
        };

        (string row, _, _) = Weapon.Weapon2String(weapon);
        string[] cells = row.Split(';');

        Assert.Equal(Weapon.Columns.Count, cells.Length);
        Assert.Equal("MyFirstWeapon", cells[Column("name")]);
        Assert.Equal("sword90", cells[Column("id")]);
        Assert.Equal("saber", cells[Column("Subtype")]);
        Assert.Equal("300", cells[Column("Price")]);
        Assert.Equal("25", cells[Column("Slashing_Damage")]);
        Assert.Equal("", cells[Column("Markup")]);
    }

    [Fact]
    public void Row_RoundTripsThroughString2Weapon()
    {
        Weapon weapon = new()
        {
            Name = "RoundTrip",
            Tier = "3",
            ID = "sword91",
            Slot = "sword",
            Subtype = "saber",
            altGrip = "Guard Halberd",
            Rare = "Unique",
            Material = "metal",
            Price = 1234,
            Markup = 2,
            MaxDuration = 77,
            Rng = 1,
            BlockRecovery = 6,
            AbilitiesEnergyCost = 4,
            MaxHp = 15,
            CritAvoid = 3,
            FatigueGain = 2,
            Audio = "light",
            Upgrade = "Wheels2",
            Fireproof = 1,
            NoDrop = "1",
            tags = "aldor common",
            Lifesteal = 5,
        };

        string row = Weapon.Weapon2String(weapon).Item1;
        Weapon parsed = Weapon.String2Weapon(row);

        Assert.Equal(weapon.Name, parsed.Name);
        Assert.Equal(weapon.Tier, parsed.Tier);
        Assert.Equal(weapon.ID, parsed.ID);
        Assert.Equal(weapon.Subtype, parsed.Subtype);
        Assert.Equal(weapon.altGrip, parsed.altGrip);
        Assert.Equal(weapon.Rare, parsed.Rare);
        Assert.Equal(weapon.Material, parsed.Material);
        Assert.Equal(weapon.Price, parsed.Price);
        Assert.Equal(weapon.Markup, parsed.Markup);
        Assert.Equal(weapon.BlockRecovery, parsed.BlockRecovery);
        Assert.Equal(weapon.AbilitiesEnergyCost, parsed.AbilitiesEnergyCost);
        Assert.Equal(weapon.MaxHp, parsed.MaxHp);
        Assert.Equal(weapon.CritAvoid, parsed.CritAvoid);
        Assert.Equal(weapon.FatigueGain, parsed.FatigueGain);
        Assert.Equal(weapon.Audio, parsed.Audio);
        Assert.Equal(weapon.Upgrade, parsed.Upgrade);
        Assert.Equal(weapon.Fireproof, parsed.Fireproof);
        Assert.Equal(weapon.NoDrop, parsed.NoDrop);
        Assert.Equal(weapon.tags, parsed.tags);
        Assert.Equal(weapon.Lifesteal, parsed.Lifesteal);
    }

    [Fact]
    public void BuildWeaponRow_AlignsWithTheLiveHeader()
    {
        Weapon weapon = new()
        {
            Name = "Aligned",
            Tier = "1",
            ID = "sword92",
            Slot = "sword",
            Subtype = "cleaver",
            Rare = "Common",
            Material = "metal",
            Price = 500,
            SlashingDamage = 33,
            tags = "aldor common",
        };

        string row = Msl.BuildWeaponRow(Header, weapon);
        string[] cells = row.Split(';');

        Assert.Equal(Header.Length, cells.Length);
        Assert.Equal("Aligned", cells[Column("name")]);
        Assert.Equal("1", cells[Column("Tier")]);
        Assert.Equal("sword92", cells[Column("id")]);
        Assert.Equal("sword", cells[Column("Slot")]);
        Assert.Equal("cleaver", cells[Column("Subtype")]);
        Assert.Equal("Common", cells[Column("rarity")]);
        Assert.Equal("500", cells[Column("Price")]);
        Assert.Equal("33", cells[Column("Slashing_Damage")]);
        Assert.Equal("aldor common", cells[Column("tags")]);
        Assert.Equal("", cells[Column("audio")]);
    }

    [Fact]
    public void Set_ReportsTheColumnWhenANumberIsNotNumeric()
    {
        string[] cells = new string[Header.Length];
        cells[Column("name")] = "Broken";
        cells[Column("Price")] = "Common";

        FormatException error = Assert.Throws<FormatException>(() => Weapon.String2Weapon(string.Join(";", cells)));
        Assert.Contains("Price", error.Message);
        Assert.Contains("Common", error.Message);
    }
}
