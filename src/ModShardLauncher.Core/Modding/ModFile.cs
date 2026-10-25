using System.Reflection;
using System.Text;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher;

/// <summary>
/// One entry inside a packed <c>.sml</c> archive. Names use the archive's historical
/// backslash-separated convention (for example <c>MyMod\icon.png</c>).
/// </summary>
public class FileChunk
{
    public string name = string.Empty;
    public int offset;
    public int length;
}

/// <summary>A modding project found under <c>ModSources/</c> (a folder containing a .csproj).</summary>
public class ModSource
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Existed => File.Exists(Path);
    public override string ToString() => Name;
}

public enum PatchStatus
{
    None,
    Patching,
    Success,
}

/// <summary>
/// A loaded <c>.sml</c> mod package: the embedded assembly, its icon and its packed files.
/// This type is part of the modding contract (<see cref="Mod.ModFiles"/>) and must keep
/// its public shape.
/// </summary>
public class ModFile
{
    public string Name = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<FileChunk> Files = new();
    public Assembly? Assembly;
    public int FileOffset;
    public FileStream? Stream;
    public string Path = string.Empty;
    public Mod? instance { get; set; }
    public bool Enabled { get; set; }
    public PatchStatus PatchStatus { get; set; } = PatchStatus.None;
    public bool Existed => File.Exists(Path);
    public byte[] Icon { get; set; } = Array.Empty<byte>();

    /// <summary>Invoked when the package disappeared from disk, so the catalog can rescan.</summary>
    internal Action? ReloadRequested { get; set; }

    public override string ToString() => instance?.ToString() ?? Name;

    public byte[] GetFile(string fileName)
    {
        if (!Existed)
        {
            Log.Error("The mod {0} which was located at {1} does not exist anymore.", Name, Path);
            ReloadRequested?.Invoke();
            return Array.Empty<byte>();
        }

        FileChunk? file = Files.Find(t => System.IO.Path.GetFileName(t.name) == System.IO.Path.GetFileName(fileName));
        if (file == null)
        {
            throw new FileNotFoundException($"File {fileName} not found in the packed sml.");
        }

        FileStream stream = Stream ??= new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!stream.CanRead)
        {
            stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream = stream;
        }

        try
        {
            long position = (long)FileOffset + file.offset;
            if (position < 0 || position > stream.Length)
            {
                throw new InvalidDataException($"File {fileName} points outside the packed sml.");
            }

            stream.Position = position;
            return SmlPackageReader.ReadBytes(stream, file.length);
        }
        finally
        {
            stream.Close();
            if (ReferenceEquals(Stream, stream))
            {
                Stream = null;
            }
        }
    }

    public string GetCode(string fileName)
    {
        byte[] data = GetFile(fileName);
        if (data.Length == 0)
        {
            Log.Warning("{0} is empty.", fileName);
            return "";
        }

        // A UTF-8 BOM produced by some editors confuses the GML compiler, so drop it.
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            data = data.Skip(3).ToArray();
        }

        string text = Encoding.UTF8.GetString(data);
        if (text.Length == 0)
        {
            Log.Error("The mod {0} which was located at {1} does not exist anymore.", Name, Path);
            throw new ArgumentException("String cannot be of length zero");
        }

        return text;
    }

    public bool FileExist(string fileName) => GetFile(fileName).Length > 0;
}
