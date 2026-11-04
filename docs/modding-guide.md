# 模组制作指南（C# / .sml）

面向想为《石质碎片》（Stoneshard）写 C# 模组的人。本文按**本仓库（Avalonia + .NET 10 重构版）的实际行为**编写；与旧版 WPF 教程不一致的地方，在 [§10](#10-已知差异与缺陷) 集中说明并给出原因。

> 本文对应启动器 **v0.13.2.0**。文中代码在 `original.win` 上完整跑过：编译 → 打补丁 → 读回校验，验证方式见 [§11](#11-验证与调试)。若以后代码行为变了，以 `src/ModShardLauncher.Core/` 为准。
>
> 这个版本对齐了武器表 API：`Weapon` / `CloneDefaults` / `GetWeapon` / `SetWeapon` / `ModWeapons` / `InjectTableWeapons` 都能正常工作了。如果你看的是旧版教程里“这些 API 会写坏数据、只能手改表”的说明，那是 v0.13.2.0 之前的限制，见 [§10.1](#101-已修复旧教程里的坑现在不用绕)。

## 目录

1. [成品长什么样](#1-成品长什么样)
2. [环境准备](#2-环境准备)
3. [目录：文件放在哪](#3-目录文件放在哪)
4. [创建工程](#4-创建工程)
5. [Mod 主类](#5-mod-主类)
6. [编译成 .sml](#6-编译成-sml)
7. [第一把武器](#7-第一把武器)
8. [本地化](#8-本地化)
9. [打补丁、启用与分发](#9-打补丁启用与分发)
10. [已知差异与缺陷](#10-已知差异与缺陷)
11. [验证与调试](#11-验证与调试)
12. [附录：完整示例](#12-附录完整示例)
13. [API 速查](#13-api-速查)
14. [附录 B：注入音效、菜单与 GML 函数](#14-附录-b注入音效菜单与-gml-函数)
15. [附录 C：新增可选角色](#15-附录-c新增可选角色)
16. [附录 D：技能、状态与技能树](#16-附录-d技能状态与技能树)
17. [附录 E：五维属性与状态词条](#17-附录-e五维属性与状态词条)

## 1. 成品长什么样

一个模组最终是一个 `.sml` 包，里面装三类东西：

| 组成 | 说明 |
|---|---|
| 程序集 | 你的 C# 类（`Mod` 子类 + 若干辅助类）编译结果 |
| 资源文件 | 工程目录里的 PNG、GML、JSON 等，原样打包 |
| `icon.png` | 模组卡片封面，放在工程**根目录** |

流程是：启动器打开**原版** `data.win` → 逐个启用模组，注入贴图、调用 `PatchMod()` 写游戏表 → 另存成新的数据文件 → 游戏读取新文件。

## 2. 环境准备

| 需要 | 说明 |
|---|---|
| Windows 10 / 11 x64 | 游戏只支持 Windows |
| .NET SDK 10.0.4xx | **只在开发机需要**：在 IDE 里写代码、`dotnet build` 验证。真正的打包由启动器内置的 Roslyn 完成，玩家机器不需要 SDK |
| 一份原版 `data.win` | Steam 正式版或 `modbranch`。示例使用安装目录里的 `original.win`；不要用已经打过补丁的文件（表结构会被上一次补丁影响） |
| 启动器 | `ModShardLauncher.GUI.exe`（图形界面）或 `ModShardLauncher.CLI.exe`（命令行） |

## 3. 目录：文件放在哪

用户数据目录固定在 `%LOCALAPPDATA%\ModShardLauncher`：

```text
%LOCALAPPDATA%\ModShardLauncher\
├─ Settings.json     # 语言、日志开关、已启用的模组
├─ Mods\             # .sml 成品（GUI 与 CLI 都从这里读）
├─ ModSources\       # C# 工程，每个工程一个子文件夹
├─ logs\             # GUI / CLI 日志
└─ export\           # 可选的导出清单
```

与旧版最大的差异：旧版是在 `ModShardLauncher.exe` 同级建 `Mods` / `ModSources`，**本版固定在用户数据目录**，升级或重装程序不会动你的模组。首次启动会把旧的便携布局自动迁移过来。

- GUI：设置页「文件夹」里可以直接打开这些目录；
- CLI：`--data-dir <目录>` 可以换一套互不干扰的模组/数据目录；
- `data.win` 不在这里：它是任意位置的原版数据文件，用「打开」对话框选择。

### 3.1 打补丁的输出约定（快速迭代用）

改 mod 之后是「编译 → 打补丁 → 进游戏」的循环，所以约定死两条：

| 约定 | 原因 |
|---|---|
| **输入永远是 `original.win`**（干净原版） | 拿已经打过的 `data.win` 当输入会叠加补丁，出错（§10.2 #14） |
| **输出默认直接覆盖游戏目录的 `data.win`** | 省掉改名、复制、重新选文件，打完直接启动游戏 |

也就是说：`original.win` 是"源"，`data.win` 是"产物"，每次重打都从源重新生成产物覆盖旧的——**不要手工备份产物，要备份就备份源**。

```powershell
$game = "C:\Program Files (x86)\Steam\steamapps\common\Stoneshard"
$cli  = "...\ModShardLauncher.CLI.exe"
$src  = "$env:LOCALAPPDATA\ModShardLauncher\ModSources\<工程名>"

# 1) 备份原版（只需一次；之后 original.win 就是唯一的"源"）
Copy-Item "$game\data.win" "$game\original.win" -Force

# 2) 每次改完 mod：编译 + 打补丁 → 直接覆盖 data.win
& $cli compile --source $src
& $cli patch --data "$game\original.win" --output "$game\data.win" --enable <ModName>
```

> `--data` 与 `--output` 不能是同一个文件。
> 想连同其它已装 mod 一起打，把 `--enable <ModName>` 换成 `--all`（`Mods` 目录里除 `*.bak` 外的全部启用）。
> 游戏更新或 Steam 校验把 `data.win` 还原后，重跑一次 `patch` 即可，`original.win` 不受影响。
> 单个工程里可以把上面三行写成 `rebuild.ps1`，双击/单命令跑完整个循环。

## 4. 创建工程

### 4.1 建目录

在 `%LOCALAPPDATA%\ModShardLauncher\ModSources` 下新建文件夹，**文件夹名就是最终 `.sml` 的名字**：

```text
ModSources\MyFirstMod\
```

建议只用英文、数字、下划线。中文和空格虽然能打包，但 CLI 里要处处加引号。

### 4.2 新建类库

Visual Studio：新建项目 → 类库（`.NET`） → 目标框架 `net10.0`。或者命令行：

```powershell
dotnet new classlib -o "$env:LOCALAPPDATA\ModShardLauncher\ModSources\MyFirstMod" --framework net10.0
```

### 4.3 引用宿主程序集

把模板生成的类库项目文件（例如 `MyFirstMod.csproj`）改成下面这样，并删掉自带的 `Class1.cs`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyFirstMod</AssemblyName>
    <RootNamespace>MyFirstMod</RootNamespace>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- 指向启动器目录里的 ModShardLauncher.dll：安装目录，或仓库的 publish\、
         src/ModShardLauncher.Core/bin/Release/net10.0/ -->
    <Reference Include="ModShardLauncher">
      <HintPath>$(MslPath)</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

编译时传路径：

```powershell
dotnet build .\MyFirstMod.csproj -c Release -p:MslPath="<启动器目录>\ModShardLauncher.dll"
```

`<Private>false</Private>` 让宿主 DLL 不复制进你的输出目录（打包时也不需要）。

如果你的代码用到 `UndertaleData`、`UndertaleSprite` 等 UndertaleModTool 类型，还要再引用同目录的 `UndertaleModLib.dll`，否则报 `CS0012`：

```xml
<ItemGroup>
  <Reference Include="UndertaleModLib">
    <HintPath>$([System.IO.Path]::GetDirectoryName($(MslPath)))\UndertaleModLib.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

### 4.4 打包器不读 csproj（重要）

点「编译」时，启动器是用 Roslyn **直接编译工程目录里的 `.cs`**，不执行 MSBuild，也不做 NuGet 还原。所以：

- `csproj` 只服务于 IDE 和 `dotnet build`，里面的 `PackageReference` 在打包时**不存在**；
- 能用的引用只有：宿主程序集、随程序复制的 `ModReference`（`netstandard`、`System.Runtime`、`System.Collections`、`System.Linq`、`System.ObjectModel`）、以及运行时核心库；
- 实测踩坑：`System.Collections.Immutable` 不在其中，用 `Localization.LanguageList`（类型是 `ImmutableList<ModLanguage>`）会报 `CS0012`。遍历语言请用 `Enum.GetValues<ModLanguage>()`。

## 5. Mod 主类

`Mod` 子类必须 `public`，宿主启动时会扫描程序集里第一个 `Mod` 子类并实例化它。

```csharp
using ModShardLauncher.Mods;

namespace MyFirstMod;

public class MyFirstMod : Mod
{
    public override string Name => "MyFirstMod";
    public override string Author => "YourName";
    public override string Description => "我的第一个 mod";
    public override string Version => "v1.0.0.0";

    public override void PatchMod()
    {
        // 打补丁时执行：往 data.win 里写表、注入代码都放这里
    }
}
```

| 成员 | 作用 |
|---|---|
| `Name` | 显示名，也是 `Mods` 列表和启用状态里用的名字 |
| `Author` / `Description` | 卡片上显示的信息 |
| `ShortDesc` / `TargetVersion` | 旧接口，保留但当前 UI 不显示 |
| `Version` | 你自己的模组版本（显示在卡片上） |
| `LoadAssembly()` | 扫描到包时调用，早于打补丁；适合准备数据 |
| `PatchMod()` | 打补丁时调用；**资源注入（贴图）已经先做完** |
| `ModWeapons` | 新武器清单：宿主在 `PatchMod()` 之后逐个写进武器表和装备表（见 §7.2） |

约束：

- 一个程序集里写多个 `Mod` 子类没有意义，宿主只取第一个；
- **成员上的 `///` 注释会让编译失败**（`CS1569`，打包器缺陷，见 §10.2）。类/命名空间级别可以，成员一律用 `//`。

打补丁的时序（`PatchRunner`）：

```text
加载 data.win
  → GameTableCodec 读出 weapons / equipment 两张表
逐个启用的模组：
  → TextureInjector.Inject(mod)   // PNG → 图集 → 精灵
  → mod.PatchMod()                // 你的代码
  → mod.ModWeapons 里登记的武器    // Msl.AddWeapon：武器表 + 装备表
全部结束后：
  → 署名房间、模组菜单、掉落脚本注入
```

## 6. 编译成 .sml

### 6.1 GUI

左侧「模组源码」→ 找到工程卡片 → 点「编译」。编译**不需要先打开 `data.win`**，成功后会自动刷新「模组」列表。

### 6.2 CLI

```powershell
ModShardLauncher.CLI.exe compile --source "%LOCALAPPDATA%\ModShardLauncher\ModSources\MyFirstMod"
```

产物固定落在 `Mods\<文件夹名>.sml`，例如 `Mods\MyFirstMod.sml`。包内的版本串是启动器版本（示例为 `v0.13.2.0`），和启动器版本不同时打补丁只会警告，不会拒绝。

退出码：`0` 成功，`1` 参数错误，`2` 编译/补丁失败，`3` 未处理异常。

### 6.3 打包规则（实测）

| 目录里的内容 | 处理 |
|---|---|
| 子目录 | 递归扫描（`bin`、`obj`、`.vs` 排除） |
| `.cs` | 不打包；编译成程序集放进包里 |
| `icon.png`（根目录） | 作为卡片封面，**不**进游戏图集 |
| 其它 `.png` | 作为贴图注入游戏（见 §7.6） |
| `.gml` / `.lua` | 原样打进包；运行时用 `Mod.ModFiles.GetCode` / `GetFile` 读取 |
| 其它扩展名（`.json`、`.txt`、`.wav`、`.py` …） | **被丢弃**，不报错也不进包（见 §10.2 #13） |

白名单按扩展名精确匹配（实测：`.wav` / `.json` / `.txt` 不进包，`.gml` / `.lua` 进包，内容不变）。二进制资源（音频、配置等）想进包，就在原名后面再加 `.lua`。

一个 6 张 PNG 的示例包，用 `SmlPackageReader` 读出来是：

```text
name=MyFirstMod version=v0.13.2.0 chunks=7 iconBytes=680 assembly=MyFirstMod
  MyFirstMod\icon.png
  MyFirstMod\Textures\s_char_myfirstweapon_0.png
  MyFirstMod\Textures\s_char_myfirstweapon_1.png
  MyFirstMod\Textures\s_inv_myfirstweapon_0.png
  ...
```

### 6.4 编译失败怎么办

| 现象 | 原因 | 处理 |
|---|---|---|
| `error CS1569: 写入 XML 文档文件时出错` | 成员上有 `///` 文档注释 | 改成 `//` |
| `error CS0012: ... 在未引用的程序集中定义` | 用了未随附的程序集（如 `ImmutableList`），或用了 UTMT 类型但没引用 `UndertaleModLib` | 改用基础 API / 补引用 |
| `error CS0246` / `CS0103` | 缺 `using`、拼错 API 名 | 见 §13 |

## 7. 第一把武器

### 7.1 武器数据在哪

武器存在脚本 `gml_GlobalScript_table_weapons` 里：一行一件武器，`;` 分隔，**第一行是表头**（当前版本 84 列）。`Weapon.Columns` 就是这个表头的列名（游戏顺序），`Weapon.Weapon2String(weapon).Item1` 产出的就是一整行。

一般不需要直接碰表：§7.2 新增、§7.4 修改都有 API；只有 API 没覆盖的列才需要 §7.5 的手写方式。

关键列（完整列表看 `Weapon.Columns`，**不要写死下标**）：

| 列 | 含义 | 示例 |
|---|---|---|
| `name` | 显示键，装备描述表按它关联 | `Homemade Blade` |
| `Tier` | 等级 1–5 | `1` |
| `id` | 游戏内部 ID | `sword09` |
| `Slot` | 栏位：`sword` / `axe` / `mace` / `dagger` / `spear` / `2hsword` … | `sword` |
| `Subtype` | 子类型，决定具体武器类别：`sword` / `saber` / `cleaver` / `axe` / `poleaxe` … | `sword` |
| `rarity` | `Common` / `Unique` | `Common` |
| `Mat` | `wood` / `metal` / `leather` | `metal` |
| `Price` | 价格 | `100` |
| `MaxDuration` | 最大耐久 | `55` |
| `Slashing_Damage` / `Piercing_Damage` / `Blunt_Damage` … | 各伤害类型 | `16` |
| `Hit_Chance` / `CRT` / `CRTD` / `PRR` … | 命中、暴击、格挡 | `3` |
| `tags` | 标签，如 `aldor common`、`unique`、`elven` | `aldor` |
| `upgrade` / `fireproof` / `NoDrop` / `audio` | 升级路径、防火、不可掉落、音效 | 多为空 |

### 7.2 新增武器：继承 `Weapon`

新建一个类继承 `Weapon`，在 `SetDefaults()` 里先用 `CloneDefaults` 抄一把同类原版武器，再覆盖要改的字段：

```csharp
public class MyFirstWeapon : Weapon
{
    public override void SetDefaults()
    {
        // 复制原版“Homemade Blade”的数值属性（Name / ID / 译文不会被复制）
        CloneDefaults("Homemade Blade");

        Name = "MyFirstWeapon";
        ID = "sword90";
        Subtype = "sword";

        Price = 300;
        MaxDuration = 100;
        SlashingDamage = 25;
        HitChance = 5;

        NameList[ModLanguage.English] = "My First Weapon";
        NameList[ModLanguage.Chinese] = "我的第一把武器";
        WeaponDescriptions[ModLanguage.English] = "Smells of fresh oil.";
        WeaponDescriptions[ModLanguage.Chinese] = "闻起来像刚上过油。";
    }
}
```

然后在 `PatchMod()` 里构造它并登记到 `ModWeapons`：

```csharp
public override void PatchMod()
{
    MyFirstWeapon weapon = new MyFirstWeapon();
    weapon.SetDefaults();
    ModWeapons.Add(weapon);
}
```

宿主会在 `PatchMod()` 返回后逐个调用 `Msl.AddWeapon`，一次写入：

- 武器表一行（84 列，按表头生成）；
- 装备描述表三行（名称 / 描述 / 性别）。

也可以跳过 `ModWeapons`，直接 `Msl.AddWeapon(weapon)` 立即写入。

注意：

- `CloneDefaults` 只复制**数值属性**，`Name`、`ID` 和各语言译名需要自己写；
- `Subtype` 要填该武器实际存在的类型（上面的表里有常用值）。`Msl.InjectTableWeapons` 这种便捷接口会按 `Slot` 猜一个常见值，`Subtype` 想精确控制就用本节的写法；
- `name` 是装备描述表的键，必须唯一。

### 7.3 装备描述表：自动写三行

`AddWeapon`（以及 `ModWeapons`）会自动追加三行：

```text
MyFirstWeapon;;My First Weapon;我的第一把武器;...;   ← 名称
MyFirstWeapon;;Smells of fresh oil.;闻起来像刚上过油。;...;   ← 描述
MyFirstWeapon;He;;;He;He;He;He;He;He;;;;   ← 语法性别
```

- 名称/描述来自 `NameList` / `WeaponDescriptions`，没填的语言写作空；
- 性别来自 `Weapon.Genders`；**全部留空时默认按男性写**（`He;;;He;He;He;He;He;He;;;;`）。女性/中性名词请覆盖对应语言：

```csharp
weapon.Genders[ModLanguage.German] = "It";     // das Schwert
weapon.Genders[ModLanguage.French] = "She";    // l'épée
```

想手工改表也可以（`GameTableCodec.GetTable/SetTable`），但用 API 更省事。

### 7.4 改原版武器

```csharp
Weapon blade = Msl.GetWeapon("Homemade Blade");   // 按 name（或唯一前缀）找行
blade.Price = 999;
blade.SlashingDamage = 40;
Msl.SetWeapon("Homemade Blade", blade);           // 写回 data.win
```

- `GetWeapon` 把整行解析成 `Weapon`，名称/描述从装备表读出；
- `SetWeapon` 只覆盖 `Weapon` 能映射的列，表里其它列（API 不认识的）原样保留；
- `Name` / `ID` 留空时不会把行清空，可以只改数值；
- 写入是**立即持久化**到当前 `data.win`，不需要再手动 `SetTable`；
- 改了 `Name` 会同步改装备表三行的键。

### 7.5 没有 API 覆盖时：直接改表

`Weapon` 没有映射到的列（以及将来游戏新增的列），可以直接按表头写：

```csharp
List<string> table = GameTableCodec.GetTable("gml_GlobalScript_table_weapons")!;
string[] header = table[0].Split(';');
int index = table.FindIndex(line => line.StartsWith("Homemade Blade;"));
string[] row = table[index].Split(';');

row[Array.IndexOf(header, "audio")] = "light";

table[index] = string.Join(";", row);
GameTableCodec.SetTable(table, "gml_GlobalScript_table_weapons");
```

关键是**按列名定位**、行里的单元格数与表头一致，这样游戏加列也不会错位。

### 7.6 贴图

贴图靠**文件名**认领精灵：

| 文件 | 含义 |
|---|---|
| `s_char_<武器名>_0.png`、`_1.png` | 角色持握（示例原版 48×40，2 帧：通常是两种持握/挥砍姿态） |
| `s_inv_<武器名>_0.png` … `_2.png` | 背包图标（3 帧 = 完好 / 磨损 / 破损），尺寸是 **27 的倍数**，例如 1×3 格 = 27×81 |
| `s_loot_<武器名>_0.png` | 掉落在地上 |

要点：

- `<武器名>` 是**小写、去掉空格**的写法：`Homemade Blade` → `s_inv_homemadeblade`。原版武器全都遵守这个命名，游戏也是按 `"s_char_"` / `"s_inv_"` 前缀去找精灵的（`data-o.win` 里能直接搜到这两个字面量），所以名字必须完全对得上；
- 文件名末尾 `_N` 是帧号，从 0 开始；不带 `_N` 就是第 0 帧；
- 双手武器只需要 `s_char_`，因为没有左右手两种拿法；
- 放在工程里**任意子目录**都可以，会递归打包；
- 单张不超过 2048×2048；启动器自动拼图集（2px 间隔），不用自己排版；
- 如果精灵已存在，会**替换对应帧**；不存在则新建精灵（原点固定 0,0，可能需要你自行适应位置）；
- `icon.png` 例外：它是模组封面，不参与游戏图集。

### 7.7 先验证管线，再做新武器

新武器进游戏后还得有获取途径（商人、掉落表），这属于掉落表/商人的活（见 §13）。第一次做模组时，建议先用 §7.4 把 `Homemade Blade` 的价格改成 999：进游戏看一眼价格变了，就说明编译、打包、打补丁、表格写入整条链路是通的。

## 8. 本地化

`ModLanguage` 的枚举顺序就是游戏表的列顺序，共 12 种：

```csharp
Russian, English, Chinese, German, Spanish, French,
Italian, Portuguese, Polish, Turkish, Japanese, Korean
```

- `Localization.SetDictionary("文本")`：12 种语言全填同一个值；
- `Localization.SetDictionary("俄;英;中")`：按顺序填，其余语言回退到**英文**；
- **英文必须给值**：`SetDictionary` 会直接取 `English` 作默认值，缺了会抛 `KeyNotFoundException`；
- 武器名称/描述 → `Weapon.NameList` / `Weapon.WeaponDescriptions`（§7.3 自动写表）；
- 物品 → `Msl.InjectTableItemLocalization(oName, name, id, description)`（写 `gml_GlobalScript_table_items`）；
- 对话可用 `LocalizationSentence` / `Msl.InjectTableDialogLocalization`。

玩家能看到的文字都走游戏表，别写死在 C# 里——写死的话换语言不会变。

## 9. 打补丁、启用与分发

### 9.1 GUI

1. 先把游戏目录里的原版 `data.win` 改名备份：`original.win`（`vanilla.win` 同样可以）；
2. 「打开 data.win」选择这份**改名后的原版副本**（补丁只改副本，不动游戏正在用的文件）；
3. 「模组」页启用要用的模组；
4. 「打补丁并保存」（Ctrl+S），输出到游戏目录，文件名建议 `data.win`（换成别的名字也能用，见 §9.3）。

保存对话框在源文件不叫 `data.win` 时默认建议 `data.win`，否则建议 `<原文件名>_patched.win`；启动器**不允许覆盖当前已加载的原版文件**，会提示换一个输出路径。打补丁失败不会写坏原文件（先写临时文件再替换）。

### 9.2 CLI

```powershell
# 启用全部模组
ModShardLauncher.CLI.exe patch --data "<原版>\data.win" --output ".\data_patched.win" --all

# 只启用指定模组
ModShardLauncher.CLI.exe patch --data "<原版>\data.win" --output ".\data_patched.win" --enable MyFirstMod,Quicksave
```

`--mods <目录>` 可以指定别的 `Mods` 目录（例如给 CI 用）。启用状态保存在 `Settings.json` 的 `EnabledMods` 里。

### 9.3 装进游戏

补丁文件不是必须叫 `data.win`：游戏目录里**存在** `data.win` 时启动会直接加载它，叫别的名字则每次启动都要在选择界面手动选一次。所以最省事的做法是保存成 `<游戏目录>\data.win`，原版按 §3.1 留作 `original.win`（游戏不会自动加载它）。换游戏版本时先确认用的是 `modbranch` 或与数据表结构匹配的版本。

### 9.4 分发

给玩家的只有 `.sml`：放进 `%LOCALAPPDATA%\ModShardLauncher\Mods` 即可。WPF 时代编译的旧包**不需要重新编译**也能用——Core 的程序集名固定为 `ModShardLauncher`、版本固定 `1.0.0.0`，旧包按名字就能绑定到新宿主。

## 10. 已知差异与缺陷

旧版 WPF 启动器与旧版本游戏的资料已过时。下面是本仓库的实际行为，**按“现象 / 原因 / 做法”记**，遇到对不上的地方先看这里。

### 10.1 已修复：旧教程里的坑，现在不用绕

v0.13.2.0 之前的版本里，武器表 API 与游戏当前的 84 列结构不一致；下面这些都已修好，并加了回归测试：

| 旧问题 | 现在 |
|---|---|
| `CloneDefaults("Homemade Blade")` 抛 `FormatException: The input string 'Common' ...` | `Weapon` 的字符串形式改成**武器表行本身**，按列名解析；`CloneDefaults` / `GetWeapon` 正常工作 |
| `Msl.SetWeapon` 把整行写成旧格式（70 多列） | 只覆盖 `Weapon` 映射的列，其它列原样保留，并立即写回 `data.win` |
| `Msl.InjectTableWeapons` 生成 80 列，`rarity` 落到 `Subtype` 列 | 按表头生成，列数永远与表一致；`Subtype` 按 `Slot` 取常见值 |
| `ModWeapons` 里的武器不会出现在游戏里 | 宿主在 `PatchMod()` 之后调用 `Msl.AddWeapon`，写入武器表 + 装备表三行 |
| 直接改 `ModLoader.Weapons` 的改动会被后续写表覆盖 | 每次 `SetTable` 都会把快照指向刚写入的列表，读写不再错位 |

### 10.2 仍然存在的差异

| # | 现象 | 原因 | 做法 |
|---|---|---|---|
| 1 | 旧版把 `Mods`/`ModSources` 放在 exe 同级 | 本版改到 `%LOCALAPPDATA%\ModShardLauncher` | 见 §3 |
| 2 | 旧版必须先打开 `data.win` 才能编译 | 本版编译不依赖游戏数据 | 直接编译 |
| 3 | `Description[ModLanguage.Chinese]` 编译不过 | 这个字段在 `Weapon` 上叫 `WeaponDescriptions` | 用 `WeaponDescriptions[ModLanguage.Chinese]` |
| 4 | 成员上的 `///` 注释导致 `CS1569` | 打包器的 XML 文档写入缺陷（`count ('-3')`），与中英文无关 | 成员注释统一用 `//` |
| 5 | `CS0012: ImmutableList<> 在未引用的程序集中定义` | `Localization.LanguageList` 的类型是 `ImmutableList`，打包引用里没有 `System.Collections.Immutable` | 用 `Enum.GetValues<ModLanguage>()` |
| 6 | 代码里用 `UndertaleData` 报 `CS0012` | 宿主不会把 `UndertaleModLib` 传递给你的工程 | csproj 显式引用 `UndertaleModLib.dll`（§4.3） |
| 7 | 旧版要求把 `ModShard.dll` 放进游戏目录、用 `give` 命令 | 本版不提供脚本控制台，也不会注入控制台 GML；`give` / `ModShard.dll` 都不可用 | 调试用日志（§11）；取武器走掉落表/商人 |
| 8 | 打补丁时警告 `built with msl ... different from ...` | `.sml` 的构建版本和当前启动器不同 | 只是警告，可以继续；要消掉就重编译 |
| 9 | 打开 `data.win` 报 YYC | 游戏用了 YoYo 编译器，数据不可改 | 换 `modbranch` 或非 YYC 版本 |
| 10 | 用原版 MSL（net6.0）打包时报 `System.Linq, Version=10.0.0.0` 找不到，堆栈停在 `PatchMod()` | mod 源码用了 LINQ（如 `.Any(...)`），打包器按当前 .NET 10 环境引用 `System.Linq`，net6 启动器加载时找不到 | 不依赖 LINQ（改 `foreach`），并在 csproj 关闭 `ImplicitUsings`，只用 netstandard 基础 API |
| 11 | 打补丁时报 `Assembly with same name is already loaded` | `Mods` 目录里有多个程序集名相同的 `.sml`（例如 `Map_TP.sml` 与 `Map_TP-v0.3.0.0.sml`） | 只保留一份 `.sml`，旧包改成 `.sml.bak` 或移出 `Mods` |
| 12 | 同一个 `.sml` 在两个启动器之间表现不一致 | Avalonia 版是 net10，打的包引用 `System.Private.CoreLib 10`；原版 MSL 是 net6 | 用哪个启动器进游戏，就用哪个启动器打包 |
| 13 | 工程里的 `.json` / `.txt` / `.wav` 打包后不在包里，运行时 `ModFiles.GetFile` 返回 `null` | 打包器只按扩展名收录 `.gml` / `.lua` / `.png`，其它扩展名**静默丢弃**（不报错、不警告） | 资源改名成以 `.lua` 结尾（音频 `x.wav.lua`、配置 `x.json.lua`，内容是原始字节），并在代码里对 `GetFile` 判空（§14.1） |
| 14 | 换了素材 / 改了 GML，重打同一份**已打过补丁**的 `data.win`，进游戏还是旧的 | 模组自己写的幂等判断（`if (FindSound(name) != null) continue`、`if (CodeExists(name)) continue`）会跳过已存在的资源与代码 | 每次都在干净的 `original.win` 上重打；幂等判断只用来防止**同一次**补丁里重复添加 |
| 15 | `Msl.AddFunction` 注入的函数运行时报 `_amount not set` 之类的实例变量错误 | 打包器不会把**具名参数**写进函数的 locals 表，函数体里的参数名被当成实例变量 | 参数一律写 `argument0` / `argument1`，函数体内先 `var _x = argument0;`（§14.3） |
| 16 | 照反编译文本写的锚点找不到，或插入位置落进 `if` 与 `{` 之间 | 反编译器输出**不带分号**；`if (...)` 行后面紧跟的可能是 `{` | 锚点取分支内第一条语句，不带分号；比较前把空格/制表符/回车去掉（§14.6） |
| 17 | 模组菜单的下拉框读回来是 `"0"` 而不是第一个选项 | `UIComponent(..., ComboBox, string[])` 的 `DefaultValue` 是 int `0`，回读走 `ini_read_string(..., "0")` | GML 里把 `"0"` 当作第一个选项；下拉值是**选项文本本身**（`global.x == "方案 B"`），不是下标（§14.5） |
| 18 | 新增的音效播不出来，或播成别的音 | 只加了 `UndertaleSound` 没加 `UndertaleEmbeddedAudio`，或 `AudioID` / `AudioGroup` 没接上 | 两者成对添加，并设 `sound.AudioFile`、`AudioID = data.EmbeddedAudio.Count - 1`、`AudioGroup = data.AudioGroups[0]`、`GroupID = 0`（§14.2） |
| 19 | `Msl.LoadGML("gml_Script_xxx")` 找不到代码条目 / 改完没生效 | 一个脚本文件里有多个函数时，函数是**嵌套代码条目**，不能单独加载改写 | 用父条目 `gml_GlobalScript_<脚本名>` 整段读改写 |
| 20 | 进游戏报 `Unable to find function xxx`，堆栈指到事件里的一行调用 | `Msl.AddFunction` 只新增代码条目、**不注册函数名**；跨条目调用时运行期查不到（§14.3 有实测堆栈） | 注入代码一律内联，不新增函数；状态用 `global.xxx` 传 |
| 21 | 注入的 GML 报 `Variable o_xxx.yyy(...) not set before reading it` | 代码比资产先编译：`o_xxx` 当时还不在资产表里，GML 编译器把未知资产名编译成了**实例变量读取**（不报编译错） | 先 `Msl.AddObject` / 贴图注入，再编译引用它们的代码；或在 GML 里用 `asset_get_index("o_xxx")` 查 |
| 22 | 打补丁报 `Expected assignment operator around line N`；改成 `with (obj) method(...)` 后能编译，但进游戏报 `Unable to find function method` | MSL 的 GML 编译器**不支持 `obj.method(...)`**；而 `with (obj) method(...)` 是按**全局函数名**查找，struct 的 static 方法根本没注册成全局函数 | 直接给成员赋值（`obj.pointsArray = [...]`），或 `var _f = obj.method; _f(...)`；别用 `with`（§16.3） |
| 23 | 进游戏前加载包就抛 `InvalidDataException: invalid chunk count ...` | `.sml` 的版本串没有终止符，读取器靠"遇到非数字就停"判断长度；**纹理块数量落在 48–57** 时首字节正好是 ASCII 数字，被吞进版本号（v0.13.2.0 已修） | 用新版 CLI/GUI；或把纹理数量调开这个区间 |
| 24 | 自定义文本全部不生效：界面显示 `className[...]` / `classDescription[...]` / `name[xxx]` | `scr_tableWriteMap` **会先清空目标 map 再填充**，注入插在它的调用之前就会被抹掉 | 注入点放在**这张 map 自己的** `scr_tableWriteMap` 之后（§16.5） |
| 25 | 想 hook 伤害函数，报 `GML Compile Error: Errors parsing code` | 部分原版函数的反编译文本**无法回编译**（`scr_damage_calculation` / `scr_skill_damage` / `scr_physical_damage`） | 换用能回编译的入口：`scr_attack_result_hit` / `scr_simple_damage` / `scr_damage` / `scr_cast_spell`（§16.4） |
| 26 | `.ps1` 里带中文，`powershell -File` 报 `Missing closing '}'` | Windows PowerShell 5.1 默认按 **ANSI** 读脚本，UTF-8 无 BOM 的中文被打断 | 脚本写成纯 ASCII；或存成带 BOM 的 UTF-8 |
| 27 | `--enable <名字>` 报 `Mod xxx not found` | 启用的名字是包里的 `Mod.Name`；**没有 `icon.png` 时**回退成文件名（含 `-vX.Y.Z.0`） | 先 `list` 看真实名字；给工程补 `icon.png` |
| 28 | 技能树节点一直红字「需要阅读相应文献才能解锁」 | 解锁调用早于格子实例创建：分支基类用 `Alarm_0` 延后 2 帧建实例，此时 `with (_obj)` 是空操作；另外分支本身也要先「开启」 | 分支 Create 里 `alarm[3] = 10`，`Alarm_3` 里先 `scr_tier_open("分支text")` 再 `scr_skill_open_from_array1d(tier)`（§16.3） |
| 29 | 被动技能提示框里描述位显示成 `0` | `scr_skill_atr` 对**被动**只用 `argument0` 当文本键（`name = skill_name_text[arg0]`、`desc = skill_desc[arg0]`），传 `0` 就查不到 | 被动必须传技能名字符串：`scr_skill_atr("Ash_Edge")`；主动才传数字（那个参数对主动没意义） |

## 11. 验证与调试

### 11.1 日志

日志分两个阶段，位置完全不同，**别在错的地方找**。

**构建期（打补丁阶段）**

- GUI：底部固定高度的日志面板（补丁过程实时刷新），文件在 `%LOCALAPPDATA%\ModShardLauncher\logs`；
- CLI：控制台 + `logs\cli_*.log`；
- 这一阶段能看到的报错：包解析失败、锚点找不到、`GML Compile Error`、贴图注入失败。

**运行期（进游戏之后）**

游戏是**沙盒运行**的，模组写文件只会落在：

```text
C:\Users\<用户名>\AppData\Local\StoneShard\
```

所以：

- `show_debug_message` / `show_message` **不会**产生你能打开的日志文件。别去游戏安装目录、也别去 `%LOCALAPPDATA%\ModShardLauncher\logs` 找运行时输出——那里没有；
- 打补丁注入的 `o_msl_log` / `scr_msl_log` 同样受沙盒约束，实际落地在沙盒目录（旧版文档写的"游戏目录 `Logs\`"在 `Program Files` 下通常写不进去）；
- 需要临时诊断时，用 GML 的文件函数按**相对路径**写一个 txt，这也是社区模组的通行做法（沙盒目录里常见的 `qb_diag.txt`、`maptp_diag.txt` 就是这么来的）：

```gml
// 相对路径 → %LOCALAPPDATA%\StoneShard\my_diag.txt
var _f = file_text_open_write("my_diag.txt");
file_text_write_string(_f, "skill=" + string(array_length(skill))
    + " points=" + string(connectionsRender.pointsArrayLength)
    + " lines=" + string(connectionsRender.linesArrayLength));
file_text_close(_f);
```

要点：

| 注意 | 说明 |
|---|---|
| 用**相对路径** | 拼绝对路径会被沙盒拦掉或被重定向，写不进去 |
| 必须 `file_text_close` | 不关文件内容可能没落盘，打开看到的是空文件 |
| `open_write` 每次覆盖 | 适合"记录最后一次状态"；要累计历史用 `file_text_open_append` |
| 诊断代码是临时的 | 定案后删掉，别把 `*_diag.txt` 的写入逻辑留在正式版里 |
| 位置选对 | 想验证"某个事件到底有没有执行"，就把写文件放在 `Alarm_N` / `Other_N` 的第一行——这比推断可靠得多 |

如果只需要"当场看一眼"，也可以借用游戏自己的消息栏（左下角滚动文字）：

```gml
scr_actionsLogAddMessage("skill=" + string(array_length(skill)));
```

### 11.2 排查顺序

1. `Mods` 列表里能看到你的包吗？看不到 → 包没生成或解析失败（看 `logs`）；
2. 打补丁时该模组的行是 `Patching succeeded` 还是失败？
3. 表行改对了吗？用 CLI 打补丁后读回校验（见 §11.3）；
4. 进游戏没效果：先确认启用状态、确认游戏用的是新数据文件、确认没有别的模组同时改同一行。

### 11.3 本文的验证方式

本文所有代码用下面这条链路验证过（.NET SDK 10.0.402、启动器 v0.13.2.0、游戏目录的 `original.win` 作为原版）：

```powershell
# 1. 编译
ModShardLauncher.CLI.exe compile --source "%LOCALAPPDATA%\ModShardLauncher\ModSources\MyFirstMod"

# 2. 打补丁
ModShardLauncher.CLI.exe patch --data "<游戏目录>\original.win" --output ".\out.win" --all

# 3. 读回 out.win，检查武器表行、装备表行、精灵
```

读回结果（节选）：

```text
weapons rows=451 headerCols=84
Homemade Blade: Tier=1 id=sword09 Subtype=sword Price=999 Slash=16
MyFirstWeapon:  Tier=1 id=sword90 Subtype=sword Price=300 Slash=25
InjectedSword:  Tier=2 id=sword91 Subtype=sword Price=
装备表: MyFirstWeapon 三行 / 每行 14 列
精灵: s_char_myfirstweapon 2 帧 48×40；s_inv_myfirstweapon 3 帧 27×81；s_loot_myfirstweapon 1 帧 26×13
```

也就是说：**表格写入、贴图注入、包结构**都验证到了；“进游戏后长什么样”需要你自己开游戏确认（仓库的自动化测试不启动游戏）。

## 12. 附录：完整示例

`ModSources\MyFirstMod\MyFirstMod.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyFirstMod</AssemblyName>
    <RootNamespace>MyFirstMod</RootNamespace>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="ModShardLauncher">
      <HintPath>$(MslPath)</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

`ModSources\MyFirstMod\MyFirstMod.cs`：

```csharp
using ModShardLauncher;
using ModShardLauncher.Mods;

namespace MyFirstMod;

public class MyFirstMod : Mod
{
    public override string Name => "MyFirstMod";
    public override string Author => "YourName";
    public override string Description => "指南示例：新增一把武器，并改一把原版武器";
    public override string ShortDesc => "Guide sample";
    public override string Version => "v1.0.0.0";

    public override void PatchMod()
    {
        // 1) 改一把原版武器：整行读出来、改一格、再写回去。
        Weapon blade = Msl.GetWeapon("Homemade Blade");
        blade.Price = 999;
        Msl.SetWeapon("Homemade Blade", blade);

        // 2) 新武器：声明到 ModWeapons，启动器在 PatchMod 之后写入武器表 + 装备描述表。
        MyFirstWeapon weapon = new MyFirstWeapon();
        weapon.SetDefaults();
        ModWeapons.Add(weapon);

        // 3) 便捷接口：只写武器表（没有名称/描述，需要自己补装备表）。
        Msl.InjectTableWeapons(
            name: "InjectedSword",
            Tier: Msl.WeaponsTier.Tier2,
            id: "sword91",
            Slot: Msl.WeaponsSlot.sword,
            rarity: Msl.WeaponsRarity.Common,
            Mat: Msl.WeaponsMaterial.metal,
            tags: Msl.WeaponsTags.aldor);
    }
}

public class MyFirstWeapon : Weapon
{
    public override void SetDefaults()
    {
        // 复制原版“Homemade Blade”的数值属性（Name / ID / 译文不会被复制）
        CloneDefaults("Homemade Blade");

        Name = "MyFirstWeapon";
        ID = "sword90";
        Subtype = "sword";

        Price = 300;
        MaxDuration = 100;
        SlashingDamage = 25;
        HitChance = 5;

        NameList[ModLanguage.English] = "My First Weapon";
        NameList[ModLanguage.Chinese] = "我的第一把武器";
        WeaponDescriptions[ModLanguage.English] = "Smells of fresh oil.";
        WeaponDescriptions[ModLanguage.Chinese] = "闻起来像刚上过油。";
    }
}
```

配一张 `icon.png`（封面）和 `Textures\` 下的 6 张 PNG：

```text
s_char_myfirstweapon_0.png   48×40
s_char_myfirstweapon_1.png   48×40
s_inv_myfirstweapon_0.png    27×81
s_inv_myfirstweapon_1.png    27×81
s_inv_myfirstweapon_2.png    27×81
s_loot_myfirstweapon_0.png   26×13
```

## 13. API 速查

| API | 用途 |
|---|---|
| `Weapon` + `SetDefaults()` + `CloneDefaults(name)` | 用原版武器当模板定义新武器（§7.2） |
| `Mod.ModWeapons` | 登记新武器，`PatchMod()` 之后由宿主写入两张表 |
| `Msl.AddWeapon(Weapon)` | 立即新增武器：武器表一行 + 装备表三行 |
| `Msl.GetWeapon(name)` / `Msl.SetWeapon(name, weapon)` | 读 / 写一把已有武器（立即持久化） |
| `Weapon.Columns` | 武器表列名（游戏顺序，84 列） |
| `GameTableCodec.GetTable(name)` / `SetTable(table, name)` | 读写 `gml_GlobalScript_table_*` 这类分号表 |
| `Localization.SetDictionary(...)` | 把文本铺成 12 语言字典（可传单个字符串或字典） |
| `Msl.InjectTableItemLocalization(...)` | 物品名称/描述（`table_items`） |
| `Msl.InjectTableWeapons(...)` | 便捷新增武器：只写武器表，`Subtype` 按 `Slot` 猜（§7.2 有更精确的写法） |
| `Msl.AddLootTable(...)` / `Msl.AddReferenceTable(...)` | 掉落表、引用表（让新武器能刷出来） |
| `Msl.LoadGML(codeName)` / `Msl.AddNewEvent(...)` | 改/加 GML 代码与事件（`AddNewEvent` 传代码正文，不是函数名） |
| `Msl.AddFunction(code, name)` | ⚠ 只新增代码条目、不注册函数名：跨条目调用会 `Unable to find function`，见 §14.3 |
| `ModInterfaceEngine` / `Msl.HookFunction(...)` | 脚本控制台已移除；类型仅作兼容占位，`HookFunction` 调用会抛 `NotSupportedException` |
| `Msl.AddMenu(...)` / `Msl.AddCreditDisclaimerRoom(...)` | 模组菜单、署名房间 |
| `Mod.ModFiles.GetFile / GetCode` | 读自己包里打进的其他文件（JSON/GML） |

完整公开 API 见源码 `src/ModShardLauncher.Core/Modding/`（`Msl` 的公开方法大多带中文注释）。

---

## 14. 附录 B：注入音效、菜单与 GML 函数

§7 走的是表格和贴图；如果你的模组是另一条路——往 `data.win` 里加新的音效资源、加自己的 GML 函数、用一个控制器对象驱动、再挂一个菜单项——坑集中在**二进制资源、GML 注入、菜单读写**三块，按顺序记在下面。示例里的名字都是占位，换成自己的即可。

### 14.1 资源：二进制文件叫 `*.lua`

| 工程里的文件 | 进包 | 读法 |
|---|---|---|
| `gml/my_helper.gml` | 是 | `ModFiles.GetCode("gml/my_helper.gml")` |
| `Data/clip1.wav.lua` | 是 | `ModFiles.GetFile("Data/clip1.wav.lua")` |
| `Data/clip1.wav` | 否 | 被丢弃（§10.2 #13） |
| `config.json` | 否 | 要叫 `config.json.lua` 才进包 |

```csharp
byte[] wav = ModFiles.GetFile("Data/" + name + ".wav.lua");
if (wav == null || wav.Length < 44)
    throw new Exception("audio chunk is missing: " + name);
```

`GetFile` 只按 **basename** 匹配：`Data/clip1.wav.lua` 与 `Other/clip1.wav.lua` 会撞车，不同目录必须用不同文件名。

### 14.2 音效：`Sound` 与 `EmbeddedAudio` 成对添加

```csharp
// 模板：随便找一个已在 audiogroup_default 里的音效
UndertaleSound template = FindSound(data, "snd_empty_file");
byte[] wav = ModFiles.GetFile("Data/" + name + ".wav.lua");

UndertaleString assetName = data.Strings.MakeString("snd_my_" + name);
UndertaleEmbeddedAudio audio = new UndertaleEmbeddedAudio();
audio.Name = assetName;
audio.Data = wav;                     // 真 WAV 字节，不用再转码
data.EmbeddedAudio.Add(audio);

UndertaleSound sound = new UndertaleSound();
sound.Name = assetName;
sound.Flags = template.Flags;
sound.Type = template.Type;
sound.File = data.Strings.MakeString(assetName.Content + ".wav");
sound.Effects = template.Effects;
sound.Volume = template.Volume;
sound.Pitch = template.Pitch;
sound.Preload = true;
sound.AudioGroup = data.AudioGroups[0];          // = audiogroup_default
sound.AudioFile = audio;                         // 不接就静音
sound.AudioID = data.EmbeddedAudio.Count - 1;    // 必须是“添加后”的索引
sound.GroupID = 0;
sound.AudioLength = 0f;                          // 交给游戏自己算
data.Sounds.Add(sound);
```

### 14.3 GML 函数：参数只能写 `argumentN`

```gml
// gml/my_add.gml
function my_add(argument0)
{
    my_init();

    var _amount = floor(abs(argument0));
    if (_amount <= 0)
        return;

    global.my_pending += _amount;
    global.my_deadline = current_time + 1200;
}
```

写成 `function my_add(amount)` 能编译，但运行时 `amount` 被当成实例变量读，报 `not set`（§10.2 #15）。

**但 `Msl.AddFunction` 不能用来做“跨条目调用”**（v0.13.2.0 实测）。它的实现只是新建一个代码条目：

```csharp
// CodeInjection.cs
public static UndertaleCode AddFunction(string codeAsString, string name)
{
    UndertaleCode scriptCode = AddCode(codeAsString, name);   // 只是 Data.Code.Add
    MslContext.Data.Code.Add(MslContext.Data.Code[0]);        // 再把它挪到最前面
    MslContext.Data.Code.RemoveAt(0);
    return scriptCode;
}
```

它**不注册函数名**。游戏运行期按名字查 `Data.Functions`（原版每个脚本都成对存在 `gml_GlobalScript_xxx` + `gml_Script_xxx`），我们新增的名字不在里面，所以从**别的代码条目**（新对象的事件、被 `ReplaceBy` 改写的原版事件、`InsertBelow` 插进去的语句）调用它，进游戏就炸：

```text
ERROR in action number 1 of Create Event for object <undefined>:
Error on load
Unable to find function scr_caligo_set_form
```

§14.4 那种 `Msl.AddNewEvent(ticker, "my_init()", ...)` 同理：只要 `my_init` 是 `AddFunction` 加的，运行期都找不到。

**结论：注入代码一律内联，不新增函数。** 三条替代路线：

| 想做的事 | 做法 |
|---|---|
| 多处复用同一段逻辑 | 直接复制代码块（十几行以内足够），别抽函数 |
| 跨代码条目传状态 | 用 `global.xxx`（配 `variable_global_exists` 判空），别用函数返回值 |
| 一定要一个“函数” | 把逻辑写进**某个对象的事件**，别处用 `instance_create` / 事件调用触发 |

> 未验证方向：手工在 `Data.Functions` 里补一条和 `gml_GlobalScript_xxx` 配对的 `gml_Script_xxx` 记录，或许能让 `AddFunction` 真正可用——但这属于绕开公开 API 改数据结构，本指南不给结论。

### 14.4 控制器对象：持久化 + 挂在 `START`

```csharp
UndertaleGameObject ticker = Msl.AddObject("o_my_ticker", isPersistent: true);

// 传进去的必须是「事件本身的完整代码」，不能是对自定义函数的调用：
// AddFunction 加的函数据运行期查不到（§14.3，实测报 Unable to find function）
Msl.AddNewEvent(ticker, ModFiles.GetCode("gml/my_ticker_Create.gml"), EventType.Create, 0);
Msl.AddNewEvent(ticker, ModFiles.GetCode("gml/my_ticker_Step.gml"), EventType.Step, 0);

Msl.GetRoom("START").AddGameObject("Instances", ticker);
```

- 不持久化的话换房间就断；挂 `START` 才能保证每个存档都有实例。
- 初始化函数里用 `if (variable_global_exists("my_ready")) return;` 守卫，避免回主菜单再进时重复创建 `ds_list`。
- 判断“上一段播完没有”用**资源索引**而不是播放实例 id：`audio_is_playing(soundIndex)`；`audio_play_sound` 返回的实例播完会被回收。

### 14.5 菜单：ComboBox 的默认值不是第一个选项

```csharp
Msl.AddMenu("我的设置",
    new UIComponent("模式", "my_mode", UIComponentType.ComboBox,
                    new[] { "方案 A", "方案 B" }));
```

- 结果写进 `global.my_mode`，值是**选项文本**（`"方案 A"` / `"方案 B"`），不是下标。
- 菜单从未打开过时，`o_msl_initializer` 用 `ini_read_string(..., "0")` 回填，值是字符串 `"0"`。GML 侧要把它当第一个选项处理：

```gml
var _mode = "0";
if (variable_global_exists("my_mode"))
    _mode = global.my_mode;

if (_mode == "方案 B")
{
    // 第二套配置
}
```

- 持久化文件是 `msl_menu_mod.ini`（section 名 = `AddMenu` 的第一个参数），由注入的 GML 在游戏数据目录读写，不在启动器目录里。

### 14.6 锚点：去分号、取语句、squash 比对

不要拿 `if (...)` 行当锚点，用分支内的第一条语句（下面是示意，锚点换成你实际反编译出来的那行）：

```csharp
InsertAfter("gml_GlobalScript_scr_some_script",
            "some_stat_update(\"income\", price)",   // 不带分号，空格也会被 squash 掉
            "my_add(price);");
```

比对前把空格、制表符、回车全部去掉再 `Contains`，这样反编译器换排版也不失配；插入时沿用锚点行的缩进，别破坏 `{}` 结构。找不到锚点就把实际反编译文本落到 `%TEMP%`，报错里带上路径——游戏更新后照着改即可。

### 14.7 素材生成（可选：TTS + ffmpeg）

音效可以自己录，也可以用 TTS 批量生成（`edge-tts` + `imageio-ffmpeg` 自带的 ffmpeg）。批量生成时下面几步必须做，否则拼接处会“咔哒”、音量忽大忽小：

| 处理 | 参数 | 原因 |
|---|---|---|
| 统一格式 | `-ar 24000 -ac 1 -c:a pcm_s16le` | 各段采样率一致才不会在拼接处重采样 |
| 去首尾静音 | `silenceremove` + `areverse` 走两遍 | 词条首尾的静音会拉长句间停顿 |
| 淡入淡出 | 4 ms 淡入 / 10 ms 淡出 | 消除拼接爆音 |
| 峰值归一 | -1 dBFS，增益限 ±12 dB | 不同词条的响度差很多 |

另外：音色名必须带正确的地区前缀。有的音色只有 `zh-TW-` 版本，写成 `zh-CN-` 时服务端返回**空音频**（`NoAudioReceived`），而不是“音色不存在”，很容易误判成网络问题。换音色后先单条试合成，再批量跑。

---

## 15. 附录 C：新增可选角色

§7 做武器、§14 做音效与菜单；这一条是第三条路——**往选人界面加一个新角色**。

结论：不需要改引擎，也不需要外部工具，**一个角色 = 一个继承 `o_player` 的空壳对象 + 一条 `classesMap` 注册记录 + 几条文本 + 几张精灵**，全部能靠 MSL 现有 API 做完。

前置条件：**玩家必须先通关序章**（`account.ini` 里 `Prologue/isComplete=true`）。没通关时点「新游戏」会直接进序章（固定用 `o_verren`），根本走不到选人界面——这是游戏流程，不是 mod 的问题。

> **验证状态**：完整链路已在启动器 v0.13.2.0 + 游戏 `original.win` 上跑通并**实机确认**——编译 → 打补丁 → 读回 `data.win` 校验对象/精灵/GML → 进游戏选卡、开局、存档读档。逐项结果见 [§15.10](#1510-验证)。下面写的尺寸、原点、锚点、属性写入时机都是实测值，不是推测。

### 15.1 角色在数据里是什么

选人界面不认识“角色”，它只认识 `global.classesMap` 这个 ds_map。每个角色一条记录，由 `gml_GlobalScript_scr_classCreate` 写入：

```gml
scr_classCreate(
    arg0  object  ,   // 角色对象（必须继承 o_player）
    arg1  sprite  ,   // 卡片头像，37×50
    arg2  nameKey ,   // "Jorgrim" —— 名字表的键，也是存档里记的键
    arg3  sexKey  ,   // "Male" / "Female"
    arg4  raceKey ,   // "Human" / "Dwarf" / "Elf"
    arg5  countryKey, // "Aldor" / "Skadia" / "Fjall" / "Jacinth" / "Yohara" / "Nistra"
    arg6  classKey,   // 职业名表的键，如 "Reaver"
    arg7  STR, arg8 AGL, arg9 PRC, arg10 Vitality, arg11 WIL,
    arg12 tiersArray, // 开局开放的技能树，如 [global.swords_tier1, global.combat_tier1]
    arg13 perksArray, // 开局 perk 对象列表，可传 []
    arg14 dlc = -4,   // -4 = 不属于任何 DLC
    arg15 dlcRequired = false
);
```

内部会往 `orderArray` 追加对象名，并把上面这些键写进 `classesMap[对象名]`。**这张表就是角色系统的全部数据结构**。

### 15.2 原版 9 个角色注册在哪

在 `gml_Object_o_dataLoader_Other_10` 里，紧跟 `global.classesMap = __dsDebuggerMapCreate();` 之后连着 9 行 `scr_classCreate(...)`：

```gml
scr_classCreate(6978, 6772, "Verren", "Male", "Human", "Aldor", "RelicSeeker", 10, 10, 10, 10, 10, [...], [6733]);
scr_classCreate(6982, 6782, "Velmir", "Male", "Human", "Skadia", "Revenger", 11, 11, 11, 10, 10, [...], [6734]);
scr_classCreate(6979, 6779, "Jorgrim", "Male", "Dwarf", "Fjall", "Reaver", 11, 10, 11, 11, 10, [...], [6729]);
...
```

（前两个参数在这里是对象/精灵**索引**，因为反编译把资产名丢了；我们注入时写 `asset_get_index("名字")` 更安全。）

注意 `o_verren` 虽然注册了，但卡片列表里被显式跳过——他是序章专用：

```gml
if (_objectKey == "o_verren")
    continue;
```

### 15.3 卡片不用改 UI，自动出现

`gml_Object_o_charSelection_Create_0` 直接遍历 `orderArray` 建卡，长度还 **+1**（末尾那张禁用的“自定义”占位卡）：

```gml
var _orderArray = ds_map_find_value(global.classesMap, "orderArray");
var _orderArrayLength = array_length(_orderArray) + 1;
for (var _i = 0; _i < _orderArrayLength; _i++)
{
    ...
    _objectKey = _orderArray[_i];
```

所以**只要注册成功，界面就自动多一张卡**，位置在原版角色之后、占位卡之前（已实机验证）。点卡时执行的是：

```gml
global.player_class = classObject;   // gml_Object_o_charSelectionCard_Other_10
```

### 15.4 角色对象本体：一个只有 Create 事件的空壳

原版 5 个角色对象（`o_reaver` / `o_revenger` / `o_knight_maiden` / `o_runaway_wizzard` / `o_woodward`）**全部只有 1 个 Create 事件**（`o_knight_maiden` 多一个 PreCreate）。实测对象属性：

| 属性 | 值 |
|---|---|
| sprite | `s_char_select`（38×40 占位图） |
| parent | `o_player` |
| visible / awake | `true` |
| persistent | `false` |
| depth | `0` |

Create 事件里只做三件事：设外观精灵、发初始装备、首次进游戏时发钱和杂物。以 Jorgrim 为例：

```gml
event_inherited();

with (o_inventory)
{
    if (!is_start_equipment)
    {
        scr_atr_set_simple("Head", "s_JogrimHead");
        scr_atr_set_simple("CorpseSprite", sprite_get_name(s_Jorgrim_dead));
        scr_atr_set_simple("BodySprite", sprite_get_name(s_dwarf_male));
        scr_playerSpriteInit();

        if (!global.is_load_game)
        {
            with (scr_equip("Worn Cloak", 1))
                scr_inv_atr_set("Duration", 100);
            ...
            with (scr_inventory_add_item(4774))
                scr_container_add_gold(300);
        }
        else
        {
            scr_load_player();
        }

        with (other.id)
            alarm[11] = 3;
    }

    is_start_equipment = true;
}
```

写自己的版本时照抄结构即可，要点：

- `event_inherited()` 必须是第一句，否则父对象 `o_player` 的 40 个事件初始化全丢；
- 三个精灵变量（`Head` / `CorpseSprite` / `BodySprite`）**必须在 `scr_playerSpriteInit()` 之前设**，这个函数就是靠它们拼精灵的；
- `!global.is_load_game` 分支发初始装备，`else` 分支必须调 `scr_load_player()`（读档时恢复），两个分支都不能漏；
- 原版代码里的 `UnknownEnum.Value_1` 是反编译把枚举名丢了，本意就是 `1`（栏位参数），直接写 `1` 就行；
- `scr_inventory_add_item(4774)` 这类数字是物品/对象索引，**别照抄**，用 UTMT 查名字后换成自己的。

### 15.5 属性不要自己写

角色的 STR / AGL / PRC / Vitality / WIL **不是**角色对象设的，而是玩家实例真正创建那一刻由 `scr_classInit("parameters")` 从 `classesMap` 读出来写进存档的：

```gml
if (sprite_index == s_MainCharacterStandup)
{
    scr_classInit("parameters");
    instance_create_depth(x - 26, y, 0, global.player_class);
```

所以只要注册时五维填对就行，不要在 Create 事件里手动 `scr_atr_set_simple("STR", ...)`，否则读档时会被覆盖、表现不一致。实机验证：注册 11/10/10/11/10，进游戏角色面板显示的正是这一组 —— 说明五维确实走这条路，而不是角色对象自己设的。

### 15.6 文本：改 loader，不改那张巨大的表

名字/职业名/描述来自 `table_text`（45 万字符的单行数组），直接改它代价太大。它在 `gml_Object_o_textLoader_Other_25` 里被拆成几个 map：

```gml
scr_tableWriteMap(global.char_name, _array, "char_name");
scr_tableWriteMap(global.class_name, _array, "class_name");
scr_tableWriteMap(global.race_name, _array, "race_name");
scr_tableWriteMap(global.country_name, _array, "country_name");
scr_tableWriteMap(global.char_desc, _array, "char_desc");
```

**在这几句后面补 `ds_map_set` 就行**。注意 `scr_tableWriteMap` 只取当前语言那一列（`arg1[_i][global.language]`），所以 map 里存的是**单一语言的字符串**，我们注入时也要自己按语言分支：

```gml
// 插入位置：本段自己的 scr_tableWriteMap 之后；通用规则见 §16.5，实测锚点见 §15.10.2
var _mc_name = "My Hero";            // 默认英文
var _mc_class = "Sellsword";
var _mc_desc = "A stranger with a borrowed blade.";

switch (global.language)
{
    case 1:
        _mc_name = "Мой Герой";
        _mc_class = "Наёмник";
        _mc_desc = "Чужак с чужим клинком.";
        break;
    case 3:
        _mc_name = "我的角色";
        _mc_class = "佣兵";
        _mc_desc = "带着一把借来的剑的陌生人。";
        break;
}

ds_map_set(global.char_name, "MyHero", _mc_name);      // 键 = nameKey
ds_map_set(global.class_name, "Sellsword", _mc_class); // 键 = classKey
ds_map_set(global.char_desc, "MyHero", _mc_desc);      // 键 = nameKey
```

- 语言列号 `global.language` 与 `ModLanguage` 的对应见 §16.5（`case 1` = 俄语、`case 3` = 中文），映射只在那里维护一份；
- 键千万别搞混：`char_name` / `char_desc` 用 **nameKey**，`class_name` 用 **classKey**；
- 不写 `race_name` / `sex_name` / `country_name` 也可以，直接用原版已有的 `"Male"` / `"Human"` / `"Aldor"`；
- **`"Aldor"` 这种 key 大小写敏感**，图标精灵实际叫 `s_country_Aldor`（大写 A），写错就是空图标。

### 15.7 精灵：命名契约 + 必须补原点

`gml_GlobalScript_scr_playerSpriteInit` 是按名字拼出来的：

```gml
global.playerSpriteBody = sprite_get_index(_bodySpriteName);
global.playerSpriteBodyMask = sprite_get_index(_bodySpriteName + "_mask");
global.playerSpriteHeadNormalArray = [sprite_get_index(_headSpriteName + "_normal"), sprite_get_index(_headSpriteName + "_helmet_normal")];
global.playerSpriteHeadBloodArray  = [sprite_get_index(_headSpriteName + "_blood"),  sprite_get_index(_headSpriteName + "_helmet_blood")];
```

所以 `Head` 填 **不带后缀** 的名字。实测规格（原版值，照抄尺寸就行）：

| 精灵 | 尺寸 | 帧 | 原点 | 必需 |
|---|---|---|---|---|
| `s_<角色名>`（卡片头像） | 37×50 | 1 | 0,0 | ✅ |
| `s_<角色名>Head_normal` | 31×35 | 4 | **15,36** | ✅ |
| `s_<角色名>Head_helmet_normal` | 31×35 | 4 | **15,36** | ✅ |
| `s_<角色名>Head_blood` | 31×35 | 4 | **15,36** | ✅ |
| `s_<角色名>Head_helmet_blood` | 31×35 | 4 | **15,36** | ✅ |
| `s_<角色名>_dead`（尸体） | 40×40 | 1 | **20,20** | ✅ |
| 身体（复用 `s_human_male` / `s_dwarf_male` / `s_human_female`，**须与头部底稿同种族**） | 48×40 + `_mask` | 3 | 22,34 | ⚠️ 重着色角色须显式设 |
| `s_char_<武器>_<角色名>`（专属持握变体） | 48×40 | 2 | — | ❌ 可省，有回退 |

**最大的坑在原点**：贴图注入器新建精灵时原点固定写 `(0,0)`、margin 写满整张图：

```csharp
UndertaleSprite newSprite = Msl.CreateSpriteNoCollisionMasks(
    spriteName,
    new MarginData(0, node.Height - 1, 0, node.Width - 1),
    new OriginData(0, 0),
```

而合成玩家外观时就是靠原点定位的（`draw_sprite_ext(sprite, index, x, y, ...)`），原点不对头就会飞出去。**注入完成后必须在 `PatchMod()` 里手工改回来**：

```csharp
public override void PatchMod()
{
    FixOrigin("s_myherohead_normal", 15, 36);
    FixOrigin("s_myherohead_helmet_normal", 15, 36);
    FixOrigin("s_myherohead_blood", 15, 36);
    FixOrigin("s_myherohead_helmet_blood", 15, 36);
    FixOrigin("s_myhero_dead", 20, 20);
}

private static void FixOrigin(string name, int x, int y)
{
    UndertaleSprite sprite = Msl.GetSprite(name);
    sprite.OriginX = x;
    sprite.OriginY = y;
}
```

> `PatchMod()` 执行时贴图已经注入完了，所以这里能直接 `GetSprite`。卡片头像原版就是 `(0,0)`，不用改。
> 原版头部精灵名有个拼写错误——是 `s_JogrimHead`（少一个 r），抄模板时按实际名字取。

**已实机验证**：不加这段修正头部会错位，加上之后头部与身体拼接正常。别把这条当成“可选优化”。

#### 15.7.1 用原版精灵重绘（换色 / 换底稿）的额外坑

如果不手绘像素，而是像 Caligo 那样——把原版某个角色的头部 / 肖像 / 尸体放进一个“材料类表”里逐像素重新上色，再当成自己的精灵注入——除了 §15.7 的命名与原点，还有一组**静默失败**的坑。管线大致是：

```
导出原版 sprites → 逐像素查表（源 RGB → 材质类字母 → 输出 RGB）→ 写 PNG → 注入 data.win
```

| # | 现象 | 原因 | 做法 |
|---|---|---|---|
| 1 | 换底稿后整张脸或若干色块**保持原版颜色**，且**不报任何错** | 类表的 key 是**源精灵的精确 RGB**（写成 `"r,g,b"`）；换底稿（如 Dirwin→Jorgrim）后源调色板整体变了，旧键全部 miss，而 miss 时像素**原样保留** | 换底稿后必须重新导出新底稿调色板，**逐值重写**类表 key（导出脚本见下） |
| 2 | 头和身体肤色对不上，出现“补丁色块 / 半张脸分色” | 头是重着色过的，身体却挂了**异种族**或**没重着色**的原版精灵（重绘矮人头却用 `s_human_male`） | `BodySprite` 必须与头部底稿**同种族**、并同样处理：矮人→`s_dwarf_male`，人→`s_human_male` |
| 3 | 边缘残留一圈原色“脏边” | 源图有抗锯齿 / 半透明过渡像素，其 RGB 不在类表里 | 补最近色兜底，或把这些过渡色也写进表；纯像素风底稿一般没有这问题 |
| 4 | 改了脚本、重打后进游戏还是旧的 | 在**已打过补丁**的 `data.win` 上重打，被幂等判断跳过 | 永远在干净的 `original.win` 上重打（§10.2 #14） |
| 5 | `s_JogrimHead` 找不到 | 原版拼写少一个 r（同 §15.7 注） | 按实际名取 |

**换底稿检查清单**（漏一项就会静默出错）：

- [ ] 头部四个变体目录：`_normal` / `_helmet_normal` / `_blood` / `_helmet_blood`
- [ ] 肖像目录 `s_<Base>\s_<Base>_0.png`、尸体目录 `s_<Base>_dead\...`
- [ ] 身体精灵 + `_mask`（与头部**同种族**）
- [ ] 三个类表（head / portrait / corpse）的 key 全部按新底稿重写
- [ ] 重跑整链并**读回 `data.win`** 核对尺寸 / 帧 / 原点 / `BodySprite` 名——编译与打补丁成功 **≠** 内容正确

**导出某张 PNG 的去重颜色**（类表 key 直接照抄输出）：

```powershell
# dump-sprite-palette.ps1 —— 脚本保持纯 ASCII（见 §10.2 #26）
param([string]$Path)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Path)
$seen = @{}
for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $c = $bmp.GetPixel($x, $y)
        if ($c.A -eq 0) { continue }
        $seen["$($c.R),$($c.G),$($c.B)"] = 1
    }
}
$bmp.Dispose()
$seen.Keys | Sort-Object
```

> 输出里的 `r,g,b` 直接就是类表的 key；重着色**只改输入端（源 key）**，输出端调色板（材质类字母 → 输出 RGB）保持你角色自己的设定。

### 15.8 完整注入清单

工程名假设为 `New_Character`，角色 `nameKey` 为 `MyHero`：

| # | 目标 | 手段 |
|---|---|---|
| 1 | 新建 `o_myhero` | `Msl.AddObject("o_myhero", spriteName:"s_char_select", parentName:"o_player", isVisible:true, isPersistent:false, isAwake:true)` |
| 2 | 角色 Create 事件 | `Msl.AddNewEvent("o_myhero", ModFiles.GetCode("gml/o_myhero_Create.gml"), EventType.Create, 0)` |
| 3 | 注册进 `classesMap` | `LoadGML("gml_Object_o_dataLoader_Other_10")` → 在 `global.player_class = -4` **之前**插 `scr_classCreate(...)` |
| 4 | 三条文本 | `LoadGML("gml_Object_o_textLoader_Other_25")` → 在 `scr_tableWriteMap(global.char_name, _array, "char_name");` **之后**插 `ds_map_set(...)` |
| 5 | 精灵原点 | 见 §15.7，`PatchMod()` 里 `GetSprite` 后改 `OriginX/OriginY` |
| 6 | 封面 | 工程根目录 `icon.png` |

C# 侧骨架：

```csharp
public override void PatchMod()
{
    // 1) 空壳角色对象
    Msl.AddObject("o_myhero", spriteName: "s_char_select", parentName: "o_player",
                  isVisible: true, isPersistent: false, isAwake: true);
    Msl.AddNewEvent("o_myhero", ModFiles.GetCode("gml/o_myhero_Create.gml"), EventType.Create, 0);

    // 2) 注册（必须在 global.player_class = -4 之前；这两个锚点已实测可用，见 §15.10.2）
    Msl.LoadGML("gml_Object_o_dataLoader_Other_10")
       .MatchFrom("global.player_class = -4")
       .InsertAbove(ModFiles.GetCode("gml/register_class.gml"))
       .Save();

    // 3) 文本
    Msl.LoadGML("gml_Object_o_textLoader_Other_25")
       .MatchFrom("scr_tableWriteMap(global.char_name, _array, \"char_name\")")
       .InsertBelow(ModFiles.GetCode("gml/register_text.gml"))
       .Save();

    // 4) 精灵原点
    FixOrigin("s_myherohead_normal", 15, 36);
    ...
}
```

`gml/register_class.gml`：

```gml
scr_classCreate(
    o_myhero,
    s_myhero,
    "MyHero", "Male", "Human", "Aldor", "Sellsword",
    11, 10, 10, 11, 10,
    [global.swords_tier1, global.athletics_tier1, global.armor_tier1, global.combat_tier1],
    []
);
```

直接写资产名就行，**不用** `asset_get_index`：MSL 的 GML 编译器在资产表里查名字，而 `Msl.AddObject` 已经先把 `o_myhero` 加进去了（资产数量变了会触发重建，见 `GmlCompiler.AssetsChanged`）。实测编译结果就是索引：`scr_classCreate(9665, 17248, "MyHero", ...)`。`asset_get_index("o_myhero")` 写法同样能用，只是多余。

`perksArray` 传 `[]` 就是“开局没有 perk”；想给就传原版 perk 对象名，如 `[o_xxx]`。

### 15.9 已知缺陷与坑（新增角色特有）

| # | 现象 | 原因 | 做法 |
|---|---|---|---|
| 1 | 锚点找不到 / 插入位置跳进 `if` 与 `{` 之间 | MSL 自带的旧版反编译器输出与 UTMT 0.9.2 不一致：MSL 出**资产名**、UTMT 出**索引**，且 MSL 版本**不带分号**；`MatchFrom` 是整行比对，多一个 `;` 就失配 | 用 §15.10.1 的探针拿真实文本；本节两个锚点已实测（§15.10.2） |
| 2 | 头飞出去 / 头位置错 | 新建精灵原点恒为 `(0,0)` | §15.7 手工改 `OriginX/OriginY` |
| 3 | 选人界面根本不出现新卡 | 注册代码没跑到 / `orderArray` 没追加 | 确认插入点在 `classesMap` 初始化之后、且 `scr_classCreate` 没抛错 |
| 4 | 卡上名字显示成 `name[MyHero]` | 文本 map 里没这个键 | §15.6 补 `ds_map_set`，键用 nameKey |
| 5 | 卡上国家图标空白 | `countryKey` 写错大小写 | 只有 `Aldor` / `Skadia` / `Fjall` / `Jacinth` / `Yohara` / `Nistra`，对应精灵 `s_country_<Key>` |
| 6 | 五维跟自己填的不一样 | 在 Create 里手写了 `STR` | 删掉，交给 `scr_classInit("parameters")` |
| 7 | 新角色没有专属台词/剧情反应 | 台词按 name/race/sex 过滤，新 nameKey 只命中通用分支 | 内容层限制；要专属台词得改 `table_char_lines` |
| 8 | 删掉 mod 后读档崩 | 存档里存的是 `playerClass` 对象名，对象不存在 | 分发说明必须写清：卸载前先备份存档 |
| 9 | 进游戏直接是序章，看不到选人界面 | 序章未通关 | 通关一次，或手动把 `account.ini` 的 `Prologue/isComplete` 设成 `true` |
| 10 | 和别的 mod 冲突 | 都要改 `o_dataLoader` / `o_textLoader` 同一个代码条目 | 锚点尽量短且唯一；不要 `MatchAll().ReplaceBy` 整段替换 |
| 11 | 重着色角色部分色块还是原色、或整张脸没变 | 换底稿后类表的源 key 没跟着新底稿重写，查表 miss 且静默保留原色 | 重新导出底稿调色板、逐值重写 key（§15.7.1） |
| 12 | 头身肤色割裂、出现补丁色块 | `BodySprite` 与头部底稿不同种族，或没跟着一起重着色 | 身体必须同种族并一起处理（§15.7.1 #2） |

### 15.10 验证

#### 15.10.1 第一步：探针拿真实反编译文本

锚点必须对着 **MSL 自己的反编译器输出** 写，不能照抄 UTMT 的结果。用一个只做导出的临时 mod 就能拿到：

```csharp
// 探针 mod 的 PatchMod()：把目标代码条目按 MSL 视角 dump 出来
public override void PatchMod()
{
    string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msl_probe");
    System.IO.Directory.CreateDirectory(dir);

    System.IO.File.WriteAllLines(
        System.IO.Path.Combine(dir, "dataLoader.txt"),
        Msl.LoadGML("gml_Object_o_dataLoader_Other_10").ienumerable);

    System.IO.File.WriteAllLines(
        System.IO.Path.Combine(dir, "textLoader.txt"),
        Msl.LoadGML("gml_Object_o_textLoader_Other_25").ienumerable);
}
```

`FileEnumerable.ienumerable` 是公开字段，直接写盘即可。

**实测差异（重要）**：MSL 反编译出来的是**资产名**，UTMT 0.9.2 反编译出来是**索引**。同一段注册代码，两种工具的输出长这样：

```gml
// MSL 反编译（gml_Object_o_dataLoader_Other_10，共 320 行）
scr_classCreate(o_reaver, s_Jorgrim, "Jorgrim", "Male", "Dwarf", "Fjall", "Reaver", 11, 10, 11, 11, 10, [...], [6729])

// UTMT 0.9.2 反编译同一行
scr_classCreate(6979, 6779, "Jorgrim", "Male", "Dwarf", "Fjall", "Reaver", 11, 10, 11, 11, 10, [...], [6729]);
```

差别不止资产名：MSL 版本**不带分号**，这是写锚点时最容易踩空的地方（`MatchFrom` 是按行全文比对，多一个 `;` 就找不到）。

#### 15.10.2 实测通过的两个锚点

| 代码条目 | 锚点原文（照抄，无分号） | 插入方式 | 实测位置 |
|---|---|---|---|
| `gml_Object_o_dataLoader_Other_10` | `global.player_class = -4` | `InsertAbove` | 原第 67 行；插入后新行落在第 65 行，紧跟 9 条原版 `scr_classCreate` 之后 |
| `gml_Object_o_textLoader_Other_25` | `scr_tableWriteMap(global.char_name, _array, "char_name")` | `InsertBelow` | 原第 63 行；插入块落在第 64 行起，仍在 `case 0:` 块内 |

两条都验证过 **插入位置正确**（不是插进 `if` 与 `{` 之间），补丁后节选：

```gml
// dataLoader：新行紧跟原版 9 条 scr_classCreate，位于 global.player_class = -4 之前
scr_classCreate(9665, 17248, "MyHero", "Male", "Human", "Aldor", "Sellsword", 11, 10, 10, 11, 10, [...], []);
global.player_class = -4;
```

```gml
// textLoader：插入块落在 scr_tableWriteMap(global.char_name, ...) 之后
var _mc_name = "My Hero";
```

#### 15.10.3 第二步：整链路

```powershell
ModShardLauncher.CLI.exe compile --source "%LOCALAPPDATA%\ModShardLauncher\ModSources\New_Character"
ModShardLauncher.CLI.exe patch --data "<游戏目录>\original.win" --output ".\out.win" --enable New_Character
```

`--enable` 写的是包里的 `Mod.Name`（不是去掉 `-vX.Y.Z.0` 的文件名），`Mods` 里同名程序集只能留一份——两处的细节见 §10.2 #27、#11。

本次实测耗时：编译 ~2 s，打补丁 8.6 s（首次，反编译缓存冷），再次打补丁 3.4 s（缓存热）。

#### 15.10.4 实测结果（v0.13.2.0 + `original.win`）

打完后用 UTMT 读回 `out.win` 校验，逐项对照：

| 检查项 | 结果 |
|---|---|
| 对象 `o_myhero` | ✅ 存在，`sprite=s_char_select`、`parent=o_player`、`visible=true`、`persistent=false`、`awake=true`，与原版角色对象完全一致 |
| `classesMap` 注册 | ✅ 生成了 `scr_classCreate(9665, 17248, "MyHero", ...)`，`9665` = `o_myhero`、`17248` = `s_myhero`，资产名解析正确 |
| 卡片头像 `s_myhero` | ✅ 37×50，1 帧，origin `0,0` |
| 头部 4 张 | ✅ 均 31×35、4 帧、origin 修正为 `15,36`（不修就是 `0,0`） |
| 尸体 `s_myhero_dead` | ✅ 40×40，1 帧，origin `20,20` |
| 文本注入 | ✅ `char_name` / `class_name` / `char_desc` 三处 `ds_map_set` 就位，中文与俄文字符串往返无损 |
| 角色 Create 事件 | ✅ `gml_Object_o_myhero_Create_0` 已生成（41 行），`Head` / `CorpseSprite` 引用正确 |
| GML 编译 | ✅ 打补丁阶段无 `GML Compile Error`，说明注入的语法与资产引用都合法 |
| 对象总数 | 9696 → 9697 |

**实机部分**（2026-11-03，用 `out.win` 进游戏）：5 项全部通过（§15.10.5），同时验证了 §15.3 自动出卡、§15.5 五维来源、§15.7 原点修正三处推断。

#### 15.10.5 实机确认清单（全部通过）

| # | 检查项 | 结果 |
|---|---|---|
| 1 | 选人界面出现新卡，位置在原版 5 个之后、末尾占位卡之前 | ✅ |
| 2 | 卡上头像、名字（`My Hero`）、职业名（`Sellsword`）、五维（11/10/10/11/10）、技能树正确 | ✅ |
| 3 | 选它开局，进第一个场景后人能正常站立、走动，**头的位置正确** | ✅ |
| 4 | 背包里有 `Worn Cloak` / `Peasant Shoes` / `Homemade Blade` 和 300 金币 | ✅ |
| 5 | 存档 → 回主菜单 → 读档，不崩，外观与装备都在 | ✅ |

也就是说 §15.1–§15.8 给出的做法是**可直接照抄的**，不用再自己试。剩下的工作是**美术替换**（重着色 / 换底稿有自己的静默坑，见 §15.7.1）与**内容差异化**（§15.9 #7）。

---

## 16. 附录 D：技能、状态与技能树

§7 讲武器、§14 讲音效菜单、§15 讲角色；这一节是**技能 / buff / 技能树 / 变身**四件套，全部来自实际做一整套自定义角色的过程，坑都标出来了。

### 16.1 主动技能

一个能用的主动技能 = **3 个对象 + 1 行数值表 + 2 条文本**：

| 组件 | 要求 |
|---|---|
| 技能本体 `o_skill_xxx` | parent **必须是 `o_skill`**；sprite 填图标本身 |
| 技能图标 `o_skill_xxx_ico` | parent **必须是 `o_skill_ico`**；sprite 同图标 |
| 效果 buff `o_b_xxx` | parent `o_buff_maneuver`（或其它 `o_buff_*`），见 §16.2 |
| 数值表行 | `gml_GlobalScript_table_skills_stats` 追加一行 |
| 名称/描述 | `ds_map_set(global.skill_name_text / global.skill_desc, "技能名", ...)` |

技能本体的 Create 只有三行：

```gml
event_inherited();
skill = "Dimming_Decree";                    // 数值表的行键
ds_list_add(attribute, ds_map_find_value(global.attribute, "STR"));
scr_skill_atr(0);                            // 从数值表读名字/描述/消耗/冷却
```

图标对象的 Create：

```gml
event_inherited();
child_skill = asset_get_index("o_skill_dimming_decree");
event_perform_object(child_skill, ev_create, 0);   // 把技能的字段搬到自己身上
```

#### 16.1.1 技能图标必须是 2 帧，第 2 帧是「禁用灰阶」

技能图标固定 **2 帧**：第 1 帧正常态，**第 2 帧是技能不可用（冷却中 / 精力不足）时显示的禁用态**。游戏只是切换 `image_index`，不会替你调色，所以第 2 帧要自己做成**去饱和的纯灰阶**。

- 原版对照：`s_skills_inceneration_1.png` 是纯灰阶，每个像素 `R = G = B`（白色仍是 255）。
- **不要只压暗**（如 `× 0.55`）：那只是"暗的彩色图"，玩家看不出技能不能放；必须去饱和。
- 生成方式（按亮度去饱和，`0.299R + 0.587G + 0.114B`）：

```powershell
$lum = [int](0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B)
$bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $lum, $lum, $lum))
```

**数值表列序**（`scr_create_skill_map` 按下标取值，不是按表头名）：

| 下标 | 含义 | 下标 | 含义 |
|---|---|---|---|
| 0 | 技能名（行键） | 14 | Bonus_Range |
| 1 | **Object**：效果 buff 的对象名 | 15 | Starcast |
| 2 | Target：`No Target` / `Point` / `Single` … | 16 | Branch |
| 3 | Range | 17 | is_knockback |
| 4 | **KD（冷却回合）** | 18 | Crime |
| 5 | **MP（能量消耗）** | 19 | Metacategory |
| 6 | is_moving_zone | 20 | FMB |
| 7 | **Duration（持续回合）** | 21 | AP |
| 8/9 | AOE 长/宽 | 22~25 | Attack / Stance / Charge / Maneuver |
| 10 | is_movement | 26 | Spell |
| 11 | Pattern | 27 | Tags |
| 12 | Validators（如 `AVOID_TILEMARKS`） | | |
| 13 | Class（`skill` / `spell`） | | |

**最小可行做法**：复制一行现成的 `No Target` 技能（如 `Brace_For_Impact`），只改 0/1/4/5/7 五列，列数保持与原行一致：

```csharp
string[] cells = SkillRowTemplate.Split(';');
cells[0] = skill.Name;      // 技能名
cells[1] = skill.Buff;      // buff 对象名
cells[4] = skill.Kd;        // 冷却
cells[5] = skill.Mp;        // 消耗
cells[7] = skill.Dur;       // 持续回合
table.Add(string.Join(";", cells));
```

`Target = No Target` **且** `Duration > 0` 时，技能施放会自动在施法者身上创建 `Object` 列指向的 buff，不需要写施法逻辑 —— 这就是"自施型技能"最省事的实现（§16.2）。

**开局给技能**（可选）：在 `gml_Object_o_skill_fast_panel_Alarm_0` 里插一行，槽位 0..9：

```gml
if (instance_exists(o_caligo))
    scr_create_skill_on_start(o_skill_dimming_decree_ico, 7);
```

### 16.2 状态（Buff）

buff 的骨架（`o_b_xxx`）：Create 初始化 → **Alarm_2 施加效果** → Destroy 收尾。

```gml
// Create
event_inherited();
scr_buff_atr();          // 按 object_get_name 取 buff_name_text / buff_desc_text
stack = 1;
first_turn = true;
max_duration = 4;
buff_snd = 170;
```

```gml
// Alarm_2：创建后一帧触发，也是「读档后重新施加」的唯一入口
event_inherited();

with (target)
{
    __dsDebuggerMapClear(other.data);

    ds_map_add(other.data, "STR", 3);
    ds_map_add(other.data, "Weapon_Damage", 25);
    ds_map_add(other.data, "Damage_Received", -20);

    if (!other.is_load)
        scr_guiAnimation_ext(x, y, 16217);
}
```

要点：

- `data` 是**属性增减表**，`scr_atr_calc` 通过 `scr_buff_param("键")` 把它叠进角色数值；
- `is_load` 用来区分"施放"与"读档恢复"，一次性表现（音效/动画/特效）必须放在 `if (!other.is_load)` 里，否则读档会重放；
- 读档恢复依赖 Alarm_2，所以**任何施加逻辑都必须写在 Alarm_2，不能只写在技能里**；
- 需要撤回的效果（换外观、挂子对象）写进 **Destroy** 事件，并且 `event_inherited()` 放第一行。

`data` 能写哪些键、各自含义见 §17.2 的全量清单，这里不重复。注意 `Pure_Damage` 只用于角色面板显示，**不会**参与伤害计算——想要"纯粹伤害"得挂伤害函数（§16.4）。

buff 文本键规则：`scr_buff_atr()` 默认 `arg0 = object_get_name(object_index)`，所以 **`buff_name_text` / `buff_desc_text` 的键是完整对象名**，例如 `o_b_pallid_sovereign`。

#### 16.2.1 buff 层数显示（图标角上的层数点）

图标上的"层数点"读的是 **`stage`**，不是 `stack`——只设 `stack` 不会显示。照着「搏斗 → 攻击战术」(`o_b_tactics_off`) 写：

```gml
// Create
event_inherited();
scr_buff_atr();
stage = 1;              // 当前层数（从 1 起）
max_stage = 6;          // 上限；非 *stage* 基类默认只有 4
have_stages = true;     // 打开层数绘制
image_speed = 0;        // 层数 buff 一般不播动画
max_duration = 9;
```

- `c_abstract_states_Draw_0` 在 `have_stages` 为真时，用 **`stage_sprite`** 画第 `stage - 1` 帧（`stage_sprite` 由基类 `c_abstract_states_Create_0` 设好：中文 6228 / 其他语言 7075），位置在图标右上角。
- 改层数：`stage = min(max_stage, stage + 1);`，然后 `with (buff) event_perform(ev_alarm, 2);` 让属性按新层数重算。
- 属性词条按 `other.stage` 相乘，例如 `ds_map_add(other.data, "CRT", 3 * other.stage);`。
- 想让存档合并 / `scr_effect_update` 也认这个层数，把 `stack` 同步成 `stage` 即可（`stack = stage;`）。

#### 16.2.2 施加到玩家身上的 buff：`target` 是对象索引 5376

`scr_effect_create(buff, duration, target, owner)` **对玩家有特殊处理**：若 `target` 是玩家，内部会把它替换成 **`5376`（`o_player` 的对象索引）**。因此：

- 玩家 buff 的 `target` **不是实例 id，而是对象索引**；`target == 玩家实例` 永远为假。
- 找"玩家身上的某个 buff"要用 `is_player(target)`，别用 `==`：

```gml
with (o_b_vareth_bane)
{
    if (is_player(target) && is_player(_owner))
        _bane = id;
}
```

- `scr_effect_create` 在 `target.HP < 1` 时直接返回 `-4`（**致死的那一击不会再挂上 debuff**），还会因免疫/抗性返回 `-4`。
- 它会给 `duration` **+1**（除非 `target.buffs_is_change`），且 `max_duration` 是**每个实例从 Create 带出来的**。要精确回合或不同时长，创建后再覆盖：

```gml
var _b = scr_effect_create(o_b_colourbreak, 12, _enemy, _owner);
if (instance_exists(_b))
{
    _b.duration = 12;
    _b.max_duration = 12;   // 同一对象在别处是 5，这里按实例改成 12
}
```

### 16.3 技能树

三层结构，全部硬编码：

```text
左侧分组（3 个，硬编码）→ 分支 o_skill_category 子对象 → 技能格子（分支 skill 数组里的对象）
```

**1) 分组**：`gml_Object_o_skillmenu_Create_0` 里是两个数组字面量，`_metaCategoriesArray` 有 3 个子数组（0=WEAPONRY / 1=UTILITY / 2=SORCERY）。不新建分组就往对应下标 push：

```gml
array_push(_metaCategoriesArray[2], asset_get_index("o_skill_category_vareth_curse"));
```

要新建分组则再往 `_metaCategoriesNamesArray` push 一个名字，注意两处 push 都要插在数组字面量**之后、长度计算之前**。

**2) 分支**：`o_skill_category` 的子对象，两个事件：

```gml
// Create
event_inherited();
text = "Vareths_Curse";                                    // 分组内的分支名键
skill = [asset_get_index("o_skill_dimming_decree_ico"), ...]; // 格子里的节点
branch_sprite = 230;                                        // 分支插图（可复用原版）
KD = 0;
category = "utility";
alarm[3] = 10;                                             // 见下面的解锁时机
```

```gml
// Other_24：格子坐标 + 连线（坐标照抄原版分支最省事）
var _p1 = new ctr_SkillPoint(connectionsRender, asset_get_index("o_skill_xxx_ico"), 31, 55);
var _l1 = new ctr_SkillLine(connectionsRender, 7277, 31, 70);

// 不要写 _p4.addConnectedPoints([_p1])：MSL 编译器不支持这种调用形式。
// 也不能写 with (_p4) addConnectedPoints([_p1])：那样是按全局函数名查找，运行期报
// "Unable to find function addConnectedPoints"。
// addConnectedPoints / addConnectedLines 是 ctr_SkillConnection 上的 static 方法，
// 作用就是 array_push(pointsArray / linesArray, ...)，直接给成员赋值即可：
_p4.pointsArray = [_p1];
_p4.linesArray = [_l1];
_l1.pointsArray = [_p1];
```

原版格子尺寸：**列间距 50（x = 31/81/131）、行间距 73（y = 55/128/201）**，取自 `o_skill_category_basic_armor`（着甲）。竖线用 sprite `7277`；斜线/横线还有 `14447 / 7376 / 412 / 15487 / 12207 / 16372 / 5713`。

> `obj.method(...)` 这种写法 MSL 的 GML 编译器**不支持**（报 `Expected assignment operator`），必须写成 `with (obj) method(...)`（§10.2 #22）。

**3) tier 数组**：决定"这个分支属于谁"，在 `scr_skill_tier_init()` 之后赋值：

```gml
// 结构：[0] = 分支的 text 键，后面是节点对象
global.vareth_curse_tier1 = ["Vareths_Curse", asset_get_index("o_skill_dimming_decree_ico"), ...];
```

然后把它放进角色注册的 `tiersArray`，或在游戏开始时显式解锁：

```gml
scr_skill_open_from_array1d(global.vareth_curse_tier1);   // 只把 is_lock 置 false，不动花费
scr_tier_open("Vareths_Curse");                          // 直接把整条分支设为已开启
```

**4) 被动技能**：原版的 `o_pass_skill_*` **没有独立的 ico**——格子里的实例本身就是被动本体，判定方式是

```gml
if (instance_exists(o_pass_skill_asceticism) && o_pass_skill_asceticism.is_open)
```

所以自建被动只要：parent = `o_skill_passive`、sprite = 图标、Create 里建 `data` 写加成、直接放进分支的 `skill` 数组。`o_skill_ico_Other_10` 里的 `object_is_ancestor(object_index, o_skill_passive)` 会自动把它们排除在快捷栏之外。

被动 Create 的写法（注意 `scr_skill_atr` 的参数）：

```gml
event_inherited();
scr_skill_atr("Ash_Edge");          // ← 必须是技能名字符串
data = __dsDebuggerMapCreate();
ds_map_add(data, "Weapon_Damage", 10);
attributes_names_to_open = ["STR"];
attributes_value_to_open = 0;
level_to_open = 1;
```

> `scr_skill_atr` 内部对两种对象走完全不同的分支：**非被动**用 `skill` 变量查数值表并填 `name` / `desc`；**被动**只做 `name = skill_name_text[argument0]`、`desc = skill_desc[argument0]`。所以被动传 `0` 会让提示框里显示成 `0`（§10.2 #29）。

**5) 解锁时机（坑）**：格子实例由分支基类的 `Alarm_0` **延后 2 帧**创建，所以在游戏开局调 `scr_skill_open_from_array1d` 时它们还不存在，`with (_obj)` 是空操作——表现就是节点一直显示「需要阅读相应文献才能解锁」。补一个自己的定时器：

```gml
// 分支 Create
alarm[3] = 10;

// 分支 Alarm_3
if (!variable_global_exists("vareth_curse_tier1")) exit;
scr_skill_open_from_array1d(global.vareth_curse_tier1);
```

### 16.4 变身与伤害钩子

**换外观**：不要写 `characterDataMap`（会进存档），直接改 `scr_playerSpriteInit` 用的那几个全局：

```gml
global.playerSpriteBody = sprite_get_index("s_caligo_pallid_body");
global.playerSpriteBodyMask = sprite_get_index("s_caligo_pallid_body_mask");
global.playerSpriteHeadNormalArray = [sprite_get_index("..._normal"), sprite_get_index("..._helmet_normal")];
global.playerSpriteHeadBloodArray = [sprite_get_index("..._blood"), sprite_get_index("..._helmet_blood")];
```

`scr_playerSpritePartsUpdate` 每帧比对这几个全局，变了就重建合成图，所以**不需要手动刷新**。恢复时改回原值即可；读档场景由 buff 的 Alarm_2 重新施加。

**世界失色**：游戏自带饱和度后处理，`o_shader_start` 的 Draw 里有

```gml
shader_set_uniform_f(uSaturationAmount, 1 + saturationRain + saturationFocus + saturationVignette);
```

用 `ReplaceBy` 整行替换，把自己的偏移加进去（0 = 正常，-1 = 全灰）：

```gml
global.caligo_dim = lerp(global.caligo_dim, _target, 0.06);
shader_set_uniform_f(uSaturationAmount, 1 + saturationRain + saturationFocus + saturationVignette + global.caligo_dim);
```

只影响**世界层**，UI 不跟着变灰。

**附加伤害**：能改的入口有限，实测结果如下：

| 代码条目 | 能否重新编译 | 说明 |
|---|---|---|
| `scr_attack_result_hit` | ✅ | 近战命中，`_damage` 算完后 `+=` |
| `scr_simple_damage` | ✅ | 技能/法术扣血的最终出口，`argument1 +=` |
| `scr_damage` | ✅ | 另一种伤害应用入口 |
| `scr_cast_spell` | ✅ | 施法总入口 |
| `scr_damage_calculation` | ❌ | 反编译文本无法回编译 |
| `scr_skill_damage` | ❌ | 同上 |
| `scr_physical_damage` | ❌ | 同上 |

```gml
// scr_attack_result_hit 里，紧跟 max(scr_damage_calculation(...)) 那行之后
if (variable_global_exists("caligo_form") && global.caligo_form && is_player(id))
    _damage += 6;
```

```gml
// scr_simple_damage 里，紧跟 var _id = id 之后
if (variable_global_exists("caligo_form") && global.caligo_form)
{
    var _who = id;
    if (variable_instance_exists(id, "owner") && instance_exists(owner) && is_player(owner))
        _who = owner;
    if (is_player(_who))
        argument1 += 6;
}
```

> 两个钩子不要同时命中同一段伤害，否则会翻倍：近战走 `scr_attack_result_hit`，技能/法术走 `scr_simple_damage`，互不重叠。

**扩散特效**：直接用图元画椭圆环，不依赖任何精灵，也不占贴图块（避免 §10.2 #23 的纹理计数问题）：

```gml
gpu_set_blendmode(bm_add);
draw_primitive_begin(pr_linestrip);
for (var _i = 0; _i <= 48; _i++)
{
    var _ang = _i * 7.5;
    draw_vertex_colour(x + lengthdir_x(_r, _ang), y + (lengthdir_y(_r, _ang) * 0.5), colour, _a);
}
draw_primitive_end();
gpu_set_blendmode(bm_normal);
```

### 16.5 文本表：键规则

`o_textLoader` 里每个段先 `scr_tableWriteMap(global.xxx, _array, "段名")`，再被各处 `ds_map_find_value_ext` 读取。补文本**不要改那张几十万字符的表**，直接 `ds_map_set`：

| map | 键 | 用途 |
|---|---|---|
| `global.char_name` | nameKey | 角色名 |
| `global.class_name` | **classKey** | 职业名（角色面板大标题，会转大写） |
| `global.char_desc` | **nameKey** | 角色面板正文（自动换行、自动高度，可放长文） |
| `global.skill_name_text` / `global.skill_desc` | 技能名（数值表行键） | 技能 |
| `global.buff_name_text` / `global.buff_desc_text` | **buff 对象名** | buff |
| `global.tier_name` | 分支的 `text` | 技能树分支名 |

**最大的坑**：`scr_tableWriteMap` **会先清空目标 map 再填充**。所以注入位置必须在这张 map 自己的 `scr_tableWriteMap` 调用**之后**——插在前面会被紧接着的清空抹掉，表现为界面上显示 `className[...]` / `classDescription[...]` 这种原始键。

角色三条文本要插在 `scr_tableWriteMap(global.char_desc, ...)` 之后（它晚于 `char_name`、`class_name`），一次覆盖三张表。

`global.language` 就是表列号：`0` 键列、`1` 俄 / `2` 英 / `3` 中 … `11` 日 / `12` 韩，列号 = `ModLanguage` 枚举序号 + 1；`scr_tableWriteMap` 直接拿它当下标。

### 16.6 文本颜色标记（数值高亮）

原生技能 / 增益描述里的数字、加成是带着颜色标记的，语法 `~tag~内容~/~`（`~/~` 收尾），`##` 分段，`#` 起一条列表项：

```
闪躲~lg~+25%~/~（每点灵敏额外~lg~+1%~/~）、行动声响~lg~-30%~/~（每点感知额外再~lg~-1%~/~）
```

这套标记由 `scr_colorText*` 系列解析，配色表在 `scr_colorTextColorsMap`：

| 标签 | 颜色 | 典型用途 |
|---|---|---|
| `~w~` | 白 | **技能**描述里的中性数值（回合 / 范围 / 基础百分比）、关键词 |
| `~sy~` | 浅褐 | **buff** 描述里的中性数值（回合 / 层数 / 几率） |
| `~lg~` | 浅绿 | **有益**效果（即使数值带负号也绿，如"所受伤害-5%"） |
| `~g~` / `~cg~` | 绿 / 亮绿 | 强正面 |
| `~r~` | 暗红 | **有害**效果 |
| `~lr~` / `~ur~` / `~dr~` | 浅红 / 亮红 / 深红 | 不同强度的负面 |
| `~bl~` | 蓝 | 精力 / 法力 |
| `~o~` | 橙 | 伤害类型、灼烧等 |
| `~y~` / `~ly~` | 黄 / 浅黄 | 强调 |
| `~p~` | 紫 | — |
| `~gr~` / `~grm~` / `~grd~` | 灰阶 | 次要信息 |

**颜色按"对玩家有利 / 有害"上，不按正负号**：原生的"所受伤害`~lg~-5%~/~"是绿的（减伤是好事），而"失手几率`~r~+33%~/~"是红的（对玩家是坏事）。

> 技能描述的中性数值用 `~w~`、buff 描述用 `~sy~`——这是原版 `table_skills` 与 `table_effects` 的既定差异，照对应表抄即可。

**坑**：

- 描述写成纯文本（`"闪避 +25%"`）在游戏里就是白字，**不会自动高亮**；必须自己包成 `~lg~+25%~/~`。
- 中文原生数字**不加空格**（写"向~w~4~/~格"，不是"向 4 格"）。
- 标签只对走 `scr_colorText*` 渲染的文本生效；`global.skill_desc` / `global.buff_desc_text` 都走这条管线（表里原生条目本身就带标签），可放心用。
- `/*...*/` 是本地化占位符（如 `/*Chance*/`、`/*Fire_Damage*/`），运行期替换成对应语言的词；自写描述直接写汉字即可，不用占位符。
- 英文 / 俄文同样适用这套标签（与语言无关），换行也用 `##`。

### 16.7 击杀触发：死亡检测、视野查询与传播

"目标死亡时……"这类被动效果没有现成的可重编译死亡钩子，推荐用**你自己的 Step 观察者**盯住被标记的目标：

> 这个 build 里 buff 的 `event_user` / Step **每个实例只触发一次**，所以按帧轮询的逻辑必须放在自建对象的 Step 里（参考 `o_colourbreak_watch`），不能指望 buff 事件。

```gml
// 目标死亡：实例已消失，或 HP 归零
var _dead = !instance_exists(target_unit) || target_unit.HP <= 0;
```

**敌人与存活**：敌人是 `o_enemy` 的后代，`with (o_enemy)` 可遍历房间里所有敌人；存活判断用 `HP > 0`。

**视野内的敌人**：

```gml
with (o_enemy)
{
    if (HP > 0
        && scr_tile_distance(_owner, id) <= _owner.VSN   // 视野半径（VSN 属性）
        && is_visible(id))                               // 雾 / 视线遮挡
    {
        // ...
    }
}
```

- `scr_tile_distance(a, b)` 返回切比雪夫距离 `max(|dx|, |dy|)`。
- `is_visible(unit)` 走玩家雾图；没有雾时恒为真。
- `VSN` 是单位的视野属性（`scr_atr_calc` 里 `clamp(..., 0, 15)`）。

**传播 debuff + 每目标一个观察者**（防止重复触发导致伤害 / 层数翻倍）：

```gml
var _spread = scr_effect_create(o_b_colourbreak, 12, _enemy, _owner);

if (instance_exists(_spread))
{
    _spread.duration = 12;
    _spread.max_duration = 12;
}

var _watched = false;

with (o_colourbreak_watch)
{
    if (target_unit == _enemy)
        _watched = true;
}

if (!_watched)
{
    var _w = instance_create_depth(0, 0, 0, o_colourbreak_watch);
    _w.target_unit = _enemy;
    _w.owner_unit = _owner;
    _w.last_hp = _enemy.HP;
}
```

**坑**：

- 造成击杀的那一下本身**不会**再挂 debuff（`scr_effect_create` 见 `HP < 1` 返回 `-4`）；要"死时身上有标记"，标记必须在这之前就挂上。
- 传播后**继续击杀继续传播**：给每个被传播的目标都建观察者，它们的死亡会再走同一条路径。
- 同一目标只留一个观察者；否则多份观察者会对同一次伤害 / 同一次死亡重复结算。

## 17. 附录 E：五维属性与状态词条

做角色、技能、buff、装备时最常查的两样东西：**五维属性**（谁影响谁）和**状态词条**（buff 的 `data` 表能写哪些键）。下表逐项对照 `table_attributes`、`scr_atr_calc` / `scr_atr_calc_combat` 核对而来，**键名大小写敏感**。

### 17.1 五维属性

| 键 | 英文 | 中文 | 在哪儿被用（实测） |
|---|---|---|---|
| `STR` | Strength | 力量 | 近战伤害 `4 + 0.33×STR`；兵器伤害源 `85 + 兵器 + 1.5×STR` |
| `AGL` | Agility | 敏捷 | 反击几率 `-14 + 1.5×AGL`；失手 `35 - 1.5×AGL`；失误几率 `35 - 1.5×AGL`；开锁 `5 + AGL` |
| `PRC` | Perception | 感知 | 准度 `65 + 1.5×PRC`；护甲穿透 `-15 + 1.5×PRC`；法咒准度/穿透 `+1.5×PRC` |
| `Vitality` | Vitality | 活力 | 生命/恢复类公式的主体，如被动回血 `0.5×Vitality`、`desperate` 加成 `(Vitality/2)×…` |
| `WIL` | Willpower | 意志 | 法术强度：15/20/25/30 每档 `+7.5%`；法力相关 |

四个攻击/防御属性还各有 **15 / 20 / 25 / 30 的档位加成**（`_bonusSTR`、`_bonusAGL`…），写技能数值时可以按这个节奏做成长曲线。

**命名坑**：敏捷是 `AGL`（不是 `AGI`），感知是 `PRC`（不是 `PER`），体质是 `Vitality`（不是 `VIT`）；同时 **`PRR` 是格挡几率**，和 `PRC` 只差一个字母。

**在哪儿读**

| 位置 | 内容 | 读法 |
|---|---|---|
| `gml_GlobalScript_table_attributes` | 键 + 13 语显示名 + 描述（486 行） | 列号 = `global.language` |
| `global.attribute` / `attribute_desc` / `attribute_trade_hover` | 上表按当前语言切好的 map | `ds_map_find_value(global.attribute, "AGL")` |
| 单位实例变量（`scr_atr_calc()` 每回合重算） | **最终值**（已含 buff，`clamp(2, 30)`） | `with (unit) { AGL }` |
| `global.characterDataMap` | 角色档案：`LVL` / `XP` / `SP` / `AP` / `Morale` / `Sanity` … | `scr_atr("LVL")` |
| `bSTR` / `bAGL` / `bPRC` / `bVIT` / `bWIL` | **基础值**（不含 buff） | `with (unit) { bAGL }` |

**技能声明「受哪些属性影响」**（角色面板会据此标注）：

```gml
// o_skill_exiles_veil_Create.gml
ds_list_add(attribute,
    ds_map_find_value(global.attribute, "AGL"),
    ds_map_find_value(global.attribute, "PRC"),
    ds_map_find_value(global.attribute, "WIL"));
```

**在 buff 里按属性点做线性加成**（属性值从目标单位取，不要从 `global` 猜）：

```gml
// o_b_exiles_veil_Alarm_2.gml
var _agl = 0;
with (target) { _agl = AGL; }              // 目标单位的最终敏捷

with (target)
{
    __dsDebuggerMapClear(other.data);      // 先清空，避免读档/重放叠加
    ds_map_add(other.data, "EVS", 25 + _agl);          // 基础 25% + 每点敏捷 1%
    ds_map_add(other.data, "Noise_Produced", -30);     // 负数 = 降低
}
```

### 17.2 状态词条（buff `data` 的键）

写法约定：

- 数值一律按**百分比**，`+` 增益、`-` 减益；“越低越好”的键（`Noise_Produced`、`Miscast_Chance`）用负数表示下降。
- 必须写在 buff 的 **`Alarm_2`**：创建后 1 帧执行一次，读档时 `loadSelfBuffs` 会重放它。只写 `Create` 的加成读档后必丢。
- 消费链：`buff.data` → `scr_buff_param("KEY")` → `scr_atr_calc()` 得到单位派生属性 → 战斗/判定公式。

**五维本身也能被词条临时改变**：`STR` / `AGL` / `PRC` / `Vitality` / `WIL` 都是合法键（最终值 `clamp(2, 30)`）。

| 分类 | 键 | 中文 |
|---|---|---|
| 生命 / 精力 | `max_hp`、`max_mp`、`HP_turn`、`MP_turn`、`Health_Restoration`、`MP_Restoration`、`Healing_Received`、`Max_Energy_Threshold` | 上限、每回合回复、治疗效率 |
| 伤害与命中 | `Weapon_Damage`（`_Main`/`_Off`）、`Hit_Chance`、`Armor_Piercing`、`Armor_Damage`、`Bodypart_Damage`、`Bonus_Range`、`range` | 兵器伤害、准度、护甲穿透/破坏、肢体伤害、距离 |
| 暴击 | `CRT`（`_Main`/`_Off`）、`CRTD`（`_Main`/`_Off`）、`Crit_Avoid` | 暴击几率、暴击效果、暴击避免 |
| 闪避 / 格挡 / 反击 / 失手 | `EVS`、`PRR`、`Block_Power`、`Block_Recovery`、`CTA`、`FMB` | 闪躲、格挡几率、格挡力量/恢复、反击、失手 |
| 减伤 | `Damage_Received`、`Damage_Returned`、`DEF`、`Head_DEF`、`Body_DEF`、`Arms_DEF`、`Legs_DEF`、`Fortitude` | 所受伤害、反伤、总/部位防护、坚忍 |
| 抗性 | `Physical_`/`Slashing_`/`Piercing_`/`Blunt_`/`Rending_`/`Fire_`/`Frost_`/`Shock_`/`Caustic_`/`Poison_`/`Arcane_`/`Unholy_`/`Sacred_`/`Psionic_` + `Resistance`；另有 `Magic_Resistance`、`Bleeding_Resistance`、`Stun_Resistance`、`Knockback_Resistance`、`Duration_Resistance` | 各类抗性、控制/位移抗性 |
| 状态几率 | `Bleeding_Chance`、`Daze_Chance`、`Stun_Chance`、`Immob_Chance`、`Stagger_Chance`、`Knockback_Chance` | 出血、击晕、硬直、限制移动、破衡、击退 |
| 法术 | `Magic_Power`、`Spell_Hit_Chance`、`Spell_Armor_Piercing`、`Miscast_Chance`、`Miracle_Chance`、`Miracle_Power`、`Pyromantic_`/`Cryomantic_`/`Electromantic_`/`Geomantic_`/`Psimantic_`/`Astromantic_`/`Venomantic_`/`Arcanistic_Power`、`Arcanistic_Distance` | 法强、法咒准度/穿透、失误、奇观、各系法力 |
| 状态槽 | `Pain_Change`、`Pain_Limit`、`Pain_Resistance`、`Morale_Change`、`Morale_Factor`、`Sanity_Change`、`Fatigue_Change`、`Fatigue_Gain`、`Toxicity_Change`、`Hunger_Resistance`、`Immunity_Change` | 疼痛/斗志/神智/疲劳/迷醉/饥饿/免疫 |
| 经济与杂项 | `Cooldown_Reduction`、`Skills_Energy_Cost`、`Spells_Energy_Cost`、`Abilities_Energy_Cost`、`Swimming_Cost`、`Trade_Favorability`、`Savvy`、`STL`（潜行）、`VSN`/`currentVSN`/`VSN_Bonus`（视野）、`Noise_Produced`（行动声响）、`Received_XP`、`Lifesteal`、`Manasteal`、`Charge_Distance`、`Avoiding_Trap` | 冷却、各类消耗、交易、开锁、潜行、视野、声响、经验、吸生/吸魔 |

装备与物品用的是**同一套键名**，走 `scr_inv_param("KEY", item)`；所以 buff、装备、技能加成的键可以互相参照。

### 17.3 全量清单怎么导出

`table_attributes` 一共 486 行，第 0 列是键，第 1 列俄文、第 2 列英文、第 3 列中文……（列号 = `global.language`）。想自己查/生成对照表：

```powershell
$p = "C:\Users\<you>\Desktop\stoneshard-assets\codes\gml_GlobalScript_table_attributes.gml"
$rows = (Get-Content $p)[2] -split '", "'
foreach ($r in $rows) {
    $c = $r -split ';'
    if ([string]::IsNullOrWhiteSpace($c[0])) { continue }   # 分组注释行
    "{0}`t{1}`t{2}" -f $c[0], $c[2], $c[3]                  # 键 / 英文 / 中文
}
```

### 17.4 踩过的坑

| # | 现象 | 原因 | 做法 |
|---|---|---|---|
| 1 | buff 的时长/层数被游戏改写 | `duration` / `max_duration` / `stack` / `first_turn` / `have_duration` 是 buff 基类变量 | 不要复用同名（曾用 `duration` 存毫秒，被 `scr_effect_create` 覆盖成回合数） |
| 2 | 注入的文本不生效，界面露出原始键 | `scr_tableWriteMap` 先清空目标 map 再填充 | 文本注入排在该 map 自己的 `scr_tableWriteMap` 之后（§16.5） |
| 3 | 读档后属性加成丢失 | 只写在 `Create`，读档由 `Alarm_2` 重放 | 属性/词条加成写在 `Alarm_2` |
| 4 | 闪躲/格挡/反击/失手看串 | 四个缩写相近 | `EVS`=闪躲、`PRR`=格挡、`CTA`=反击、`FMB`=失手 |
| 5 | 技能树连线编译报 `Expected assignment operator` | MSL 的 GML 编译器不支持对 `new` 实例调用方法 | 直接给成员赋值 `obj.pointsArray = [...]`（§16.3） |
| 6 | 图标上不显示层数 | 层数绘制读 `stage`，不是 `stack` | 设 `stage` + `max_stage` + `have_stages = true`（§16.2.1） |
| 7 | 玩家身上的 buff 找不到 / 比较恒为假 | 玩家 buff 的 `target` 是对象索引 5376，不是实例 | 用 `is_player(target)`，别用 `==`（§16.2.2） |
| 8 | 致死一击不挂 debuff | `scr_effect_create` 遇 `HP < 1` 返回 `-4` | 击杀前先挂标记，用自建 Step 观察者触发（§16.7） |
| 9 | 技能不可用时看不出禁用态 | 第 2 帧做成了“压暗的彩色图” | 第 2 帧做成去饱和灰阶（§16.1.1） |
