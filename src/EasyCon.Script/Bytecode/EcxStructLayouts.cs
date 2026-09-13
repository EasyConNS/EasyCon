using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 结构体类型表布局构建（SsaProgram 结构声明 → EcsStructLayout，含槽位展开）。
/// 独立于链接 pass：模块编码（CompileWholeProgramAsModule）与未来的接口快照共用。
/// </summary>
internal static class EcxStructLayouts
{
    internal static Dictionary<string, int> AssignStructIds(SsaProgram program)

    {

        var ids = new Dictionary<string, int>();

        int sid = 0;

        foreach (var name in program.StructDefinitions.Keys.OrderBy(n => n, StringComparer.Ordinal))

            ids[name] = sid++;

        return ids;

    }



    // ---- 类型表布局 ----



    static EcsFieldLayout ToFieldLayout(EcsFieldDef field, Func<string, int> sidByName)

    {

        if (field.FieldType is ArrayType arr && arr.Count > 0)

            return new EcsFieldLayout

            {

                Name = field.Name,

                Kind = EcsFieldKind.FixedArray,

                Type = BytecodeEncoder.ToTypeCode(arr.ElementType),

                ElementType = BytecodeEncoder.ToTypeCode(arr.ElementType),

                Count = arr.Count,

            };



        if (field.FieldType is StructType st)

            return new EcsFieldLayout

            {

                Name = field.Name,

                Kind = EcsFieldKind.NestedStruct,

                Type = EcsTypeCode.Struct,

                ElementType = EcsTypeCode.Struct,

                Count = 0,

                NestedSid = sidByName(st.Definition.Name),

            };



        if (field.FieldType is ArrayType)

            return new EcsFieldLayout

            {

                Name = field.Name,

                Kind = EcsFieldKind.Boxed,

                Type = EcsTypeCode.Array,

                ElementType = EcsTypeCode.Array,

                Count = 0,

            };



        return new EcsFieldLayout

        {

            Name = field.Name,

            Kind = EcsFieldKind.Scalar,

            Type = BytecodeEncoder.ToTypeCode(field.FieldType),

            ElementType = BytecodeEncoder.ToTypeCode(field.FieldType),

            Count = 0,

        };

    }



    internal static List<EcsStructLayout> BuildStructLayouts(SsaProgram program, Dictionary<string, int> structIds)

    {

        var layouts = new Dictionary<string, EcsStructLayout>();

        foreach (var (name, def) in program.StructDefinitions)

        {

            layouts[name] = new EcsStructLayout

            {

                Name = name,

                Fields = def.Fields.Select(f => ToFieldLayout(f, n => structIds[n])).ToImmutableArray(),

            };

        }



        var nameBySid = structIds.ToDictionary(kv => kv.Value, kv => kv.Key);

        var computing = new HashSet<string>(StringComparer.Ordinal);



        int SlotCountOf(string name)

        {

            if (!layouts.TryGetValue(name, out var layout))

                return 1;

            if (layout.SlotCount > 0)

                return layout.SlotCount;

            if (!computing.Add(name))

                throw new BytecodeException(new[] { new BytecodeDiagnostic($"结构体 {name} 存在自嵌套环", name, 0) });

            int total = 0;

            foreach (var field in layout.Fields)

            {

                field.SlotOffset = total;

                total += field.Kind switch

                {

                    EcsFieldKind.FixedArray => field.Count,

                    EcsFieldKind.NestedStruct => SlotCountOf(nameBySid[field.NestedSid]),

                    _ => 1,

                };

            }

            computing.Remove(name);

            layout.SlotCount = total;

            return total;

        }



        foreach (var name in layouts.Keys.ToList())

            SlotCountOf(name);



        return structIds.OrderBy(kv => kv.Value).Select(kv => layouts[kv.Key]).ToList();

    }

}