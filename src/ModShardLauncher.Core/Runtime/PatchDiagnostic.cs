using Serilog;

namespace ModShardLauncher;

/// <summary>
/// Structured replacement for the old WinForms/RTF <c>MSLDiagnostic</c>.
/// The UI decides how to render <see cref="Summary"/> and <see cref="Details"/>.
/// </summary>
public sealed class PatchDiagnostic
{
    public PatchDiagnostic(Exception exception, ModFile? modFile)
    {
        Exception = exception;
        ModName = modFile?.Name ?? "(unknown mod)";

        if (exception.Data.Contains("fileName"))
        {
            FileName = $"{exception.Data["fileName"]}";
        }

        if (exception.Data.Contains("patchingWay"))
        {
            PatchingMethod = $"{exception.Data["patchingWay"]}";
        }
    }

    public string ModName { get; }

    public string? FileName { get; }

    public string? PatchingMethod { get; }

    public Exception Exception { get; }

    public string Title => $"Patching {ModName} failed";

    /// <summary>Short explanation with the same wording as the old dialog, without RTF.</summary>
    public string Summary
    {
        get
        {
            string summary = $"An error was encountered while patching the mod {ModName}:";
            if (FileName is not null)
            {
                summary += Environment.NewLine + $"In file {FileName}";
            }

            if (PatchingMethod is not null)
            {
                summary += $" by {PatchingMethod}";
            }

            return summary;
        }
    }

    public string Details => Exception.ToString();

    public void ToLog()
    {
        string extraInformation = " for {{{0}}}";
        if (FileName is not null)
        {
            extraInformation += " in file {{{1}}}";
        }

        if (PatchingMethod is not null)
        {
            extraInformation += " while patching by {{{2}}}";
        }

        Log.Error(Exception, "Something went wrong" + extraInformation, ModName, FileName, PatchingMethod);
    }
}
