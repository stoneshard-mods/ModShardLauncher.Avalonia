using System.Text;
using System.Text.RegularExpressions;
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
    // Mod 版本是 Mod 子类上的属性，不写进程序集元数据，打包器也不会执行它；
    // 这里按本仓库约定从源码里读 `public override string Version => "vX.Y.Z.W";`，
    // 读不到时退回原来的 <工程名>.sml。
    private static readonly Regex VersionPattern = new(
        @"override\s+string\s+Version\s*=>\s*""([^""]+)""",
        RegexOptions.Compiled);

    private readonly string _outputDirectory;

    public ModCompiler(string? outputDirectory = null)
    {
        _outputDirectory = outputDirectory ?? AppPaths.ModsDirectory;
    }

    public ModCompileResult Compile(string sourceDirectory)
    {
        string outputDirectory = _outputDirectory;
        Directory.CreateDirectory(outputDirectory);

        string projectName = Path.GetFileName(sourceDirectory);
        string plainFile = Path.Combine(outputDirectory, projectName + ".sml");

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

        string outputFile = MoveToVersionedName(outputDirectory, projectName, plainFile, sourceDirectory);

        Log.Information("Compiled {0} into {1}", sourceDirectory, outputFile);
        return new ModCompileResult(true, outputDirectory, File.Exists(outputFile) ? outputFile : null);
    }

    /// <summary>
    /// Renames <c>&lt;project&gt;.sml</c> to <c>&lt;project&gt;-&lt;version&gt;.sml</c> when the mod source
    /// declares a <c>Version</c> string, and removes older packages of the same mod so the
    /// launcher never sees two assemblies with the same name.
    /// </summary>
    private static string MoveToVersionedName(string outputDirectory, string projectName, string plainFile, string sourceDirectory)
    {
        string? version = TryReadVersion(sourceDirectory);
        if (version is null || !File.Exists(plainFile))
        {
            return plainFile;
        }

        string versionedFile = Path.Combine(outputDirectory, $"{projectName}-{SanitizeFileName(version)}.sml");
        File.Move(plainFile, versionedFile, overwrite: true);

        // 只清理形如 <工程名>-v1.2.3.4.sml 的旧包，不碰 <工程名>-其它名字.sml。
        var oldPackage = new Regex(
            "^" + Regex.Escape(projectName) + @"-v?\d+(\.\d+)*\.sml$",
            RegexOptions.IgnoreCase);

        foreach (string existing in Directory.EnumerateFiles(outputDirectory, "*.sml"))
        {
            if (string.Equals(existing, versionedFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (oldPackage.IsMatch(Path.GetFileName(existing)))
            {
                try
                {
                    File.Delete(existing);
                    Log.Information("Removed older package {0}", existing);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not remove older package {0}", existing);
                }
            }
        }

        return versionedFile;
    }

    private static string? TryReadVersion(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return null;
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            System.Text.RegularExpressions.Match match = VersionPattern.Match(File.ReadAllText(file));
            if (match.Success)
            {
                string value = match.Groups[1].Value.Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}
