using System.Globalization;
using System.Reflection;

namespace ModShardLauncher.Mods
{
    /// <summary>
    /// One row of <c>gml_GlobalScript_table_weapons</c>.
    /// <para>
    /// The string form of a weapon is the game's table row itself: <see cref="Columns"/> is the
    /// column order of the table (as of the game data this launcher targets) and
    /// <see cref="Weapon2String"/> emits one cell per column. Parsing is column-name based, so a
    /// row that grows a new column keeps working for the columns we know.
    /// </para>
    /// </summary>
    public class Weapon
    {
        /// <summary>
        /// Weapon table columns, in game order. Empty entries are the separator columns the game
        /// leaves blank between logical groups.
        /// </summary>
        public static IReadOnlyList<string> Columns { get; } = new[]
        {
            "name", "Tier", "id", "Slot", "Subtype", "altGrip", "rarity", "Mat", "Price", "Markup",
            "MaxDuration", "Rng", "", "Armor_Piercing", "Armor_Damage", "Bodypart_Damage", "",
            "Slashing_Damage", "Piercing_Damage", "Blunt_Damage", "Rending_Damage", "Fire_Damage",
            "Shock_Damage", "Poison_Damage", "Caustic_Damage", "Frost_Damage", "Arcane_Damage",
            "Unholy_Damage", "Sacred_Damage", "Psionic_Damage", "",
            "FMB", "Hit_Chance", "CRT", "CRTD", "CTA", "PRR", "Block_Power", "Block_Recovery", "",
            "Bleeding_Chance", "Daze_Chance", "Stun_Chance", "Knockback_Chance", "Immob_Chance", "Stagger_Chance", "",
            "MP", "MP_Restoration", "Cooldown_Reduction", "Abilities_Energy_Cost", "Skills_Energy_Cost",
            "Spells_Energy_Cost", "Magic_Power", "Miscast_Chance", "Miracle_Chance", "Miracle_Power", "Bonus_Range", "",
            "max_hp", "Health_Restoration", "Healing_Received", "Crit_Avoid", "Fatigue_Gain", "Lifesteal",
            "Manasteal", "Damage_Received", "",
            "Pyromantic_Power", "Geomantic_Power", "Venomantic_Power", "Electromantic_Power", "Cryomantic_Power",
            "Arcanistic_Power", "Astromantic_Power", "Psimantic_Power", "",
            "Balance", "tags", "upgrade", "fireproof", "NoDrop", "audio", "",
        };

        /// <summary>(table column, field name) pairs. Columns missing here are left blank.</summary>
        private static readonly (string Column, string Field)[] ColumnMap =
        {
            ("name", nameof(Name)),
            ("Tier", nameof(Tier)),
            ("id", nameof(ID)),
            ("Slot", nameof(Slot)),
            ("Subtype", nameof(Subtype)),
            ("altGrip", nameof(altGrip)),
            ("rarity", nameof(Rare)),
            ("Mat", nameof(Material)),
            ("Price", nameof(Price)),
            ("Markup", nameof(Markup)),
            ("MaxDuration", nameof(MaxDuration)),
            ("Rng", nameof(Rng)),
            ("Armor_Piercing", nameof(ArmorPiercing)),
            ("Armor_Damage", nameof(ArmorDamage)),
            ("Bodypart_Damage", nameof(BodypartDamage)),
            ("Slashing_Damage", nameof(SlashingDamage)),
            ("Piercing_Damage", nameof(PiercingDamage)),
            ("Blunt_Damage", nameof(BluntDamage)),
            ("Rending_Damage", nameof(RendingDamage)),
            ("Fire_Damage", nameof(FireDamage)),
            ("Shock_Damage", nameof(ShockDamage)),
            ("Poison_Damage", nameof(PoisonDamage)),
            ("Caustic_Damage", nameof(CausticDamage)),
            ("Frost_Damage", nameof(FrostDamage)),
            ("Arcane_Damage", nameof(ArcaneDamage)),
            ("Unholy_Damage", nameof(UnholyDamage)),
            ("Sacred_Damage", nameof(SacredDamage)),
            ("Psionic_Damage", nameof(PsionicDamage)),
            ("FMB", nameof(FMB)),
            ("Hit_Chance", nameof(HitChance)),
            ("CRT", nameof(CRT)),
            ("CRTD", nameof(CRTD)),
            ("CTA", nameof(CTA)),
            ("PRR", nameof(PRR)),
            ("Block_Power", nameof(BlockPower)),
            ("Block_Recovery", nameof(BlockRecovery)),
            ("Bleeding_Chance", nameof(BleedingChance)),
            ("Daze_Chance", nameof(DazeChance)),
            ("Stun_Chance", nameof(StunChance)),
            ("Knockback_Chance", nameof(KnockbackChance)),
            ("Immob_Chance", nameof(ImmobChance)),
            ("Stagger_Chance", nameof(StaggerChance)),
            ("MP", nameof(MP)),
            ("MP_Restoration", nameof(MPRestoration)),
            ("Cooldown_Reduction", nameof(CooldownReduction)),
            ("Abilities_Energy_Cost", nameof(AbilitiesEnergyCost)),
            ("Skills_Energy_Cost", nameof(SkillsEnergyCost)),
            ("Spells_Energy_Cost", nameof(SpellsEnergyCost)),
            ("Magic_Power", nameof(MagicPower)),
            ("Miscast_Chance", nameof(MiscastChance)),
            ("Miracle_Chance", nameof(MiracleChance)),
            ("Miracle_Power", nameof(MiraclePower)),
            ("Bonus_Range", nameof(BonusRange)),
            ("max_hp", nameof(MaxHp)),
            ("Health_Restoration", nameof(HealthRestoration)),
            ("Healing_Received", nameof(HealingReceived)),
            ("Crit_Avoid", nameof(CritAvoid)),
            ("Fatigue_Gain", nameof(FatigueGain)),
            ("Lifesteal", nameof(Lifesteal)),
            ("Manasteal", nameof(Manasteal)),
            ("Damage_Received", nameof(DamageReceived)),
            ("Pyromantic_Power", nameof(PyromanticPower)),
            ("Geomantic_Power", nameof(GeomanticPower)),
            ("Venomantic_Power", nameof(VenomanticPower)),
            ("Electromantic_Power", nameof(ElectromanticPower)),
            ("Cryomantic_Power", nameof(CryomanticPower)),
            ("Arcanistic_Power", nameof(ArcanisticPower)),
            ("Astromantic_Power", nameof(AstromanticPower)),
            ("Psimantic_Power", nameof(PsimanticPower)),
            ("Balance", nameof(Balance)),
            ("tags", nameof(tags)),
            ("upgrade", nameof(Upgrade)),
            ("fireproof", nameof(Fireproof)),
            ("NoDrop", nameof(NoDrop)),
            ("audio", nameof(Audio)),
        };

