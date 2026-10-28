# 架构说明

本文记录重构后的工程结构、职责边界、关键流程，以及不可破坏的兼容性约定。

## 1. 目标与约束

| 目标 | 约束 |
|---|---|
| 从 WPF 迁移到 Avalonia，不保留 WPF 代码 | 已发布的 `.sml` 必须继续可加载，modder 的源码工程必须继续可编译 |
| 高内聚、低耦合 | `ModShardLauncher` 程序集不得引用 Avalonia / WPF / WinForms / System.Drawing |
| 现代 IDE 风格界面 | 保持像素级数据（GML、贴图、表格）行为不变 |
| 增加无头 CLI | 领域逻辑必须可在无 UI 环境下运行 |

## 2. 工程与依赖方向

```
┌──────────────────────────────┐   ┌──────────────────────────────┐
│ ModShardLauncher.GUI         │   │ ModShardLauncher.CLI         │
│ Avalonia 12 + MVVM           │   │ 控制台（无 UI）               │
└──────────────┬───────────────┘   └──────────────┬───────────────┘
               └───────────────┬──────────────────┘
                               ▼
              ┌────────────────────────────────────┐
              │ ModShardLauncher (Core)            │
              │ AssemblyName = ModShardLauncher    │
              │ Modding API + Runtime              │
              └────────────────┬───────────────────┘
                               ▼
              ┌────────────────────────────────────┐
              │ UndertaleModLib 0.6.1（冻结二进制） │
              │ UndertaleModTool 0.6.1（冻结二进制）│
              └────────────────────────────────────┘
```

依赖规则：

1. **只允许单向依赖**：GUI/CLI → Core → UTMT 二进制。
2. Core 不引用任何 UI 包；View 只通过 ViewModel 调用 Core。
3. 跨层通信使用接口/事件/`IProgress`，Core 不弹窗、不读写控件、不 `Dispatcher.Invoke`。
4. 第三方 mod 代码只能看到 Core（程序集 `ModShardLauncher`），看不到 GUI/CLI。

## 3. Core 分层

```
src/ModShardLauncher.Core/
├─ Modding/                  # 对 modder 公开的 API（兼容面）
│  ├─ Mod.cs                 # Mod 基类、ModLanguage
│  ├─ ModFile.cs             # ModFile / FileChunk / ModSource / PatchStatus
│  ├─ MslContext.cs          # 静态 API 的环境状态（替代 ModLoader.Data / Main.Instance）
│  ├─ Code/                  # GML/ASM 读写（CodeInjection）与匹配管线（CodeMatching）
│  ├─ Assembly/              # 汇编注入 + AssemblyWrapper 工厂
│  ├─ Events/ Objects/ Rooms/ Textures/ Weapons/ Loot/ Menus/ Tables/ Hooks/ Variables/
│  ├─ Logging/               # 注入游戏内日志模块
│  └─ Randomness/            # 确定性 PRNG（RandomUtils，位于 CodeLoading.cs）
└─ Runtime/                  # 宿主侧编排（不对 modder 承诺稳定）
   ├─ GameDataService.cs     # data.win 的加载/保存（原 DataLoader）
   ├─ ModCatalog.cs          # Mods/ModSources 扫描与 .sml 装载（原 ModLoader 一半）
   ├─ PatchRunner.cs         # 打补丁编排（原 ModLoader 另一半）
   ├─ SmlPackageReader.cs    # .sml 容器解析（原 FileReader）
   ├─ ModCompiler.cs         # 运行时 Roslyn 编译（原 UtilsPacker）
   ├─ Textures/              # SkiaSharp 图集、碰撞掩码、注入
   ├─ Configuration/         # UserSettings + SettingsStore
   ├─ Scripting/             # 本地 Socket 脚本引擎（无 UI）
   ├─ GameDataExporter.cs    # 原 Exporter
   ├─ DataChecksumValidator.cs
   ├─ PatchDiagnostic.cs
   └─ AppPaths.cs / AppInfo.cs / GmlResources.cs
```

