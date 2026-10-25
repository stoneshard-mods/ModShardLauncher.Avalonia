using System.Text;
using ModShardLauncher;

namespace ModShardLauncherTest;

/// <summary>
/// B8: packagereads must never silently zero-pad. Truncated or malformed containers have to
/// throw a descriptive exception instead of producing corrupt assemblies/code.
/// </summary>
public class SmlPackageReaderTests
{
    [Fact]
    public void Read_MagicMismatch_ReturnsNull()
    {
        string path = WritePackage(Encoding.UTF8.GetBytes("NOPE" + new string('1', 24)));
        try
        {
            Assert.Null(SmlPackageReader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_TruncatedHeader_ThrowsEndOfStream()
    {
        string path = WritePackage(Encoding.UTF8.GetBytes("MSLM" + new string('1', 8)));
        try
        {
            Assert.ThrowsAny<EndOfStreamException>(() => SmlPackageReader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_NegativeChunkCount_ThrowsInvalidData()
    {
        List<byte> data = new(BuildHeader());
        AddInt32(data, -1);             // negative count in the first table
        string path = WritePackage(data.ToArray());
        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => SmlPackageReader.Read(path));
            Assert.Contains("chunk count", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ChunkOutsidePayload_ThrowsInvalidData()
    {
        List<byte> data = new(BuildHeader());
        AddInt32(data, 1);              // one chunk in the first table
        AddInt32(data, 3);              // name length
        data.AddRange(Encoding.UTF8.GetBytes("abc"));
        AddInt32(data, 100);            // offset
        AddInt32(data, 100);            // length
        AddInt32(data, 0);              // remaining three tables are empty
        AddInt32(data, 0);
        AddInt32(data, 0);

        string path = WritePackage(data.ToArray());
        try
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => SmlPackageReader.Read(path));
            Assert.Contains("outside the payload", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildHeader()
    {
        List<byte> bytes = new(Encoding.UTF8.GetBytes("MSLM"));
        bytes.AddRange(Encoding.UTF8.GetBytes(new string('1', 24)));
        return bytes.ToArray();
    }


    private static void AddInt32(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));

    private static string WritePackage(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"msl-test-{Guid.NewGuid():N}.sml");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
