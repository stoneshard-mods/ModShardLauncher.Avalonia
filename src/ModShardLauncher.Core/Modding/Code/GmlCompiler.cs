using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Compiles GML and caches the compiler's asset name→id table across compiles.
/// <para>
/// <see cref="UndertaleCode.ReplaceGML"/> builds a new <see cref="CompileContext"/> for every call,
/// and its setup rebuilds the asset dictionary (objects, sprites, sounds, rooms, scripts, extension
/// functions, …): 5.7 ms per compile on the Stoneshard data file, for dozens of compiles per patch
/// run. That dictionary only changes when the set of assets changes, so it is created once here and
/// passed to otherwise fresh contexts.
/// </para>
/// <para>
/// Only the two asset containers are carried over; everything else (local variable bookkeeping,
/// function lists, per-entry flags) is allocated per compile, exactly like <c>ReplaceGML</c> does.
/// Sharing a whole <see cref="CompileContext"/> is not safe: measured against the baseline, the
/// compiler leaves per-entry state behind that changes the generated instructions.
/// </para>
/// <para>
/// The rest of <c>ReplaceGML</c> is reproduced in the same order: compile, replace the instruction
/// list, record the entry in the library's GML cache journal and honour UTMT's profile mode.
/// </para>
/// </summary>
internal static class GmlCompiler
{
    private static readonly object Gate = new();

    private static Dictionary<string, int>? _assetIds;
    private static List<string>? _scripts;
    private static int[]? _assetCounts;
    private static bool _assetsInvalid = true;

    /// <summary>Recompiles <paramref name="code"/> from <paramref name="gml"/>.</summary>
    /// <exception cref="Exception">The GML does not compile (same message as <c>ReplaceGML</c>).</exception>
    internal static void ReplaceGml(UndertaleCode code, string gml)
    {
        if (code is null)
        {
            throw new ArgumentNullException(nameof(code));
        }

        lock (Gate)
        {
            bool redoAssets = AssetsChanged();

            // A fresh context keeps every piece of compiler state other than the asset table clean.
            CompileContext context = new(MslContext.Data, code);
            if (!redoAssets && _assetIds is not null && _scripts is not null)
            {
                // Setup() only rebuilds when the dictionary is empty, so assigning these before the
                // compile is what skips the 5.7 ms MakeAssetDictionary() call.
                context.assetIds = _assetIds;
                context.scripts = _scripts;
            }

            CompileContext result;
            try
            {
                result = Compiler.CompileGMLText(gml, context, redoAssets);
            }
            catch
            {
                ResetCaches();
                throw;
            }

            if (!result.SuccessfulCompile || result.HasError)
            {
                ResetCaches();
                Console.WriteLine(gml);
                throw new Exception("GML Compile Error: " + result.ResultError);
            }

            // Keep the containers this compile ended up with (fresh ones when the assets changed).
            _assetIds = result.assetIds;
            _scripts = result.scripts;

            code.Replace(result.ResultAssembly);
            MslContext.Data.GMLCacheChanged?.Add(code.Name?.Content);
            WriteProfileEntry(code, gml);
        }
    }

    /// <summary>Forces the asset dictionary to be rebuilt before the next compile.</summary>
    internal static void InvalidateAssets()
    {
        lock (Gate)
        {
            _assetsInvalid = true;
        }
    }

    /// <summary>Drops the cached asset table; called when another data file is attached.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            ResetCaches();
        }
    }

    private static void ResetCaches()
    {
        _assetIds = null;
        _scripts = null;
        _assetCounts = null;
        _assetsInvalid = true;
    }

    /// <summary>
    /// Whether the asset dictionary has to be rebuilt. The counts cover every list the library puts
    /// into it (plus extension functions), which is O(1) to compute; a rename in place is the only
    /// change this cannot see, so the APIs that can do that call <see cref="InvalidateAssets"/>.
    /// </summary>
    private static bool AssetsChanged()
    {
        int[] counts = CurrentAssetCounts();
        if (_assetsInvalid || _assetCounts is null || !counts.AsSpan().SequenceEqual(_assetCounts))
        {
            _assetCounts = counts;
            _assetsInvalid = false;
            return true;
        }

        return false;
    }

    private static int[] CurrentAssetCounts()
    {
        UndertaleData data = MslContext.Data;

        int extensionFunctions = 0;
        if (data.Extensions is not null)
        {
            foreach (UndertaleExtension extension in data.Extensions)
            {
                foreach (UndertaleExtensionFile file in extension.Files)
                {
                    extensionFunctions += file.Functions.Count;
                }
            }
        }

        return
        [
            data.GameObjects?.Count ?? 0,
            data.Sprites?.Count ?? 0,
            data.Sounds?.Count ?? 0,
            data.Backgrounds?.Count ?? 0,
            data.Paths?.Count ?? 0,
            data.Fonts?.Count ?? 0,
            data.Timelines?.Count ?? 0,
            data.Scripts?.Count ?? 0,
            data.Shaders?.Count ?? 0,
            data.Rooms?.Count ?? 0,
            data.AudioGroups?.Count ?? 0,
            data.AnimationCurves?.Count ?? 0,
            data.Sequences?.Count ?? 0,
            data.ParticleSystems?.Count ?? 0,
            extensionFunctions,
        ];
    }

    /// <summary>
    /// Mirrors the profile-mode write of <c>UndertaleCode.ReplaceGML</c>: it is a no-op unless the
    /// tool's profile folder is configured, but a mod that turned profile mode on must keep working.
    /// </summary>
    private static void WriteProfileEntry(UndertaleCode code, string gml)
    {
        UndertaleData data = MslContext.Data;
        var toolInfo = data.ToolInfo;
        if (toolInfo is null)
        {
            return;
        }

        try
        {
            string tempPath = Path.Combine(
                toolInfo.AppDataProfiles,
                toolInfo.CurrentMD5,
                "Temp",
                code.Name?.Content + ".gml");

            if (toolInfo.ProfileMode || File.Exists(tempPath))
            {
                File.WriteAllText(tempPath, gml);
            }
        }
        catch (Exception exc)
        {
            throw new Exception("Error during writing of GML code to profile:\n" + exc);
        }
    }
}