        /// <summary>Column name → backing field, built from <see cref="ColumnMap"/>.</summary>
        private static readonly Dictionary<string, FieldInfo> ColumnFields = BuildColumnFields();

        public Weapon()
        {
            NameList = new Dictionary<ModLanguage, string>();
            WeaponDescriptions = new Dictionary<ModLanguage, string>();
            Genders = new Dictionary<ModLanguage, string>();
            foreach (ModLanguage lan in Enum.GetValues(typeof(ModLanguage)))
            {
                NameList.Add(lan, "None");
                WeaponDescriptions.Add(lan, "None");
                Genders.Add(lan, "");
            }
        }

        /// <summary>
        /// Builds a weapon from a table row (the string produced by <see cref="Weapon2String"/>).
        /// <paramref name="description"/> and <paramref name="names"/> are the language values of
        /// the equipment table rows that follow the key.
        /// </summary>
        public Weapon(string property, List<string>? description = null, List<string>? names = null)
        {
            NameList = new Dictionary<ModLanguage, string>();
            WeaponDescriptions = new Dictionary<ModLanguage, string>();
            Genders = new Dictionary<ModLanguage, string>();

            string[] cells = property.Split(";");
            Name = cells.Length > 0 ? cells[0] : "";
            ID = cells.Length > 2 ? cells[2] : "";
            Set(property);

            int index = 0;
            foreach (ModLanguage lan in Enum.GetValues(typeof(ModLanguage)))
            {
                if (description != null && index < description.Count) WeaponDescriptions.Add(lan, description[index]);
                else WeaponDescriptions.Add(lan, "None");
                if (names != null && index < names.Count) NameList.Add(lan, names[index]);
                else NameList.Add(lan, "None");
                Genders.Add(lan, "");
                index++;
            }
        }

        private static Dictionary<string, FieldInfo> BuildColumnFields()
        {
            Dictionary<string, FieldInfo> result = new(StringComparer.Ordinal);
            foreach ((string column, string field) in ColumnMap)
            {
                FieldInfo? info = typeof(Weapon).GetField(field, BindingFlags.Public | BindingFlags.Instance);
                if (info is null)
                {
                    throw new InvalidOperationException($"Weapon field '{field}' (column '{column}') no longer exists.");
                }

                result[column] = info;
            }

            return result;
        }

