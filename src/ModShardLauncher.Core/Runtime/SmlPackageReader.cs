using System.Text;
using System.Text.RegularExpressions;
using ModShardLauncher.Mods;
using Serilog;

namespace ModShardLauncher;

/// <summary>
/// Reads the packed <c>.sml</c> container produced by ModShardPacker:
/// magic "MSLM", a version string, four chunk tables and the compiled mod assembly.
/// The on-disk layout is a compatibility contract and is intentionally unchanged.
/// </summary>
public static class SmlPackageReader
{
    private const string Magic = "MSLM";
    private const int MaxChunkNameLength = 4096;
    private const int MaxChunksPerTable = 100_000;

    public static ModFile? Read(string path, ModAssemblyLoadContext? loadContext = null)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string modName = Path.GetFileNameWithoutExtension(path);

        ModFile file = new()
        {
            Stream = stream,
            Name = modName,
        };

        if (Encoding.UTF8.GetString(ReadBytes(stream, 4)) != Magic)
        {
            return null;
        }

        Regex versionDigits = new("0([0-9])");
        const byte dot = 0x2E;
        const byte zero = 0x30;
        const byte nine = 0x39;

        // The version string is at most 24 bytes and, unlike every other string in the
        // container, it has no terminator: it is simply followed by the first chunk table.
        // Stopping at the first non digit/dot byte is therefore not enough — a chunk count
        // whose leading byte is an ASCII digit (48..57 entries) is swallowed by the version.
        // Collect every plausible length instead, then keep the longest one whose chunk
        // table actually parses.
        byte[] versionPeek = ReadBytes(stream, 24);
        List<int> versionCandidates = new();
        for (int len = 1; len <= 24; len++)
        {
            byte b = versionPeek[len - 1];
            bool acceptable = len == 1 || b == dot || (b >= zero && b <= nine);
            if (!acceptable)
            {
                break;
            }

            if (b != dot)
            {
                versionCandidates.Add(len);
            }
        }

        int versionLength = 24;
        for (int i = versionCandidates.Count - 1; i >= 0; i--)
        {
            int candidate = versionCandidates[i];
            stream.Seek(4 + candidate, SeekOrigin.Begin);
            if (LooksLikeChunkTable(stream))
            {
                versionLength = candidate;
                break;
            }
        }

        if (versionLength == 24)
        {
            Log.Warning("Version number seems ill formed");
        }

        stream.Seek(4, SeekOrigin.Begin);
        byte[] versionBytes = ReadBytes(stream, versionLength);

        file.Version = versionDigits.Replace(Encoding.UTF8.GetString(versionBytes), "$1");

        ReadChunkTable(stream, file);
        ReadChunkTable(stream, file);
        ReadChunkTable(stream, file);
        ReadChunkTable(stream, file);

        // The package keeps its mod name inside the chunk paths (<ModName>\icon.png),
        // not in the header. Deriving the name from there means the file itself can be
        // renamed to <ModName>-<version>.sml without changing the mod's identity.
        string? packageRoot = GetPackageRootName(file);
        if (packageRoot is not null)
        {
            file.Name = packageRoot;
        }

        Log.Information("Reading {{{0}}} built with version {{{1}}}", file.Name, file.Version);

        if (stream.Position > int.MaxValue)
        {
            throw InvalidPackage(stream, "the chunk tables are too large");
        }

        file.FileOffset = (int)stream.Position;
        ValidateChunkRanges(stream, file);

        if (file.Files.Count > 0)
        {
            FileChunk last = file.Files[^1];
            long skip = (long)last.offset + last.length;
            if (skip > int.MaxValue)
            {
                throw InvalidPackage(stream, "the last chunk is too large");
            }

            ReadBytes(stream, (int)skip);
        }

        int assemblyLength = ReadInt32(stream, "assembly length");
        file.Assembly = (loadContext ?? ModAssemblyLoadContext.Instance).LoadMod(ReadBytes(stream, assemblyLength));
        file.Path = path;

        try
        {
            file.Icon = file.GetFile(file.Name + "\\icon.png");
        }
        catch
        {
            Log.Information("Cannot find the icon.png associated to {0}", Path.GetFileName(path));
        }

