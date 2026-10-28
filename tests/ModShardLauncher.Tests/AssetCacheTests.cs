using ModShardLauncher;
using UndertaleModLib.Models;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// Opt-in regression tests for the compiler's cached asset dictionary (see the internal
/// <c>GmlCompiler</c>). Requires <c>MSL_TEST_DATA</c> pointing at a vanilla <c>data.win</c>.
/// <para>
/// The dictionary maps asset names to ids and is only rebuilt when the asset set changes; if that
/// detection is wrong, GML that references a new asset compiles into a variable reference instead of
/// an asset reference and the patch silently produces broken code. The compiler emits an asset
/// reference as <c>push.i &lt;asset id&gt;</c> followed by <c>conv</c>, while an unresolved name becomes
/// <c>push.v &lt;variable&gt;</c>, so the two cases are distinguishable on the instruction list.
/// </para>
/// </summary>
[Collection(GameDataCollection.Name)]
public class AssetCacheTests
{
    private readonly ITestOutputHelper _output;

    public AssetCacheTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AssetAddedBetweenCompiles_IsResolvedAsAnAsset()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        GameDataService service = new();
        await service.LoadAsync(dataPath);
        PatchBaselineSupport.AssertVanilla(dataPath);

        // First compile builds the asset dictionary for the vanilla data.
        Msl.AddCode("var msl_asset_probe_a = 1;", "gml_msl_asset_probe_a");

        // Adding an object changes the asset set, so the next compile has to rebuild the dictionary.
        UndertaleGameObject probe = Msl.AddObject("o_msl_asset_probe");
        int id = MslContext.Data.GameObjects.IndexOf(probe);
        UndertaleCode code = Msl.AddCode(
            "instance_create_depth(0, 0, 0, o_msl_asset_probe);",
            "gml_msl_asset_probe_b");

        Assert.True(PushesId(code, id), $"expected push.i {id} for the object, got:\n{Describe(code)}");
        Assert.False(PushesVariable(code, "o_msl_asset_probe"), "the object was compiled as a variable reference");
    }

    [Fact]
    public async Task ReplacingAnObject_InvalidatesTheDictionary()
    {
        string? dataPath = PatchBaselineSupport.TryGetDataPath();
        if (dataPath is null)
        {
            _output.WriteLine(PatchBaselineSupport.MissingDataMessage);
            return;
        }

        GameDataService service = new();
        await service.LoadAsync(dataPath);
        PatchBaselineSupport.AssertVanilla(dataPath);

        UndertaleGameObject original = Msl.AddObject("o_msl_asset_probe_old");
        // Warm the dictionary so it knows the old name.
        Msl.AddCode("var msl_asset_probe_c = 1;", "gml_msl_asset_probe_c");

        UndertaleGameObject replacement = new() { Name = StringPool.Intern("o_msl_asset_probe_new") };
        int id = MslContext.Data.GameObjects.IndexOf(original);
        Msl.SetObject("o_msl_asset_probe_old", replacement);

        UndertaleCode code = Msl.AddCode(
            "instance_create_depth(0, 0, 0, o_msl_asset_probe_new);",
            "gml_msl_asset_probe_d");

        Assert.True(PushesId(code, id), $"expected push.i {id} for the renamed object, got:\n{Describe(code)}");
        Assert.False(PushesVariable(code, "o_msl_asset_probe_new"), "the renamed object was compiled as a variable reference");
    }

    private static bool PushesId(UndertaleCode code, int id) =>
        code.Instructions.Any(x =>
            x.Kind == UndertaleInstruction.Opcode.PushI
            && x.Value is IConvertible
            && Convert.ToInt32(x.Value, System.Globalization.CultureInfo.InvariantCulture) == id);

    private static bool PushesVariable(UndertaleCode code, string name) =>
        code.Instructions.Any(x =>
            x.Value is UndertaleInstruction.Reference<UndertaleVariable> reference
            && reference.Target?.Name?.Content == name);

    private static string Describe(UndertaleCode code) =>
        string.Join(
            Environment.NewLine,
            code.Instructions.Select(x => $"{x.Kind} {x.Type1} {x.Value?.GetType().Name} {x.Value}"));
}
