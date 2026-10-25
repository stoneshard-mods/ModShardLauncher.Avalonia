using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// Opt-in regression test: set <c>MSL_TEST_DATA</c> to a vanilla data.win and verify that patching
/// still applies the base modules (logger) but no longer injects any script-console content.
/// Without the variable the test is a no-op so CI stays fast.
/// </summary>
public class PatchContentTests
{
    [Fact]
    public async Task PatchDoesNotInjectScriptConsole()
    {
        string? path = Environment.GetEnvironmentVariable("MSL_TEST_DATA");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        GameDataService gameData = new();
        await gameData.LoadAsync(path);

        PatchDiagnostic? diagnostic = new PatchRunner().Patch(Array.Empty<ModFile>());
        Assert.Null(diagnostic);

        var data = MslContext.Data;
        Assert.DoesNotContain(data.GameObjects, gameObject => gameObject.Name.Content == "o_ScriptEngine");
        Assert.DoesNotContain(data.Code, code =>
            code.Name?.Content is "SendMsg" or "gml_Script_SendMsg" or "give" or "gml_Script_give"
                or "msl_print" or "gml_Script_msl_print"
                or "createHookObj" or "gml_Script_createHookObj"
                or "gml_Object_o_ScriptEngine_Create_0"
                or "gml_Object_o_ScriptEngine_Step_0"
                or "gml_Object_o_ScriptEngine_Other_68");
        Assert.DoesNotContain(data.Scripts, script =>
            script.Name?.Content is "gml_Script_SendMsg" or "gml_Script_give");

        // The console is gone, but the ordinary base patch (logger) must still be applied.
        Assert.Contains(data.Code, code => code.Name?.Content == "scr_msl_log");
        Assert.Contains(data.GameObjects, gameObject => gameObject.Name.Content == "o_msl_log");
    }
}
