using System.Reflection;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher;

/// <summary>
/// Discovers the content of <c>Mods/</c> and <c>ModSources/</c>, loads every <c>.sml</c> package and
/// instantiates its <see cref="Mod"/> entry point. Replaces the static ModLoader file scan.
/// </summary>
public sealed class ModCatalog
{
    private readonly string _modsDirectory;
    private readonly string _sourcesDirectory;
    private readonly List<ModFile> _mods = new();
    private readonly List<ModSource> _modSources = new();
    private readonly List<string> _loadErrors = new();

    /// <summary>
    /// Loaded packages by file path. Assemblies are loaded into a <b>non collectible</b>
    /// <see cref="ModAssemblyLoadContext"/>, so reloading an unchanged package would leak one
    /// assembly per patch run. Reusing the package keeps one assembly per <c>.sml</c>; a fresh
    /// <see cref="Mod"/> instance is still created for every reload, so mod state does not leak
    /// between runs.
    /// </summary>
    private readonly Dictionary<string, CachedPackage> _packages = new(StringComparer.Ordinal);

    public ModCatalog(string? modsDirectory = null, string? sourcesDirectory = null)
    {
        _modsDirectory = modsDirectory ?? AppPaths.ModsDirectory;
        _sourcesDirectory = sourcesDirectory ?? AppPaths.ModSourcesDirectory;
    }

    public IReadOnlyList<ModFile> Mods => _mods;

    public IReadOnlyList<ModSource> ModSources => _modSources;

    /// <summary>Non fatal problems (for example a package without a Mod subclass) for the UI to surface.</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    public event EventHandler? Changed;