        /// <summary>名称</summary>
        public string? Name;
        /// <summary>译名列表</summary>
        public Dictionary<ModLanguage, string> NameList;
        /// <summary>描述翻译列表</summary>
        public Dictionary<ModLanguage, string> WeaponDescriptions;
        /// <summary>各语言的语法性别（He / She / It，留空表示该语言没有性别）</summary>
        public Dictionary<ModLanguage, string> Genders;
        /// <summary>内部ID</summary>
        public string? ID;
        /// <summary>武器等级(1-5)</summary>
        public string? Tier;
        /// <summary>武器的种类(单/双手等)</summary>
        public string? Slot;
        /// <summary>武器子类型(sword / saber / axe / ...)</summary>
        public string? Subtype;
        /// <summary>备用手持方式</summary>
        public string? altGrip;
        /// <summary>武器品质</summary>
        public string? Rare;
        /// <summary>武器材料</summary>
        public string? Material;
        /// <summary>[!] 未知属性</summary>
        public string? tags;
        /// <summary>不可掉落(0/1)</summary>
        public string? NoDrop;
        /// <summary>最大耐久</summary>
        public int MaxDuration;
        /// <summary>武器价格</summary>
        public int Price;
        /// <summary>价格倍率</summary>
        public int Markup;
        /// <summary>[!] 未知属性</summary>
        public int Rng;
        /// <summary>护甲破坏</summary>
        public int ArmorDamage;
        /// <summary>护甲穿透</summary>
        public int ArmorPiercing;
        /// <summary>肢体伤害</summary>
        public int BodypartDamage;
        /// <summary>劈砍伤害</summary>
        public int SlashingDamage;
        /// <summary>穿刺伤害</summary>
        public int PiercingDamage;
        /// <summary>钝击伤害</summary>
        public int BluntDamage;
        /// <summary>撕裂伤害</summary>
        public int RendingDamage;
        /// <summary>灼烧伤害</summary>
        public int FireDamage;
        /// <summary>电击伤害</summary>
        public int ShockDamage;
        /// <summary>中毒伤害</summary>
        public int PoisonDamage;
        /// <summary>腐蚀伤害</summary>
        public int CausticDamage;
        /// <summary>霜冻伤害</summary>
        public int FrostDamage;
        /// <summary>秘术伤害</summary>
        public int ArcaneDamage;
        /// <summary>邪术伤害</summary>
        public int UnholyDamage;
        /// <summary>神圣伤害</summary>
        public int SacredDamage;
        /// <summary>灵术伤害</summary>
        public int PsionicDamage;
        /// <summary>准度</summary>
        public int HitChance;
        /// <summary>暴击几率</summary>
        public int CRT;
        /// <summary>暴击效果</summary>
        public int CRTD;
        /// <summary>格挡几率</summary>
        public int PRR;
        /// <summary>格挡力量</summary>
        public int BlockPower;
        /// <summary>格挡恢复</summary>
        public int BlockRecovery;
        /// <summary>反击几率</summary>
        public int CTA;
        /// <summary>失手几率</summary>
        public int FMB;
        /// <summary>出血几率</summary>
        public int BleedingChance;
        /// <summary>击晕几率</summary>
        public int DazeChance;
        /// <summary>硬直几率</summary>
        public int StunChance;
        /// <summary>击退几率</summary>
        public int KnockbackChance;
        /// <summary>限制移动几率</summary>
        public int ImmobChance;
        /// <summary>破衡几率</summary>
        public int StaggerChance;
        /// <summary>精力</summary>
        public int MP;
        /// <summary>精力自动恢复</summary>
        public int MPRestoration;
        /// <summary>冷却时间</summary>
        public int CooldownReduction;
        /// <summary>技能精力消耗</summary>
        public int AbilitiesEnergyCost;
        /// <summary>技能精力消耗</summary>
        public int SkillsEnergyCost;
        /// <summary>法术精力消耗</summary>
        public int SpellsEnergyCost;
        /// <summary>法力</summary>
        public int MagicPower;
        /// <summary>失误几率</summary>
        public int MiscastChance;
        /// <summary>奇观几率</summary>
        public int MiracleChance;
        /// <summary>奇观效果</summary>
        public int MiraclePower;
        /// <summary>生命上限</summary>
        public int MaxHp;
        /// <summary>生命自动恢复</summary>
        public int HealthRestoration;
        /// <summary>治疗效果</summary>
        public int HealingReceived;
        /// <summary>暴击闪避</summary>
        public int CritAvoid;
        /// <summary>疲劳增长</summary>
        public int FatigueGain;
        /// <summary>生命吸取</summary>
        public int Lifesteal;
        /// <summary>精力吸取</summary>
        public int Manasteal;
        /// <summary>所受伤害</summary>
        public int DamageReceived;
        /// <summary>距离加成</summary>
        public int BonusRange;
        /// <summary>炎术法力</summary>
        public int PyromanticPower;
        /// <summary>地术法力</summary>
        public int GeomanticPower;
        /// <summary>毒术法力</summary>
        public int VenomanticPower;
        /// <summary>电术法力</summary>
        public int ElectromanticPower;
        /// <summary>冰术法力</summary>
        public int CryomanticPower;
        /// <summary>秘术法力</summary>
        public int ArcanisticPower;
        /// <summary>星术法力</summary>
        public int AstromanticPower;
        /// <summary>灵术法力</summary>
        public int PsimanticPower;
        /// <summary>平衡</summary>
        public int Balance;
        /// <summary>升级路径</summary>
        public string? Upgrade;
        /// <summary>防火(0/1)</summary>
        public int Fireproof;
        /// <summary>音效类别</summary>
        public string? Audio;

