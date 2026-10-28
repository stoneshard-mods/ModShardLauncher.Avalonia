using Serilog;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace ModShardLauncher;

/// <summary>
/// Writes a <c>gml_GlobalScript_table_*</c> script by rebuilding its instruction list instead of
/// recompiling GML.
/// <para>
/// These scripts are always the same construct: an inline array literal returned by a script
/// function. The GameMaker compiler emits one <c>push.s</c> (string) plus one <c>conv</c> per element
/// in <b>reverse</b> document order, then <c>call @@NewGMLArray@@</c> whose <c>ArgumentsCount</c> is
/// the element count. Only the element run and that count depend on the table content, so the rest
/// of the script (prologue, <c>ret</c>/<c>exit</c>, function registration tail) is kept as is.
/// </para>
/// <para>
/// The leading <c>push.i &lt;marker&gt;; break</c> pair that the original data file has after the
/// branch is dropped on purpose: that is what recompiling the decompiled text produces today, and
/// this writer must keep the output byte-identical to the compiler path it replaces.
/// </para>
/// <para>
/// <see cref="TryReplace"/> validates the whole shape first and returns false without touching the
/// entry when anything is unexpected, so the caller can fall back to a real GML compile.
/// </para>
/// </summary>
internal static class TableInstructionWriter
{
    private const string ArrayFunction = "@@NewGMLArray@@";

    /// <summary>
    /// Rebuilds <paramref name="code"/> so its array literal contains <paramref name="rows"/> (in
    /// document order). Returns false when the script does not have the expected shape.
    /// </summary>
    internal static bool TryReplace(UndertaleCode code, IReadOnlyList<string> rows)
    {
        if (code.ParentEntry is not null || rows.Count is 0 or > ushort.MaxValue)
        {
            return false;
        }

        List<UndertaleInstruction> instructions = code.Instructions;
        int callIndex = instructions.FindIndex(
            x => x.Kind == UndertaleInstruction.Opcode.Call
                 && x.Function?.Target?.Name?.Content == ArrayFunction);

        if (callIndex < 0)
        {
            return false;
        }

        int arguments = instructions[callIndex].ArgumentsCount;
        if (arguments <= 0 || arguments * 2 > callIndex)
        {
            return false;
        }

        int start = callIndex - arguments * 2;
        if (!IsStringRun(instructions, start, arguments))
        {
            return false;
        }

        int branchIndex = instructions.FindIndex(x => x.Kind == UndertaleInstruction.Opcode.B);
        int exitIndex = instructions.FindIndex(x => x.Kind == UndertaleInstruction.Opcode.Exit);
        if (branchIndex < 0 || branchIndex >= start || exitIndex <= callIndex || exitIndex + 1 >= instructions.Count)
        {
            return false;
        }

        // Reproduce the compiler's output: the "function definition" header pair is not emitted
        // when the script is rebuilt from its decompiled text.
        int prefixEnd = start;
        if (prefixEnd >= 2 && IsDefinitionHeader(instructions[prefixEnd - 2], instructions[prefixEnd - 1]))
        {
            prefixEnd -= 2;
        }

        List<UndertaleInstruction> rebuilt = new(prefixEnd + rows.Count * 2 + (instructions.Count - callIndex));
        for (int i = 0; i < prefixEnd; i++)
        {
            rebuilt.Add(instructions[i]);
        }

        // Elements go in reverse document order, one push/conv pair each.
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            rebuilt.Add(PushString(rows[i]));
            rebuilt.Add(ConvString());
        }

        UndertaleInstruction call = instructions[callIndex];
        call.ArgumentsCount = (ushort)rows.Count;
        for (int i = callIndex; i < instructions.Count; i++)
        {
            rebuilt.Add(instructions[i]);
        }

        code.Replace(rebuilt);

        // The branch skips the array body and lands on the function registration tail; its offset
        // depends on the number of elements, so it has to be recomputed from the new addresses.
        UndertaleInstruction branch = code.Instructions[branchIndex];
        UndertaleInstruction target = code.Instructions[code.Instructions.FindIndex(x => x.Kind == UndertaleInstruction.Opcode.Exit) + 1];
        branch.JumpOffset = (int)(target.Address - branch.Address);

        Log.Debug("Rebuilt table script {Name} by instruction ({Count} rows)", code.Name?.Content, rows.Count);
        return true;
    }

    private static bool IsStringRun(List<UndertaleInstruction> instructions, int start, int arguments)
    {
        for (int i = 0; i < arguments; i++)
        {
            UndertaleInstruction push = instructions[start + i * 2];
            UndertaleInstruction conv = instructions[start + i * 2 + 1];
            if (push.Kind != UndertaleInstruction.Opcode.Push
                || push.Type1 != UndertaleInstruction.DataType.String
                || push.Value is not UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>)
            {
                return false;
            }

            if (conv.Kind != UndertaleInstruction.Opcode.Conv
                || conv.Type1 != UndertaleInstruction.DataType.String
                || conv.Type2 != UndertaleInstruction.DataType.Variable)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDefinitionHeader(UndertaleInstruction push, UndertaleInstruction @break) =>
        push.Kind == UndertaleInstruction.Opcode.Push
        && push.Type1 == UndertaleInstruction.DataType.Int32
        && @break.Kind == UndertaleInstruction.Opcode.Break
        && @break.Type1 == UndertaleInstruction.DataType.Int16;

    private static UndertaleInstruction PushString(string row)
    {
        // The compiler binds literals to the last pool entry with that content (see StringPool
        // .InternLast); using the same entry keeps the output byte-identical to a real compile.
        UndertaleString value = StringPool.InternLast(row, out int id);
        return new UndertaleInstruction
        {
            Kind = UndertaleInstruction.Opcode.Push,
            Type1 = UndertaleInstruction.DataType.String,
            Type2 = UndertaleInstruction.DataType.Double,
            Value = new UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>(value, id),
        };
    }

    private static UndertaleInstruction ConvString() => new()
    {
        Kind = UndertaleInstruction.Opcode.Conv,
        Type1 = UndertaleInstruction.DataType.String,
        Type2 = UndertaleInstruction.DataType.Variable,
    };
}