    /// <summary>Rescans both folders. Enabled flags and mod identities are preserved across reloads.</summary>
    public void Reload()
    {
        Directory.CreateDirectory(_modsDirectory);
        Directory.CreateDirectory(_sourcesDirectory);


        foreach (ModFile mod in _mods)
        {
            mod.Stream?.Close();
        }

        _modSources.Clear();
        _loadErrors.Clear();

        foreach (string directory in EnumerateModSourceDirectories())
        {
            _modSources.Add(new ModSource
            {
                Name = Path.GetFileName(directory),
                Path = directory,
            });
        }

        List<ModFile> loaded = new();
        foreach (string file in Directory.GetFiles(_modsDirectory, "*.sml"))
        {
            string fileName = Path.GetFileName(file);
            try
            {
                if (TryReusePackage(file, fileName, out ModFile? reused))
                {
                    loaded.Add(reused!);
                    continue;
                }

                _packages.Remove(file);

                // One context per package build: a rebuilt .sml keeps its assembly identity, and the
                // runtime rejects loading that identity twice into the same context.
                ModAssemblyLoadContext loadContext =
                    ModAssemblyLoadContext.CreateForPackage($"{fileName}#{Guid.NewGuid():N}");

                ModFile? modFile = SmlPackageReader.Read(file, loadContext);
                if (modFile?.Assembly is null)
                {
                    continue;
                }

                Type? modType = FindModType(modFile.Assembly, fileName);
                if (modType is null)
                {
                    ReportLoadError($"Loading error: {fileName} ({modFile.Assembly.GetName().Name}) has no Mod subclass");
                    continue;
                }

                if (Activator.CreateInstance(modType) is not Mod mod)
                {
                    ReportLoadError($"Loading error: {fileName} cannot create {modType.FullName}");
                    continue;
                }

                mod.LoadAssembly();
                mod.ModFiles = modFile;
                modFile.instance = mod;
                modFile.ReloadRequested = Reload;

                ModFile? previous = _mods.Find(t => t.Name == modFile.Name);
                if (previous is not null)
                {
                    modFile.Enabled = previous.Enabled;
                }

                FileInfo info = new(file);
                _packages[file] = new CachedPackage(modFile, info.Length, info.LastWriteTimeUtc);
                loaded.Add(modFile);
            }
            catch (ReflectionTypeLoadException ex)
            {
                ReportLoadError($"Loading error: {fileName}: {SummarizeLoaderExceptions(ex)}", ex);
            }
            catch (Exception ex)
            {
                ReportLoadError($"Loading error: {fileName}: {DescribeException(ex)}", ex);
            }
        }

        PrunePackages();

        _mods.Clear();
        _mods.AddRange(loaded);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reuses the assembly of an unchanged <c>.sml</c> (same size and last write time) with a fresh
    /// <see cref="Mod"/> instance. Anything unexpected makes this return false so the package is
    /// loaded from scratch, exactly like before.
    /// </summary>
    private bool TryReusePackage(string path, string fileName, out ModFile? modFile)
    {
        modFile = null;
        if (!_packages.TryGetValue(path, out CachedPackage? cached) || cached.File.Assembly is null)
        {
            return false;
        }

        FileInfo info = new(path);
        if (info.Length != cached.Length || info.LastWriteTimeUtc != cached.LastWriteTimeUtc)
        {
            return false;
        }

        Type? modType = FindModType(cached.File.Assembly, fileName);
        if (modType is null || Activator.CreateInstance(modType) is not Mod instance)
        {
            return false;
        }

        cached.File.Stream?.Close();
        cached.File.Stream = null;

        instance.LoadAssembly();
        instance.ModFiles = cached.File;
        cached.File.instance = instance;
        cached.File.ReloadRequested = Reload;

        // The status belongs to the run, not to the package: a reload starts from "not patched".
        cached.File.PatchStatus = PatchStatus.None;

        Log.Debug("Reusing the loaded assembly of {File}", fileName);
        modFile = cached.File;
        return true;
    }

    /// <summary>Drops cached packages whose <c>.sml</c> disappeared from the mods folder.</summary>
    private void PrunePackages()
    {
        if (_packages.Count == 0)
        {
            return;
        }

        HashSet<string> present = new(
            Directory.GetFiles(_modsDirectory, "*.sml").Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (string path in _packages.Keys.Where(path => !present.Contains(Path.GetFullPath(path))).ToArray())
        {
            _packages.Remove(path);
        }
    }

    private sealed record CachedPackage(ModFile File, long Length, DateTime LastWriteTimeUtc);

    /// <summary>
    /// Snapshot of the currently enabled mod names for <c>Settings.json</c>. De-duplicated so a
    /// stale list from older versions is cleaned up on the next save.
    /// </summary>
    /// <summary>Locates the Mod entry point, tolerating partially loadable assemblies.</summary>
    private static Type? FindModType(Assembly assembly, string fileName)
    {
        try
        {
            return Array.Find(assembly.GetTypes(), t => t.IsSubclassOf(typeof(Mod)));
        }
        catch (ReflectionTypeLoadException ex)
        {
            Type? modType = ex.Types.FirstOrDefault(t => t is not null && t.IsSubclassOf(typeof(Mod)));
            if (modType is not null)
            {
                Log.Warning("Some types in {File} could not be loaded: {Summary}", fileName, SummarizeLoaderExceptions(ex));
                return modType;
            }

            throw;
        }
    }

    private static string SummarizeLoaderExceptions(ReflectionTypeLoadException ex)
    {
        IEnumerable<string> messages = ex.LoaderExceptions
            .Where(e => e is not null)
            .Select(e => e!.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(3);

        string summary = string.Join(" | ", messages);
        return summary.Length == 0 ? "unknown type load failure" : summary;
    }

    private static string DescribeException(Exception ex)
    {
        // Constructor failures surface as TargetInvocationException; report the real cause.
        Exception actual = ex is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : ex;
        return $"{actual.GetType().Name}: {actual.Message}";
    }

    private void ReportLoadError(string message, Exception? exception = null)
    {
        _loadErrors.Add(message);
        if (exception is null)
        {
            Log.Error(message);
        }
        else
        {
            Log.Error(exception, message);
        }
    }
    public static List<string> GetEnabledModNames(IEnumerable<ModFile> mods) =>
        mods.Where(m => m.Enabled)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Re-applies the enabled list persisted in <c>Settings.json</c>.</summary>
    public void ApplyEnabledMods(IEnumerable<string> names)
    {
        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            ModFile? modFile = _mods.Find(t => t.Name == name);
            if (modFile is not null)
            {
                modFile.Enabled = true;
            }
            else
            {
                Log.Warning("Mod {0} not found", name);
            }
        }
    }

    private IEnumerable<string> EnumerateModSourceDirectories()
    {
        // A mod source is a folder containing at least one .csproj at its root.
        return Directory
            .GetDirectories(_sourcesDirectory)
            .Where(x => Directory.EnumerateFiles(x, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault() != null);
    }
}
