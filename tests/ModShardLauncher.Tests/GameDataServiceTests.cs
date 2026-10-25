using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// Loading failures must leave the service in a clean state so the UI can never stay stuck in a
/// "loading" state (the legacy LoadingDialog bug: a failing load never hid the modal dialog).
/// </summary>
public class GameDataServiceTests
{
    [Fact]
    public async Task LoadAsync_InvalidDataFile_ResetsStateAndRethrows()
    {
        string path = Path.Combine(Path.GetTempPath(), $"msl-broken-{Guid.NewGuid():N}.win");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        GameDataService service = new();
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.LoadAsync(path));

            Assert.False(service.IsLoaded);
            Assert.Equal(string.Empty, service.DataPath);
            Assert.False(MslContext.HasData);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ResetsState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"msl-missing-{Guid.NewGuid():N}.win");
        GameDataService service = new();

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadAsync(path));

        Assert.False(service.IsLoaded);
        Assert.Equal(string.Empty, service.DataPath);
        Assert.False(MslContext.HasData);
    }
}