### 为什么 API 与 Runtime 仍在一个程序集

当前 111 个公开类型全部位于 `ModShardLauncher` 程序集中，`.sml` 内的程序集按简单名绑定该程序集。把公开类型拆到新程序集会让历史模组出现 `TypeLoadException`，除非逐类型添加 `[assembly: TypeForwardedTo]`。因此第一阶段只做“去 UI 化”，保持单程序集；后续如要拆分，必须用类型转发渐进迁移。

## 4. 核心服务

| 服务 | 职责 | 关键约束 |
|---|---|---|
| `GameDataService` | 持有 `UndertaleData`，加载/保存、YYC 校验、失败回滚 | 失败时 `Reset()`；保存使用临时文件 + 原子替换 |
| `ModCatalog` | 扫描 `Mods/*.sml`、`ModSources/*.csproj`，实例化 `Mod` | 保留 `Enabled` 状态；单个包解析失败只记录 |
| `PatchRunner` | 依次执行日志注入 → 各模组 `PatchMod()`（含 `ModWeapons` 写入）→ 掉落脚本注入 | 保持原顺序；异常转换为 `PatchDiagnostic` |
| `TextureInjector` | PNG → 图集 → EmbeddedTexture/PageItem/Sprite/碰撞掩码 | 使用 SkiaSharp，算法与 GDI+ 版逐位对齐 |
| `GameTableCodec` + `TableDraftStore` + `TableInstructionWriter` | 读写 `gml_GlobalScript_table_*`；写操作按批合并，flush 时直接重建脚本的数组指令（`push.s`/`conv` 对 + `@@NewGMLArray@@` 参数个数 + 开头分支偏移），形状不符才回退 GML 编译 | 每个模组结束时、管线结束/保存前、以及读到该脚本前各 flush 一次；`table:<name>` 计时保留在 flush 处；输出与 GML 编译路径逐字节一致 |
| `GmlCompiler` | 复用 GML 编译器的资产 name→id 字典（`CompileContext` 其余状态每次新建） | 资产数量变化或 `Msl.SetObject` 改名时重建；替换 `UndertaleCode.ReplaceGML` 以减少每次编译 5.7 ms 的字典构建 |
| `StringPool` / `CodeIndex` / `SpriteIndex` | 字符串池、代码、精灵的 O(1) 名称查找（替代库里的线性 `MakeString`/`ByName`/`First`） | 只增量同步尾部新增项；换数据文件时整体清空 |
| `ModCompiler` | 调用 `ModShardPackerReference` 编译 `.sml` | 依赖 `ModShardLauncher.dll` 的磁盘路径 |
| `MslContext` | 静态 API 的当前数据、菜单/署名/回调等补丁期状态 | 由宿主在补丁前后 Attach/Detach |
| `SettingsStore` | `Settings.json` 读写 | 字段名与旧版一致；文件位于用户数据目录 |
| `AppPaths` | 用户数据目录布局 | 默认 `%LOCALAPPDATA%\ModShardLauncher`，CLI 可用 `--data-dir` 覆盖；首次启动迁移旧便携布局 |

## 5. 关键流程

### 5.1 打开数据文件

```
MainWindowViewModel.OpenDataCommand
  → DialogService.PickOpenDataFileAsync()            (StorageProvider)
  → GameDataService.LoadAsync(path)
      → Task.Run: UndertaleIO.Read → checksum 警告 → YYC 校验
      → MslContext.Attach(data)
      → GameTableCodec.Initialize()                  (weapons / equipment 表)
      → LootTableRegistry.ResetLootTables()
      → data.Export(DataType.Items)                  (保留旧行为)
  → ModCatalog.Reload() + ApplyEnabledMods(settings)
  → 更新状态栏/统计
```

### 5.2 打补丁并保存

