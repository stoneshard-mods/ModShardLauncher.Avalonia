using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace ModShardLauncher
{
    /// <summary>
    /// Enum used in <see cref="MatchFrom"/>, <see cref="MatchBelow"/> and <see cref="MatchAll"/> to tag specific lines.
    /// </summary>
    public enum Match
    {
        Before,
        Matching,
        After,
    }
    /// <summary>
    /// Enum to know how the code handled was extracted with UTMT. The most classic cases are code as string either decompiled from GML or disassemblied from Assembly-like GML.
    /// </summary>
    public enum PatchingWay
    {
        GML,
        AssemblyAsInstructions,
        AssemblyAsString,
    }
    /// <summary>
    /// Informations around the code handled to notably recompiled it easily.
    /// </summary>
    public class Header
    {
        public readonly string fileName;
        public readonly UndertaleCode originalCode;
        public readonly PatchingWay patchingWay;

        public Header(string fileName, UndertaleCode originalCode, PatchingWay patchingWay)
        {
            this.fileName = fileName;
            this.originalCode = originalCode;
            this.patchingWay = patchingWay;
        }
    }
    /// <summary>
    /// Result of the <see cref="Save"/> method. Contains information that can help debugging if something went wrong.
    /// </summary>
    public class ModSummary
    {
        public readonly string fileName;
        public readonly string newCode;
        public readonly PatchingWay patchingWay;

        public ModSummary(string fileName, string newCode, PatchingWay patchingWay)
        {
            this.fileName = fileName;
            this.newCode = newCode;
            this.patchingWay = patchingWay;
        }

        public override string ToString()
        {
            return string.Format("{0} was patched by {1}:\n{2}", fileName, patchingWay, newCode);
        }
    }
    /// <summary>
    /// A class wrapper that encapsulate a <see cref="Header"/> and an IEnumerable. That's the main class used while modding under the hood.
    /// </summary>
    public class FileEnumerable<T>
    {
        public readonly Header header;
        public readonly IEnumerable<T> ienumerable;
        public FileEnumerable(Header header, IEnumerable<T> ienumerable)
        {
            this.header = header;
            this.ienumerable = ienumerable;
        }
    }
    /// <summary>
    /// A static class for notably IEnumerable Extensions to provide a functional-programing-like api while modding.
    /// </summary>
    public static partial class Msl
    {
        /// <summary>
        /// Return the UndertaleCode from <paramref name="fileName"/>.
        /// </summary>
        public static UndertaleCode GetUMTCodeFromFile(string fileName)
        {
            UndertaleCode code = MslContext.Data.Code.First(t => t.Name?.Content == fileName);

            // A queued table write has to reach the script before its code entry is handed out, so
            // disassembly/compilation of that entry sees the rows the API already reports.
            TableDraftStore.FlushForCode(code);
            Log.Information("Found function: {0}", code);

            return code;
        }
        /// <summary>
        /// Add a new UndertaleCode named <paramref name="name"/> using the code <paramref name="codeAsString"/>. It is expected to be written in GML.
        /// </summary>
        /// <param name="codeAsString"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static UndertaleCode AddCode(string codeAsString, string name)
        {
            UndertaleCode code = new();
            UndertaleCodeLocals locals = new();
            code.Name = MslContext.Data.Strings.MakeString(name);
            locals.Name = code.Name;
            UndertaleCodeLocals.LocalVar argsLocal = new()
            {
                Name = MslContext.Data.Strings.MakeString("arguments"),
                Index = 0
            };
            locals.Locals.Add(argsLocal);
            code.LocalsCount = 1;
            MslContext.Data.CodeLocals.Add(locals);
            code.ReplaceGML(codeAsString, MslContext.Data);
            MslContext.Data.Code.Add(code);
            return code;
        }
        /// <summary>
        /// Add a new UndertaleCode named <paramref name="name"/> using the code <paramref name="codeAsString"/>. It is expected to be written in ASM abstraction.
        /// </summary>
        /// <param name="codeAsString"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static UndertaleCode AddCodeAsm(string codeAsString, string name)
        {
            UndertaleCode code = new();
            UndertaleCodeLocals locals = new();
            code.Name = MslContext.Data.Strings.MakeString(name);
            locals.Name = code.Name;
            UndertaleCodeLocals.LocalVar argsLocal = new()
            {
                Name = MslContext.Data.Strings.MakeString("arguments"),
                Index = 0
            };
            locals.Locals.Add(argsLocal);
            code.LocalsCount = 1;
            MslContext.Data.CodeLocals.Add(locals);
            CheckInstructionsVariables(code, codeAsString);
            string newLocalVarsAsString = AssemblyWrapper.CreateLocalVarAssemblyAsString(code);
            codeAsString = codeAsString.Insert(codeAsString.IndexOf('\n') + 1, newLocalVarsAsString);
            code.Replace(Assembler.Assemble(codeAsString, MslContext.Data));
            MslContext.Data.Code.Add(code);
            return code;
        }
        /// <summary>
        /// Get code from file in this tool.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static string GetCodeRes(string name)
        {
            return GmlResources.GetGML(name);
        }
        /// <summary>
        /// Add a new code from the code in this tool.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static UndertaleCode AddInnerCode(string name) => AddCode(GetCodeRes(name), name);
        /// <summary>
        /// Add a new function named <paramref name="name"/>.
        /// </summary>
        /// <param name="codeAsString"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static UndertaleCode AddFunction(string codeAsString, string name)
        {
            Log.Information("Trying to add the function : {0}", name);

            UndertaleCode scriptCode = AddCode(codeAsString, name);
            MslContext.Data.Code.Add(MslContext.Data.Code[0]);
            MslContext.Data.Code.RemoveAt(0);

            Log.Information("Successfully added the function : {0}", name);
            return scriptCode;
        }
        /// <summary>
        /// Add a new function from the code in this tool.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static UndertaleCode AddInnerFunction(string name) => AddFunction(GetCodeRes(name), name);
        /// <summary>
        /// Return the UndertaleCode as string from <paramref name="fileName"/>.
        /// </summary>
        public static string GetStringGMLFromFile(string fileName)
        {
            UndertaleCode code = GetUMTCodeFromFile(fileName);
            return MslContext.DecompileText(code);
        }
        /// <summary>
        /// Set the UndertaleCode in <paramref name="fileName"/> as <paramref name="codeAsString"/>.
        /// </summary>
        public static void SetStringGMLInFile(string codeAsString, string fileName)
        {
            UndertaleCode code = GetUMTCodeFromFile(fileName);
            code.ReplaceGML(codeAsString, MslContext.Data);
            MslContext.InvalidateDecompiledText(code);
        }
        /// <summary>
        /// Insert GML <paramref name="codeAsString"/> from a string in <paramref name="fileName"/> at a given <paramref name="position"/>.
        /// <para>
        /// <example>For example:
        /// <code>
        /// InsertGMLString("scr_atr("LVL") == global.max_level", "gml_Object_o_character_panel_mask_Draw_0", 3);
        /// </code>
        /// results in gml_Object_o_character_panel_mask_Draw_0 line 3 being <c>scr_atr("LVL") == global.max_level</c>.
        /// </example>
        /// </para>
        /// </summary>
        /// <param name="codeAsString">The code to insert.</param>
        /// <param name="fileName">The file to be patched.</param>
        /// <param name="position">The exact position to insert.</param>
        public static void InsertGMLString(string codeAsString, string fileName, int position)
        {
            Log.Information("Trying insert code in: {0}", fileName);

            List<string>? originalCode = GetStringGMLFromFile(fileName).Split("\n").ToList();
            originalCode.Insert(position, codeAsString);
            SetStringGMLInFile(string.Join("\n", originalCode), fileName);

            Log.Information("Patched function with InsertGMLString: {0}", fileName);
        }
        /// <summary>
        /// Replace an existing GML code by another <paramref name="code"/> from a string in <paramref name="file"/> at a given <paramref name="position"/>.
        /// <para>
        /// <example>For example:
        /// <code>
        /// ReplaceGMLString("scr_atr("LVL") == global.max_level", "gml_Object_o_character_panel_mask_Draw_0", 3);
        /// </code>
        /// results in gml_Object_o_character_panel_mask_Draw_0 line 3 being replaced by <c>scr_atr("LVL") == global.max_level</c>.
        /// </example>
        /// </para>
        /// </summary>
        /// <param name="code">The code to insert.</param>
        /// <param name="file">The file to be patched.</param>
        /// <param name="position">The exact position to insert.</param>
        public static void ReplaceGMLString(string codeAsString, string fileName, int position)
        {
            Log.Information("Trying replace code in: {0}", fileName);

            List<string>? originalCode = GetStringGMLFromFile(fileName).Split("\n").ToList();
            originalCode[position] = codeAsString;
            SetStringGMLInFile(string.Join("\n", originalCode), fileName);

            Log.Information("Patched function with ReplaceGMLString: {0}", fileName);
        }
        /// <summary>
        /// Replace an existing GML code by another <paramref name="code"/> from a string in <paramref name="file"/> at a given <paramref name="position"/>
        /// and remove the next len-1 lines.
        /// <para>
        /// <example>For example:
        /// <code>
        /// ReplaceGMLString("scr_atr("LVL") == global.max_level", "gml_Object_o_character_panel_mask_Draw_0", 3, 2);
        /// </code>
        /// results in gml_Object_o_character_panel_mask_Draw_0 line 3 being replaced by <c>scr_atr("LVL") == global.max_level</c>
        /// and line 4 being removed.
        /// </example>
        /// </para>
        /// </summary>
        /// <param name="code">The code to insert.</param>
        /// <param name="file">The file to be patched.</param>
        /// <param name="position">The exact position to insert.</param>
        public static void ReplaceGMLString(string codeAsString, string fileName, int start, int len)
        {
            Log.Information("Trying replace code in: {0}", fileName);

            List<string>? originalCode = GetStringGMLFromFile(fileName).Split("\n").ToList();
            originalCode[start] = codeAsString;
            for (int i = 1; i < Math.Min(len, originalCode.Count - start); i++)
            {
                originalCode[start + i] = "";
            }

            SetStringGMLInFile(string.Join("\n", originalCode), fileName);

            Log.Information("Patched function with ReplaceGMLString: {0}", fileName);
        }
    }
}
