using ModShardLauncher.Mods;
using ModShardPackerReference;
using Serilog;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>Result of compiling a mod source folder into a <c>.sml</c> package.</summary>
public sealed record ModCompileResult(bool Succeeded, string OutputDirectory, string? OutputFile);

/// <summary>
/// Compiles a modding project with the in-process Roslyn based packer.
/// The compiler needs the runtime location of this assembly (<c>ModShardLauncher.dll</c>),
/// which is why the desktop build is published as multiple files instead of a single file.
/// </summary>
public sealed class ModCompiler
{
    private readonly string _outputDirectory;

    public ModCompiler(string? outputDirectory = null)
    {
        _outputDirectory = outputDirectory ?? AppPaths.ModsDirectory;
    }

    public ModCompileResult Compile(string sourceDirectory)
    {
        string outputDirectory = _outputDirectory;
        Directory.CreateDirectory(outputDirectory);
        string expectedFile = Path.Combine(outputDirectory, Path.GetFileName(sourceDirectory) + ".sml");

        bool succeeded;
        try
        {
            succeeded = FilePacker.Pack(
                null!,
                sourceDirectory,
                outputDirectory,
                sourceDirectory,
                AppInfo.MslVersion,
                new[] { typeof(Mod), typeof(UndertaleCode) });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Compilation of {0} failed", sourceDirectory);
            succeeded = false;
        }

        if (!succeeded)
        {
            Log.Error("Compilation of {0} failed", sourceDirectory);
            return new ModCompileResult(false, outputDirectory, null);
        }

        Log.Information("Compiled {0} into {1}", sourceDirectory, expectedFile);
        return new ModCompileResult(true, outputDirectory, File.Exists(expectedFile) ? expectedFile : null);
    }
}
