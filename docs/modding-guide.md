# 模组制作指南（C# / .sml）

面向想为《石质碎片》（Stoneshard）写 C# 模组的人。本文按**本仓库（Avalonia + .NET 10 重构版）的实际行为**编写，章节结构参考官方文档 [开始](https://modshardteam.github.io/ModShardLauncher/zh/guides/start-modding.html)；与官方文档、以及旧版 WPF 教程不一致的地方，在 [§10](#10-与官方文档的差异与已知缺陷) 集中说明并给出原因。

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
10. [与官方文档的差异与已知缺陷](#10-与官方文档的差异与已知缺陷)
11. [验证与调试](#11-验证与调试)
12. [附录：完整示例](#12-附录完整示例)
13. [API 速查](#13-api-速查)

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

与官方文档最大的差异：旧版是在 `ModShardLauncher.exe` 同级建 `Mods` / `ModSources`，**本版固定在用户数据目录**，升级或重装程序不会动你的模组。首次启动会把旧的便携布局自动迁移过来。

- GUI：设置页「文件夹」里可以直接打开这些目录；
- CLI：`--data-dir <目录>` 可以换一套互不干扰的模组/数据目录；
- `data.win` 不在这里：它是任意位置的原版数据文件，用「打开」对话框选择。

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

左侧「模组源码」→ 找到工程卡片 → 点「编译」。编译**不需要先打开 `data.win`**（这一点与官方文档不同），成功后会自动刷新「模组」列表。

### 6.2 CLI

```powershell
ModShardLauncher.CLI.exe compile --source "%LOCALAPPDATA%\ModShardLauncher\ModSources\MyFirstMod"
```

产物固定落在 `Mods\<文件夹名>.sml`，例如 `Mods\MyFirstMod.sml`。包内的版本串是启动器版本（示例为 `v0.13.2.0`），和启动器版本不同时打补丁只会警告，不会拒绝。

退出码：`0` 成功，`1` 参数错误，`2` 编译/补丁失败，`3` 未处理异常。

### 6.3 打包规则（实测）

| 目录里的内容 | 处理 |
|---|---|
| 任意子目录、任意文件 | 打包（`bin`、`obj`、`.vs` 排除） |
| `.cs` | 不打包；编译成程序集放进包里 |
| `icon.png`（根目录） | 作为卡片封面，**不**进游戏图集 |
| 其它 `.png` | 作为贴图注入游戏（见 §7.6） |
| `.gml` / `.json` | 打进包；运行时用 `Mod.ModFiles.GetCode` / `GetFile` 读取 |

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

- `CloneDefaults` 只复制**数值属性**，`Name`、`ID` 和各语言译名需要自己写（官方文档也是这样说的）；
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

1. 「打开 data.win」选择**原版**文件；
2. 「模组」页启用要用的模组；
3. 「打补丁并保存」（Ctrl+S）。

保存对话框建议的文件名是 `<原文件名>_patched.win`；启动器**不允许覆盖当前已加载的原版文件**，会提示换一个输出路径。打补丁失败不会写坏原文件（先写临时文件再替换）。

### 9.2 CLI

```powershell
# 启用全部模组
ModShardLauncher.CLI.exe patch --data "<原版>\data.win" --output ".\data_patched.win" --all

# 只启用指定模组
ModShardLauncher.CLI.exe patch --data "<原版>\data.win" --output ".\data_patched.win" --enable MyFirstMod,Quicksave
```

`--mods <目录>` 可以指定别的 `Mods` 目录（例如给 CI 用）。启用状态保存在 `Settings.json` 的 `EnabledMods` 里。

### 9.3 装进游戏

先备份游戏目录的 `data.win`，再用打好的文件替换它（或按游戏启动时的提示选择数据文件）。不同版本游戏建议先确认自己用的是 `modbranch` 或与数据表结构匹配的版本。

### 9.4 分发

给玩家的只有 `.sml`：放进 `%LOCALAPPDATA%\ModShardLauncher\Mods` 即可。WPF 时代编译的旧包**不需要重新编译**也能用——Core 的程序集名固定为 `ModShardLauncher`、版本固定 `1.0.0.0`，旧包按名字就能绑定到新宿主。

## 10. 与官方文档的差异与已知缺陷

官方文档写于 WPF 版和旧版本游戏。下面是本仓库的差异，**按“现象 / 原因 / 做法”记**，遇到对不上的地方先看这里。

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
| 1 | 官方文档说 `Mods`/`ModSources` 在 exe 同级 | 本版改到 `%LOCALAPPDATA%\ModShardLauncher` | 见 §3 |
| 2 | 官方文档说必须先打开 `data.win` 才能编译 | 本版编译不依赖游戏数据 | 直接编译 |
| 3 | `Description[ModLanguage.Chinese]` 编译不过 | 这个字段在 `Weapon` 上叫 `WeaponDescriptions` | 用 `WeaponDescriptions[ModLanguage.Chinese]` |
| 4 | 成员上的 `///` 注释导致 `CS1569` | 打包器的 XML 文档写入缺陷（`count ('-3')`），与中英文无关 | 成员注释统一用 `//` |
| 5 | `CS0012: ImmutableList<> 在未引用的程序集中定义` | `Localization.LanguageList` 的类型是 `ImmutableList`，打包引用里没有 `System.Collections.Immutable` | 用 `Enum.GetValues<ModLanguage>()` |
| 6 | 代码里用 `UndertaleData` 报 `CS0012` | 宿主不会把 `UndertaleModLib` 传递给你的工程 | csproj 显式引用 `UndertaleModLib.dll`（§4.3） |
| 7 | 官方文档要求把 `ModShard.dll` 放进游戏目录、用 `give` 命令 | 本版不提供脚本控制台，也不会注入控制台 GML；`give` / `ModShard.dll` 都不可用 | 调试用日志（§11）；取武器走掉落表/商人 |
| 8 | 打补丁时警告 `built with msl ... different from ...` | `.sml` 的构建版本和当前启动器不同 | 只是警告，可以继续；要消掉就重编译 |
| 9 | 打开 `data.win` 报 YYC | 游戏用了 YoYo 编译器，数据不可改 | 换 `modbranch` 或非 YYC 版本 |
| 10 | 用原版 MSL（net6.0）打包时报 `System.Linq, Version=10.0.0.0` 找不到，堆栈停在 `PatchMod()` | mod 源码用了 LINQ（如 `.Any(...)`），打包器按当前 .NET 10 环境引用 `System.Linq`，net6 启动器加载时找不到 | 不依赖 LINQ（改 `foreach`），并在 csproj 关闭 `ImplicitUsings`，只用 netstandard 基础 API |
| 11 | 打补丁时报 `Assembly with same name is already loaded` | `Mods` 目录里有多个程序集名相同的 `.sml`（例如 `Map_TP.sml` 与 `Map_TP-v0.3.0.0.sml`） | 只保留一份 `.sml`，旧包改成 `.sml.bak` 或移出 `Mods` |
| 12 | 同一个 `.sml` 在两个启动器之间表现不一致 | Avalonia 版是 net10，打的包引用 `System.Private.CoreLib 10`；原版 MSL 是 net6 | 用哪个启动器进游戏，就用哪个启动器打包 |

## 11. 验证与调试

### 11.1 日志

- GUI：底部固定高度的日志面板（补丁过程实时刷新），文件在 `%LOCALAPPDATA%\ModShardLauncher\logs`;
- CLI：控制台 + `logs\cli_*.log`；
- 打补丁会给游戏注入日志对象（`o_msl_log` / `scr_msl_log`），GML 模组可以在游戏里调用它，日志写到游戏目录的 `Logs\msl_log_*.txt`。

### 11.2 排查顺序

1. `Mods` 列表里能看到你的包吗？看不到 → 包没生成或解析失败（看 `logs`）；
2. 打补丁时该模组的行是 `Patching succeeded` 还是失败？
3. 表行改对了吗？用 CLI 打补丁后读回校验（见 §11.3）；
4. 进游戏没效果：先确认启用状态、确认游戏用的是新数据文件、确认没有别的模组同时改同一行。

### 11.3 本文的验证方式

本文所有代码用下面这条链路验证过（.NET SDK 10.0.402、启动器 v0.13.2.0、游戏目录的 `data-o.win` 作为原版）：

```powershell
# 1. 编译
ModShardLauncher.CLI.exe compile --source "%LOCALAPPDATA%\ModShardLauncher\ModSources\MyFirstMod"

# 2. 打补丁
ModShardLauncher.CLI.exe patch --data "<游戏目录>\data-o.win" --output ".\out.win" --all

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
| `Msl.LoadGML(codeName)` / `Msl.AddNewEvent(...)` | 改/加 GML 代码与事件 |
| `ModInterfaceEngine` / `Msl.HookFunction(...)` | 脚本控制台已移除；类型仅作兼容占位，`HookFunction` 调用会抛 `NotSupportedException` |
| `Msl.AddMenu(...)` / `Msl.AddCreditDisclaimerRoom(...)` | 模组菜单、署名房间 |
| `Mod.ModFiles.GetFile / GetCode` | 读自己包里打进的其他文件（JSON/GML） |

完整公开 API 见官方 [API 文档](https://modshardteam.github.io/ModShardLauncher/zh/guides/api.html)，以及源码 `src/ModShardLauncher.Core/Modding/`（`Msl` 的公开方法大多带中文注释）。