```
PatchAndSaveCommand
  → 未加载数据 → 提示 LoadDataWarning
  → PatchRunner.Patch(mods)
      → GameLogInjection.InjectLog()
      → foreach enabled mod: TextureInjector.Inject → mod.PatchMod() → ModWeapons 写入（Msl.AddWeapon）
                                → TableDraftStore.FlushAll()（本模组排队的表写回只编译一次）
      → AddDisclaimerRoom / ChainDisclaimerRooms / CreateMenu
      → TableDraftStore.FlushAll()（管线兜底）
      → LootTableRegistry.InjectLootScripts()
  → 成功：StorageProvider 选择保存路径 → GameDataService.SaveAsync
                  → LootTableRegistry.SaveLootTables(目录)
  → 失败：ErrorDialog(Summary/Details)
  → 重新加载原 data.win 并刷新列表（与旧版一致）
```

### 5.3 编译模组

```
ModSourcesView → ModSourceItemViewModel.CompileCommand
  → ModCompiler.Compile(sourceDir)
      → FilePacker.Pack(null, sourceDir, Mods/, sourceDir, AppInfo.MslVersion,
                         [typeof(Mod), typeof(UndertaleCode)])
  → 成功后重新扫描 Mods/
```

### 5.4 GUI 与 CLI 两个可执行文件

- `ModShardLauncher.GUI.exe`（WinExe）→ 启动 Avalonia，不产生控制台窗口；
- `ModShardLauncher.CLI.exe`（Exe）→ `CliApplication` 解析并执行 `patch`/`list`/`compile`/`export`，退出码 0/1/2/3，日志同时写入控制台与 `%LOCALAPPDATA%\ModShardLauncher\logs\cli_*.txt`。

两者都只依赖 `ModShardLauncher`（Core），发布时由 `scripts/publish.ps1` 输出到同一目录。
## 6. 状态与线程

- `UndertaleData` 只在补丁线程/加载线程上访问；UI 线程只读取展示字段。
- 加载、保存、编译、打补丁全部 `async/await` + `Task.Run`；UI 通过 `IProgress`/状态属性更新。
- `MslContext` 是进程级单例（历史 API 决定的），由 `GameDataService` 在加载成功后 `Attach`，失败时 `Detach`。
- 应用退出时：保存设置 → `Log.CloseAndFlushAsync()` → 关闭主窗口。

## 7. 兼容性约定（不可破坏）

| # | 约定 | 说明 |
|---|---|---|
| 1 | Core 程序集名 `ModShardLauncher` | `.sml` 内程序集的 AssemblyRef |
| 2 | 命名空间 `ModShardLauncher` / `ModShardLauncher.Mods` | mod 源码 `using` |
| 3 | 公开方法签名不变 | 包括 `Msl` 约 194 个方法、111 个公开类型 |
| 4 | `UndertaleModLib` 版本冻结 0.6.1 | 公开签名大量使用其类型 |
| 5 | `.sml` 二进制格式不变 | 魔数 `MSLM`、版本串、4 段 chunk、程序集 |
| 6 | 运行时 Roslyn 能定位 `ModShardLauncher.dll` | 见发布要求（多文件） |
| 7 | `Settings.json` 字段名不变 | `Language` / `EnableLogger` / `EnabledMods` |
| 8 | 内嵌 GML 资源名不变 | `o_msl_*` / `scr_msl_*` |
| 9 | 控制台兼容占位不可删 | 脚本控制台已移除（不再注入任何控制台 GML）；`ModInterfaceEngine` / `EngineObject` / `ModDelegate` 保留并标 `[Obsolete]`，`Msl.HookFunction` 保留签名但抛 `NotSupportedException`，`ScriptCallbacks` 字段保留。`LegacyCompatibilityTests` 锁定这些成员 |

### 7.1 旧模组二进制兼容层

已发布的 `.sml` 内部是编译好的程序集，直接按名字引用宿主。加载器为此保留了三层兼容：