        file.Stream = null;
        return file;
    }

    /// <summary>
    /// Returns the folder name the packer used for every chunk (<c>ModName\icon.png</c>),
    /// or <c>null</c> when the package has no chunk paths.
    /// </summary>
    private static string? GetPackageRootName(ModFile file)
    {
        foreach (FileChunk chunk in file.Files)
        {
            int separator = chunk.name.IndexOfAny(new[] { '\\', '/' });
            if (separator > 0)
            {
                return chunk.name[..separator];
            }
        }

        return null;
    }

    /// <summary>Cheap sanity check used to tell the version length apart from the chunk table.</summary>
    private static bool LooksLikeChunkTable(FileStream stream)
    {
        try
        {
            long start = stream.Position;
            int count = ReadInt32(stream, "chunk count");
            bool looksValid = count >= 0 && count <= MaxChunksPerTable;

            if (looksValid && count > 0)
            {
                int nameLength = ReadInt32(stream, "chunk name length");
                if (nameLength <= 0 || nameLength > MaxChunkNameLength)
                {
                    looksValid = false;
                }
                else
                {
                    byte[] name = ReadBytes(stream, nameLength);
                    foreach (byte b in name)
                    {
                        if (b < 0x20 || b > 0x7E)
                        {
                            looksValid = false;
                            break;
                        }
                    }
                }
            }

            stream.Seek(start, SeekOrigin.Begin);
            return looksValid;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ReadChunkTable(FileStream stream, ModFile file)
    {
        int count = ReadInt32(stream, "chunk count");
        if (count < 0 || count > MaxChunksPerTable)
        {
            throw InvalidPackage(stream, $"invalid chunk count {count}");
        }

        for (int i = 0; i < count; i++)
        {
            int nameLength = ReadInt32(stream, "chunk name length");
            if (nameLength < 0 || nameLength > MaxChunkNameLength)
            {
                throw InvalidPackage(stream, $"invalid chunk name length {nameLength}");
            }

            string name = Encoding.UTF8.GetString(ReadBytes(stream, nameLength));
            int offset = ReadInt32(stream, "chunk offset");
            int length = ReadInt32(stream, "chunk length");
            if (offset < 0 || length < 0)
            {
                throw InvalidPackage(stream, $"invalid chunk range offset={offset} length={length}");
            }

            file.Files.Add(new FileChunk
            {
                name = name,
                offset = offset,
                length = length,
            });
        }
    }

    /// <summary>Every chunk must fit inside the payload area that follows the tables.</summary>
    private static void ValidateChunkRanges(FileStream stream, ModFile file)
    {
        long payloadLength = stream.Length - file.FileOffset;
        foreach (FileChunk chunk in file.Files)
        {
            if ((long)chunk.offset + chunk.length > payloadLength)
            {
                throw InvalidPackage(
                    stream,
                    $"chunk '{chunk.name}' points outside the payload (offset={chunk.offset}, length={chunk.length})");
            }
        }
    }

    private static int ReadInt32(FileStream stream, string field)
    {
        byte[] bytes = ReadBytes(stream, 4);
        return BitConverter.ToInt32(bytes, 0);
    }

    /// <summary>
    /// Reads exactly <paramref name="length"/> bytes. Short reads (network shares, a file that is
    /// still being copied, truncated packages) throw instead of silently zero-padding the buffer.
    /// The helper never closes the stream; the caller owns its lifetime.
    /// </summary>
    internal static byte[] ReadBytes(FileStream stream, int length)
    {
        if (length < 0)
        {
            throw InvalidPackage(stream, $"negative read length {length}");
        }

        long remaining = stream.Length - stream.Position;
        if (remaining < length)
        {
            throw new EndOfStreamException(
                $"In SmlPackageReader.ReadBytes cannot read {length} bytes in the mod {Path.GetFileName(stream.Name)} (remaining {remaining})");
        }

        byte[] bytes = new byte[length];
        stream.ReadExactly(bytes, 0, length);
        return bytes;
    }

    private static InvalidDataException InvalidPackage(FileStream stream, string detail) =>
        new($"Invalid .sml package '{Path.GetFileName(stream.Name)}': {detail}.");
}
