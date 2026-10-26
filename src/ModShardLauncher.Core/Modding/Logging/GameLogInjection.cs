using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Newtonsoft.Json;
using System.IO;
using UndertaleModLib.Models;

namespace ModShardLauncher;

public static class GameLogInjection
{
    /// <summary>Object the base patch creates; also its "this file was already patched" marker.</summary>
    internal const string MarkerObject = "o_msl_timer";

    public static void InjectLog()
    {
        // Fail before writing anything. Patching an already patched file used to inject the hook a
        // second time and only then throw on the existing event, leaving a duplicate behind and
        // reporting a confusing error. The GUI reloads the source file before every patch, so this
        // only triggers on a genuinely already-patched input.
        if (MslContext.Data.GameObjects.Any(o => o.Name?.Content == MarkerObject))
        {
            throw new InvalidOperationException(
                $"This data file already contains the '{MarkerObject}' object, so it has been patched before. " +
                "Patch a vanilla data.win instead of an already patched file.");
        }

        UndertaleGameObject timer = Msl.AddObject(MarkerObject, isPersistent: true);
        UndertaleGameObject log = Msl.AddObject("o_msl_log", isPersistent: true);

        Msl.LoadGML(Msl.EventName("o_gameLoader", EventType.Create, 0))
            .MatchAll()
            .InsertBelow("global._msl_log = instance_create_depth(0, 0, -100, o_msl_log);")
            .Save();

        Msl.AddNewEvent(timer, Msl.GetCodeRes("o_msl_timer_Create_0"), EventType.Create, 0);
        Msl.AddNewEvent(timer, Msl.GetCodeRes("o_msl_timer_Step_0"), EventType.Step, 0);
        Msl.AddNewEvent(log, Msl.GetCodeRes("o_msl_log_Create_0"), EventType.Create, 0);

        Msl.AddInnerFunction("scr_msl_log_save");
        Msl.AddInnerFunction("scr_msl_log");
    }
}
