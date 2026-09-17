using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 模块编码器（docs/EcmEcxFormat.md §4）：把「只含本模块函数」的 SSA 全量编码为
/// 独立模块产物（ModuleArtifact，可持久化为 .ecm）。外部依赖符号经 externalFunctions
/// 落导入标记（链接期解析）；类型表布局见 <see cref="EcxStructLayouts"/>；
/// 产物合并 → EcxImage 见 <see cref="EcxPipeline"/>。
/// </summary>
public static class EcxModuleEncoder
{
    /// <summary>

    /// 模块编译模式（ModuleSystem.md M5）：把「只含本模块函数」的 SSA 全量编码为独立产物。

    /// 与 v1 CompileToModules 的区别：外部依赖符号经 externalFunctions 落导入标记

    /// （而非 CallN 原生）；lib 模块的 $eval 语义 = &lt;init:module&gt;（HasInit/InitFid 供链接合成）。

    /// </summary>

    public static ModuleArtifact CompileWholeProgramAsModule(

        SsaProgram program, string moduleName,

        IReadOnlySet<FunctionSymbol>? externalFunctions,

        bool hasInit, bool isMain)

    {

        var structIds = EcxStructLayouts.AssignStructIds(program);

        var structLayouts = EcxStructLayouts.BuildStructLayouts(program, structIds);



        var ctx = new ModuleEncodeContext

        {

            Program = program,

            ModuleName = moduleName,

            LocalFuncIds = new Dictionary<FunctionSymbol, int>(),

            Imports = new List<EcsImport>(),

            ImportIds = new Dictionary<(string, int), int>(),

            Globals = new List<EcsGlobal>(),

            GlobalSlots = new Dictionary<GlobalVariableSymbol, int>(),

            Natives = new List<EcsNative>(),

            NativeIds = new Dictionary<string, int>(),

            StructIds = structIds,

            Pool = new ModulePool(),

            ExternalFunctions = externalFunctions,

        };



        // 导出按声明名序（确定性）；$eval 恒最后（v1 链接期定位入口的约定保持）

        var ordered = program.Functions.Values

            .Where(f => program.MainFunction == null || !ReferenceEquals(f, program.MainFunction))

            .OrderBy(f => f.Symbol.Name, StringComparer.Ordinal)

            .Select(f => (Fn: f, Sym: f.Symbol))

            .ToList();

        if (program.MainFunction != null)

            ordered.Add((program.MainFunction, program.MainFunction.Symbol));



        var functions = new List<EcsFunction>();

        var exports = new List<EcsExport>();

        int initFid = -1;

        foreach (var (fn, sym) in ordered)

        {

            bool isEval = program.MainFunction != null && ReferenceEquals(fn, program.MainFunction);

            bool encodeEval = !isEval || isMain || hasInit;   // 空顶层语句的 lib 模块不编码 $eval

            ctx.LocalFuncIds[sym] = functions.Count;

            if (!encodeEval)

                continue;

            functions.Add(BytecodeEncoder.Encode(fn, sym, ctx.LocalFuncIds[sym], ctx));

            if (isEval)

            {

                initFid = ctx.LocalFuncIds[sym];

                if (isMain)

                    exports.Add(new EcsExport { Name = sym.Name, LocalFid = ctx.LocalFuncIds[sym], NParams = 0, HasReturn = false });

            }

            else

            {

                exports.Add(new EcsExport

                {

                    Name = sym.Name,

                    LocalFid = ctx.LocalFuncIds[sym],

                    NParams = sym.Parameters.Length,

                    HasReturn = !sym.ReturnType.Equals(ScriptType.Void),

                });

            }

        }



        // 模块级 KeyAction/NeedIL：链接期并集（对齐 v1 SsaProgramBuilder 的全程序扫描语义）

        bool keyAction = program.Functions.Values.Any(f => f.Blocks.Any(b =>

            b.Instructions.Any(i => i.Op is SsaOp.KeyPress or SsaOp.KeyAction)));



        return new ModuleArtifact

        {

            Name = moduleName,

            Functions = functions,

            Pool = ctx.Pool,

            Imports = ctx.Imports,

            Exports = exports,

            Globals = ctx.Globals,

            Natives = ctx.Natives,

            Structs = structLayouts,

            ILNames = program.ILNames.ToList(),

            HasEval = isMain,

            HasInit = hasInit,

            InitFid = hasInit ? initFid : -1,

            KeyAction = keyAction,

            NeedIL = program.ILNames.Length > 0,

            Interface = null,

        };

    }

}