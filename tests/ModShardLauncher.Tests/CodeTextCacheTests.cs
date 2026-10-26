using ModShardLauncher;
using UndertaleModLib.Models;
using Xunit.Abstractions;

namespace ModShardLauncherTest;

/// <summary>
/// Locks down the decompiled-text cache: a script must read back with the same text every time in a
/// run (the decompiler writes its authoring warnings into the text, and with a shared decompile
/// context those warnings used to be present only on the first read, which shifted every line and
/// made positional patching depend on mod order), while a rewrite must invalidate the entry.
/// </summary>
[Collection(GameDataCollection.Name)]
public class CodeTextCacheTests
{
    private readonly ITestOutputHelper _output;

    public CodeTextCacheTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RepeatedReads_ReturnIdenticalText()
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

        string first = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        string second = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        string joined = string.Join("\n", Msl.LoadGML(PatchBaselineSupport.LoaderCreateEvent).ienumerable);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(first, joined);
        _output.WriteLine($"stable text: {first.Length} chars, {first.Split('\n').Length} lines");
    }

    [Fact]
    public async Task WritingTheCode_InvalidatesTheCachedText()
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

        const string setProbe = "global.msl_text_cache_probe = 1;";
        const string saveProbe = "global.msl_save_cache_probe = 1;";
        string name = PatchBaselineSupport.LoaderCreateEvent;

        string original = Msl.GetStringGMLFromFile(name);
        Assert.DoesNotContain("msl_text_cache_probe", original);

        // Warm the cache, then rewrite through SetStringGMLInFile. The comments would be dropped by
        // the compiler, so the probe has to be a real statement.
        int lastLine = original.Split('\n').Length;
        Msl.InsertGMLString(setProbe, name, lastLine);

        string afterSet = Msl.GetStringGMLFromFile(name);
        Assert.Contains("msl_text_cache_probe", afterSet);

        // And again through the FileEnumerable/Save path, which must invalidate as well.
        FileEnumerable<string> enumerable = Msl.LoadGML(name);
        List<string> lines = enumerable.ienumerable.ToList();
        lines.Insert(lines.Count, saveProbe);
        new FileEnumerable<string>(enumerable.header, lines).Save();

        string afterSave = Msl.GetStringGMLFromFile(name);
        Assert.Contains("msl_text_cache_probe", afterSave);
        Assert.Contains("msl_save_cache_probe", afterSave);
    }

    [Fact]
    public async Task Cache_IsDroppedWhenAnotherDataFileIsLoaded()
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

        string before = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        UndertaleCode codeBefore = Msl.GetUMTCodeFromFile(PatchBaselineSupport.LoaderCreateEvent);

        // Reloading the same file produces brand new UndertaleCode objects; a stale entry keyed by
        // the previous object must not be observable.
        await service.LoadAsync(dataPath);

        string after = Msl.GetStringGMLFromFile(PatchBaselineSupport.LoaderCreateEvent);
        UndertaleCode codeAfter = Msl.GetUMTCodeFromFile(PatchBaselineSupport.LoaderCreateEvent);

        Assert.NotSame(codeBefore, codeAfter);
        Assert.Equal(before, after);
    }
}