        // Legacy fields: kept for binary compatibility with mods compiled against the WPF build.
        // The current weapon table has no column for them, so they are not read from or written to
        // the table (the property bag is the row itself).
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int Lv;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int E;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int WeaponDamage;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int EVS;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int BackfireDamage;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int ChronomanticPower;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int RangeModifier;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int DamageReturned;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int STL;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int NoiseProduced;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int OffhandEfficiency;
        /// <summary>[!] 旧字段：当前武器表无此列</summary>
        public int SlayingChance;

        public (string, string, string) AsString()
        {
            return Weapon2String(this);
        }

        /// <summary>Copies every table column from the named vanilla weapon, keeping Name and ID.</summary>
        public void CloneDefaults(string name)
        {
            Weapon weapon = Msl.GetWeapon(name);
            string? ownName = Name;
            string? ownId = ID;
            Set(Weapon2String(weapon).Item1);
            Name = ownName;
            ID = ownId;
        }

        public virtual void SetDefaults()
        {

        }

        /// <summary>
        /// Reads a weapon table row. Values are matched by column name, so the row may contain
        /// more columns than <see cref="Columns"/> knows about (they are ignored).
        /// </summary>
        /// <exception cref="FormatException">A numeric column contains a non-numeric value.</exception>
        public void Set(string property)
        {
            string[] cells = property.Split(";");
            for (int i = 0; i < Columns.Count && i < cells.Length; i++)
            {
                if (!ColumnFields.TryGetValue(Columns[i], out FieldInfo? field))
                {
                    continue;
                }

                string cell = cells[i];
                if (field.FieldType == typeof(int))
                {
                    if (cell.Length == 0)
                    {
                        field.SetValue(this, 0);
                        continue;
                    }

                    if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        throw new FormatException(
                            $"Weapon column '{Columns[i]}' expects an integer but the row contains '{cell}'.");
                    }

                    field.SetValue(this, parsed);
                }
                else
                {
                    field.SetValue(this, cell);
                }
            }
        }

        /// <summary>
        /// The weapon as a table row, plus the equipment-table name and description lines.
        /// The row has exactly one cell per entry of <see cref="Columns"/>.
        /// </summary>
        public static (string, string, string) Weapon2String(Weapon weapon)
        {
            string[] cells = new string[Columns.Count];
            for (int i = 0; i < Columns.Count; i++)
            {
                if (!ColumnFields.TryGetValue(Columns[i], out FieldInfo? field))
                {
                    cells[i] = "";
                    continue;
                }

                object? value = field.GetValue(weapon);
                cells[i] = field.FieldType == typeof(int)
                    ? FormatNumber((int)(value ?? 0))
                    : (string?)value ?? "";
            }

            string key = weapon.Name ?? "";
            string descriptions = key + ";" + JoinLocalization(weapon.WeaponDescriptions);
            string names = key + ";" + JoinLocalization(weapon.NameList);
            return (string.Join(";", cells), descriptions, names);
        }

        /// <summary>
        /// A weapon built from its table row. The row still has to be added to the table (see
        /// <see cref="Msl.AddWeapon(Weapon)"/> for the whole package).
        /// </summary>
        public static Weapon String2Weapon(string property)
        {
            Weapon weapon = new();
            weapon.Set(property);
            return weapon;
        }

        /// <summary>Column name → value, for merging a weapon into an existing table row.</summary>
        public Dictionary<string, string> AsColumnValues()
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach ((string column, FieldInfo field) in ColumnFields)
            {
                object? value = field.GetValue(this);
                values[column] = field.FieldType == typeof(int)
                    ? FormatNumber((int)(value ?? 0))
                    : (string?)value ?? "";
            }

            return values;
        }

        private static string FormatNumber(int value) =>
            value == 0 ? "" : value.ToString(CultureInfo.InvariantCulture);

        private static string JoinLocalization(Dictionary<ModLanguage, string>? values)
        {
            IEnumerable<string> cells = Enum.GetValues<ModLanguage>().Select(language =>
            {
                if (values is null || !values.TryGetValue(language, out string? text))
                {
                    return "";
                }

                // "None" is the placeholder the constructors use for "not set".
                return text is null || text == "None" ? "" : text;
            });

            return string.Join(";", cells) + ";";
        }
    }
}