| 兼容点 | 实现 |
|---|---|
| 程序集身份 | Core 的 `AssemblyVersion` 固定为 `1.0.0.0`（旧 WPF 版只设置 `FileVersion`，程序集版本默认就是 1.0.0.0） |
| 依赖解析 | `ModAssemblyLoadContext.LoadMod(byte[])`：任何 `ModShardLauncher` 请求都绑定到已加载的宿主程序集；其余依赖从默认上下文或应用目录解析 |
| 加载上下文隔离 | 每个 `.sml` 的**每次构建**使用一个独立上下文（`CreateForPackage`）：同一个包重新编译后程序集标识不变，而运行时拒绝把同一标识二次装入同一上下文（`FileLoadException: Assembly with same name is already loaded`）。未变化的包直接复用已加载的程序集，每次刷新仍创建新的 `Mod` 实例 |
| 静态字段 | `DataLoader.data`、`ModLoader.Weapons/WeaponDescriptions/ScriptCallbacks` 保持为 `public static` **字段**（改成属性会破坏已编译的 `ldsfld`），并与 `MslContext` 共享同一实例 |
| 静态方法 | `ModLoader.GetTable/SetTable/Initalize/AddMenu`、`DataLoader.Reset/LogUTMT*` 转发到新实现；仅宿主可执行的入口（`LoadFile`/`SaveFile`）抛出明确异常 |
| WPF 依赖 | 移除对 `UndertaleModTool.LayerZIndexConverter` 的调用（它实现 `IMultiValueConverter`，类型加载需要 `PresentationFramework`）；改用 `UndertaleModLib` 的 `UpdateZIndex` |

回归测试：`LegacyCompatibilityTests` 锁定程序集身份、字段/方法存在性与 `MslContext` 别名关系；`ModAssemblyLoadingTests` 用 Roslyn 现场编译一个 `Mod` 子类，经 `ModAssemblyLoadContext` 加载并检查基类身份。
## 8. 与 WPF 版本的行为差异

| 方面 | WPF | Avalonia |
|---|---|---|
| 语言切换 | 三个 `ResourceDictionary` 同时 merge，靠移除/追加排序 | 单一 resx + satellite，`LocalizedText` 通知刷新；默认按 Windows 显示语言自动选择 |
| 加载/保存提示 | 独立模态 `LoadingDialog` + `ShowDialog()` | 主窗口内进度覆盖层 + `async`，补丁在后台线程执行以便日志实时刷新 |
| 日志 | 隐藏控制台窗口 | 底部固定高度日志面板（不可调整）+ 文件日志；信息级日志始终记录 |
| 错误对话框 | WinForms `Form` + RTF | Avalonia `ErrorDialog` + 可选中文本 |
| 文件选择 | `Microsoft.Win32` 同步对话框 | `StorageProvider` 异步 |
| 图片 | `System.Drawing` 图集 + `GetPixel` | SkiaSharp 图集 + 像素读取 |
| 布局 | 960×800 固定画布 + Viewbox | 自适应窗口 + 网格布局 |
| 主题 | 像素风图片资源 | 深色 Fluent + 自绘强调色（不再使用精灵图） |
| 导航 | 手工 `ContentControl` 换页 | MVVM 页面 + DataTemplates |
| 模组列表 | 自定义 `ItemsControl` + code-behind | `ItemsControl` + ItemViewModel |

## 9. 文件职责映射（旧 → 新）

