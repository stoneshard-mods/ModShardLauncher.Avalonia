using System.Reflection;
using ModShardLauncher.Mods;
using UndertaleModLib;

namespace ModShardLauncherTest;

/// <summary>
/// Guards the binary contract that already published <c>.sml</c> packages rely on.
/// Breaking any of these members makes old mods fail with TypeLoadException/MissingMethodException.
/// </summary>
public class LegacyCompatibilityTests
{
    [Fact]
    public void ContractAssembly_KeepsLegacyIdentity()
    {
        AssemblyName name = typeof(Mod).Assembly.GetName();
        Assert.Equal("ModShardLauncher", name.Name);
        Assert.Equal(new Version(1, 0, 0, 0), name.Version);
    }

    [Fact]
    public void DataLoader_ExposesLegacyDataField()
    {
        FieldInfo? field = typeof(DataLoader).GetField("data", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(typeof(UndertaleData), field!.FieldType);
        Assert.NotNull(typeof(DataLoader).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void ModLoader_ExposesLegacyFieldsAndTableAccessors()
    {
        Assert.NotNull(typeof(ModLoader).GetField("Weapons", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(ModLoader).GetField("WeaponDescriptions", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(ModLoader).GetField("ScriptCallbacks", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(ModLoader).GetMethod("Initalize", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(ModLoader).GetMethod("GetTable", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null));
        Assert.NotNull(typeof(ModLoader).GetMethod("SetTable", BindingFlags.Public | BindingFlags.Static, null, [typeof(List<string>), typeof(string)], null));
    }

    [Fact]
    public void MslContext_AliasesLegacyStorage()
    {
        Assert.Same(ModLoader.Weapons, MslContext.Weapons);
        Assert.Same(ModLoader.WeaponDescriptions, MslContext.WeaponDescriptions);
        Assert.Same(ModLoader.ScriptCallbacks, MslContext.ScriptCallbacks);
        Assert.Same(ModLoader.Weapons, typeof(ModLoader).GetField("Weapons")!.GetValue(null));
    }

    [Theory]
    [InlineData("LoadGML")]
    [InlineData("MatchFrom")]
    [InlineData("MatchBelow")]
    [InlineData("MatchAll")]
    [InlineData("ReplaceBy")]
    [InlineData("InsertBelow")]
    [InlineData("InsertAbove")]
    [InlineData("Remove")]
    [InlineData("Save")]
    [InlineData("GetObject")]
    [InlineData("GetSprite")]
    [InlineData("AddMenu")]
    public void Msl_KeepsMethodsUsedByPublishedMods(string methodName)
    {
        Assert.Contains(typeof(Msl).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == methodName);
    }

    [Fact]
    public void RemovedConsole_KeepsCompatibilityShims()
    {
        Type engine = typeof(ModInterfaceEngine);
        Assert.NotNull(engine.GetField("Instance", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(engine.GetField("ModPath", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetField("CurrentCallbackID", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetField("IsLoadHooks", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetField("Variables", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetField("HookDelegates", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetField("Callbacks", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(engine.GetMethod("Initalize", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null));
        Assert.NotNull(engine.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(string), typeof(object[])], null));
        Assert.NotNull(engine.GetMethod("DoHook", BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(string), typeof(object[])], null));
        Assert.NotNull(engine.GetMethod("AddHook", BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(string), typeof(Action<object[]>)], null));

        Assert.NotNull(typeof(EngineObject));
        Assert.NotNull(typeof(ModDelegate));

        MethodInfo hook = typeof(Msl).GetMethod("HookFunction", BindingFlags.Public | BindingFlags.Static, null,
            [typeof(string), typeof(string), typeof(string[])], null)!;
        ObsoleteAttribute obsolete = hook.GetCustomAttribute<ObsoleteAttribute>()!;
        Assert.True(obsolete.IsError);

        FieldInfo callbacks = typeof(ModLoader).GetField("ScriptCallbacks", BindingFlags.Public | BindingFlags.Static)!;
        Assert.False(callbacks.IsInitOnly);
        Assert.Same(ModLoader.ScriptCallbacks, MslContext.ScriptCallbacks);
    }

    [Fact]
    public void GameObjectUtils_KeepsApplyEventExtensions()
    {
        Assert.Equal(2, typeof(GameObjectUtils).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Count(m => m.Name == "ApplyEvent"));
    }
}
