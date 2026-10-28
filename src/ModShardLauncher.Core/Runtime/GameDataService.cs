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
    /// <summary>
    /// Above this size the save falls back to streaming straight to disk: buffering such a file in
    /// memory would cost more RAM than it buys in speed on a typical machine.
    /// </summary>
    private const long MaxInMemorySaveBytes = 768L * 1024 * 1024;

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
                await Task.Run(() => LoadCore(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            Reset();
            throw;
        }
    }

    /// <summary>
    /// Writes the patched data to <paramref name="path"/> using a temp file and an atomic replace.
    /// A cancellation that arrives before the swap discards the temp file and leaves the target alone.
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        using (PatchTimings.Measure("save"))
        {
            await Task.Run(() => SaveCore(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            SavedDataPath = path;
        }
    }

    /// <summary>Dumps the loaded data for code exploration (same output as the WPF build).</summary>
    public void Export(DataType type)
    {
        // The export reads the code entries directly, so pending table writes must be applied first.
        TableDraftStore.FlushAll();
        MslContext.Data.Export(type);
    }

    private void LoadCore(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read);

        // One checksum per load: it validates the file and identifies it for the optional caches.
        string checksum = DataChecksumValidator.ComputeChecksum(stream);
        if (!DataChecksumValidator.IsKnownChecksum(checksum))
        {
            Log.Warning(
                "Checksum inconsistency, {{{0}}} may not be steam vanilla from the modbranch beta branch or is a new version.",
                path);
        }

        UndertaleData data = UndertaleIO.Read(stream, LogUtmtWarning, LogUtmtMessage);
        _data = data;
        DataPath = path;
        MslContext.Attach(data, checksum);

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

        // A cancellation that arrived during the (uninterruptible) parse must discard the data
        // instead of leaving a half-initialised file attached.
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void SaveCore(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Unique temp file next to the target: a crashed or concurrent run can never clobber the
        // temp file of another save, and the target is only touched by the final swap.
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        List<Exception> exceptions = new();
        bool succeeded = false;
        bool cancelled = false;

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

        // Cancelling during the write must never replace the target: the temp file is thrown away.
        if (succeeded && cancellationToken.IsCancellationRequested)
        {
            succeeded = false;
            cancelled = true;
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

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (!succeeded)
        {
            throw new AggregateException(exceptions);
        }
    }

    private void SaveTo(string path)
    {
        // Defensive: a caller that patches through the API and saves without going through
        // PatchRunner must not lose a queued table write (see TableDraftStore).
        TableDraftStore.FlushAll();

        // The library patches its pointers by seeking backwards while it writes. A FileStream
        // discards its buffer on every seek, which turns the tail of the save into tens of thousands
        // of scattered small writes: measured 9.1 s straight to disk versus 3.4 s into memory plus
        // 0.2 s for one sequential copy. Serialising into memory and copying once is therefore ~5.5 s
        // faster while producing exactly the same bytes.
        long expected = EstimateSaveSize();
        if (expected > MaxInMemorySaveBytes)
        {
            Log.Warning(
                "The data file is very large (~{Size} MB); writing it directly to disk instead of through memory",
                expected / (1024 * 1024));
            WriteDirectly(path);
            return;
        }

        MemoryStream? memory = null;
        try
        {
            memory = new MemoryStream((int)Math.Max(expected, 16L * 1024 * 1024));
        }
        catch (OutOfMemoryException)
        {
            Log.Warning("Not enough memory to buffer the data file; writing it directly to disk");
        }

        if (memory is null)
        {
            WriteDirectly(path);
            return;
        }

        using (memory)
        {
            UndertaleIO.Write(memory, MslContext.Data, LogUtmtMessage);
            QoiConverter.ClearSharedBuffer();

            using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            memory.Position = 0;
            memory.CopyTo(stream, 1 << 20);
        }
    }

    /// <summary>Rough output size: the loaded file plus headroom for what the mods add.</summary>
    private long EstimateSaveSize()
    {
        try
        {
            if (!string.IsNullOrEmpty(DataPath) && File.Exists(DataPath))
            {
                return new FileInfo(DataPath).Length * 5 / 4;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the default estimate below.
        }

        return 32L * 1024 * 1024;
    }

    /// <summary>The previous path: let the library write straight into the file.</summary>
    private void WriteDirectly(string path)
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