| 旧文件 | 新文件 |
|---|---|
| `App.xaml` / `Main.xaml` / `Controls/*` | `GUI/Views/*` + `GUI/ViewModels/*` |
| `Main.xaml.cs`（外壳/设置） | `MainWindowViewModel`、`AppServices`、`SettingsViewModel` |
| `DataLoader.cs` | `Runtime/GameDataService.cs` |
| `ModLoader.cs` | `Runtime/ModCatalog.cs` + `Runtime/PatchRunner.cs` |
| `FileReader.cs` | `Runtime/SmlPackageReader.cs` + `Modding/ModFile.cs` |
| `TextureLoader.cs` | `Runtime/Textures/TextureSourceScanner.cs`、`TextureAtlasBuilder.cs`、`SpriteCollisionMask.cs`、`TextureInjector.cs` |
| `FilePacker.cs` | `Runtime/ModCompiler.cs` |
| `MSLDiagnostic.cs` | `Runtime/PatchDiagnostic.cs` |
| `Core/UI/ErrorMessageDialog.cs` | `GUI/Views/ErrorDialog.axaml` |
| `CodeResources.cs` | `Runtime/GmlResources.cs` |
| `Loader/Exporter.cs` | `Runtime/GameDataExporter.cs` |
| `Loader/ChecksumChecker.cs` | `Runtime/DataChecksumValidator.cs` |
| `Mods/ModInterfaceEngine.cs` | `Runtime/Compatibility/ModInterfaceEngine.cs`（仅兼容占位） |
| `ModUtils/CodeUtils.cs` | `Modding/Code/CodeInjection.cs` + `CodeMatching.cs` |
| `ModUtils/GeneralUtils.cs` | `Modding/Code/CodeLoading.cs`（含 `RandomUtils`） |
| `ModUtils/AsmUtils.cs` | `Modding/Assembly/AssemblyInjection.cs` |
| `ModUtils/RoomUtils.cs` | `Modding/Rooms/MslRooms.cs` |
| `ModUtils/ModMenuUtils.cs` | `Modding/Menus/ModMenus.cs` |
| `ModUtils/TextureUtils.cs` | `Modding/Textures/MslTextures.cs` |
| `ModUtils/EventUtils.cs` | `Modding/Events/MslEvents.cs` |
| `ModUtils/ObjectUtils.cs` | `Modding/Objects/MslObjects.cs` |
| `ModUtils/Weapon.cs` / `WeaponUtils.cs` | `Modding/Weapons/Weapon.cs` / `MslWeapons.cs` |
| `ModUtils/LootUtils.cs` | `Modding/Loot/LootTables.cs` |
| `ModUtils/LogUtils.cs` | `Modding/Logging/GameLogInjection.cs` |
| `ModUtils/HookUtils.cs` | `Modding/Hooks/MslHooks.cs` |
| `ModUtils/VariableUtils.cs` | `Modding/Variables/MslVariables.cs` |
| `ModUtils/TableUtils/*` | `Modding/Tables/*`（每个游戏表一个文件） |
| `ModUtils/TableUtils/TableUtils.cs` | `Modding/Tables/EnumMemberValues.cs` |
| `ModLoader.GetTable/SetTable` | `Modding/Tables/GameTableCodec.cs` |

## 10. 扩展点

- **新增游戏表**：在 `Modding/Tables/` 下按现有模式添加 `XxxTable.cs`，包含枚举与 `Msl.InjectTableXxx`。
- **新增本地化字符串**：在三个 `Strings.*.resx` 中添加同名 key，XAML 用 `{loc:Tr Key}`。
- **新增 CLI 命令**：在 `src/ModShardLauncher.CLI/CliCommands.cs` 添加方法，并在 `CliApplication.cs` 的 switch 中注册。
- **新增页面**：新建 `XxxViewModel` + `XxxView`，在 `App.axaml` 的 `DataTemplates` 注册，并把导航项加入 `MainWindowViewModel.NavigationItems`。
- **替换/升级 UTMT 库**：只允许在 `Core.csproj` 的 `<Reference>` 中改版本，并跑一遍完整回归；`Msl` 的公开签名依赖其类型。

## 11. 后续工作

1. 为图集布局、补丁管线补集成测试（`.sml` 读取与本地化已有回归测试）。
2. 视需要把 `MslTextures`/`MslRooms`/`LootTables` 等大文件继续按类型拆分。
3. 评估让模组程序集的加载上下文可回收（`isCollectible` + `Unload`）：目前每个包每次构建的上下文只能随进程退出释放，重新编译很多次会累积。
4. 增加 Windows 之外的构建矩阵（CLI 模式与测试可在 Linux 运行）。
