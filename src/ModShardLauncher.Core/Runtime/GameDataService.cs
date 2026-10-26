using Serilog;
using UndertaleModLib;
using UndertaleModLib.Util;

namespace ModShardLauncher;

/// <summary>
/// Owns the currently loaded <c>data.win</c> and the load/save pipeline.
/// <para>
/// This replaces the old <c>DataLoader</c> static class. It deliberately knows nothing about
/// dialogs, dispatchers or the window: callers await <see cref="LoadAsync"/> / <see cref="SaveAsync"/>
/// and decide how to present progress and errors.
/// </para>
/// </summary>
public sealed class GameDataService
{
    private UndertaleData? _data;

    public UndertaleData? Data => _data;

    public string DataPath { get; private set; } = string.Empty;

    public string SavedDataPath { get; private set; } = string.Empty;

    public bool IsLoaded => _data is not null;

    /// <summary>
    /// Opt-in (default off): write the item/weapon/armor CSV manifests after a successful load.
    /// The CLI <c>export</c> command remains available for explicit exports.
    /// </summary>
    public bool ExportManifests { get; set; }

    public void Reset()
    {
        MslContext.Detach();
        _data = null;
        DataPath = string.Empty;
        SavedDataPath = string.Empty;
    }

    /// <summary>Loads a GameMaker data file. Resets the service when loading fails.</summary>
    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using (PatchTimings.Measure("load"))
            {
                await Task.Run(() => LoadCore(path), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            Reset();
            throw;
        }
    }

    /// <summary>Writes the patched data to <paramref name="path"/> using a temp file and an atomic replace.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        using (PatchTimings.Measure("save"))
        {
            await Task.Run(() => SaveCore(path), cancellationToken).ConfigureAwait(false);
            SavedDataPath = path;
        }
    }

    /// <summary>Dumps the loaded data for code exploration (same output as the WPF build).</summary>
    public void Export(DataType type) => MslContext.Data.Export(type);

    private void LoadCore(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
        if (!DataChecksumValidator.CompareChecksum(stream))
        {
            Log.Warning(
                "Checksum inconsistency, {{{0}}} may not be steam vanilla from the modbranch beta branch or is a new version.",
                path);
        }

        UndertaleData data = UndertaleIO.Read(stream, LogUtmtWarning, LogUtmtMessage);
        _data = data;
        DataPath = path;
        MslContext.Attach(data);

        if (data.IsYYC())
        {
            throw new InvalidDataException(string.Format(
                "{{{0}}} was made with YYC (YoYo Compiler) which is unmodable currently. You should move to the beta branch called modbranch instead.",
                path));
        }

        Log.Information("Successfully load: {0}.", path);

        GameTableCodec.Initialize();
        LootTableRegistry.ResetLootTables();

        if (ExportManifests)
        {
            data.Export(DataType.Items);
            Log.Information("Wrote item/weapon/armor manifests to the export folder");
        }
    }

    private void SaveCore(string path)
    {
        // Unique temp file next to the target: a crashed or concurrent run can never clobber the
        // temp file of another save, and the target is only touched by the final swap.
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        List<Exception> exceptions = new();
        bool succeeded = false;

        try
        {
            SaveTo(tempPath);
            succeeded = true;
        }
        catch (Exception saveException)
        {
            exceptions.Add(saveException);
            try
            {
                ClearChunkDictionaries();
            }
            catch (Exception cleanupException)
            {
                exceptions.Add(cleanupException);
            }
        }

        if (succeeded)
        {
            try
            {
                // File.Replace is an atomic swap on the same volume; File.Move is atomic when the
                // destination does not exist yet. Never delete the target first: a crash between
                // delete and move would lose the previous output.
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception moveException)
            {
                exceptions.Add(moveException);
                succeeded = false;
            }
        }

        // Never leave a temp file behind, whichever way the swap ended.
        if (File.Exists(tempPath))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception cleanupException)
            {
                exceptions.Add(cleanupException);
            }
        }

        if (!succeeded)
        {
            throw new AggregateException(exceptions);
        }
    }

    private void SaveTo(string path)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        UndertaleIO.Write(stream, MslContext.Data, LogUtmtMessage);
        QoiConverter.ClearSharedBuffer();
    }

    private static void ClearChunkDictionaries()
    {
        UndertaleData data = MslContext.Data;
        if (UndertaleIO.IsDictionaryCleared)
        {
            return;
        }

        IEnumerable<IUndertaleListChunk> chunks = data.FORM.Chunks.Values
            .Where(x => x is not null)
            .Select(x => (IUndertaleListChunk)x);
        Parallel.ForEach(chunks, chunk => chunk.ClearIndexDict());
        UndertaleIO.IsDictionaryCleared = true;
    }

    private static void LogUtmtWarning(string warning) => Log.Warning("[UTMT WARNING]: {0}", warning);

    private static void LogUtmtMessage(string message)
    {
        // UndertaleModLib is chatty; warnings are routed above, messages stay silent.
    }
}
