using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace EasyCon.Script;

internal sealed class NativeLoader
{
    private static readonly ConcurrentDictionary<string, IntPtr> s_loadedLibs = new();
    private static readonly ConcurrentDictionary<(string Lib, string Func), IntPtr> s_funcPtrCache = new();
    private static readonly ConcurrentDictionary<DelegateSignature, Type> s_delegateTypeCache = new();
    private static readonly ModuleBuilder s_moduleBuilder;
    private static int s_typeIndex;

    [RequiresDynamicCode("Calls System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(AssemblyName, AssemblyBuilderAccess)")]
    static NativeLoader()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NativeLoaderAssembly"),
            AssemblyBuilderAccess.Run);
        s_moduleBuilder = assembly.DefineDynamicModule("NativeLoaderModule");
    }

    public ImmutableArray<(FunctionSymbol Symbol, LazyNativeCallable Callable)> RegisterExternFunctions(
        ImmutableArray<FunctionSymbol> externSymbols)
    {
        var result = ImmutableArray.CreateBuilder<(FunctionSymbol, LazyNativeCallable)>(externSymbols.Length);
        foreach (var symbol in externSymbols)
            result.Add((symbol, new LazyNativeCallable(symbol, this)));
        return result.ToImmutable();
    }

    internal ICallable ResolveFunction(FunctionSymbol symbol)
    {
        var libName = symbol.LibraryName;
        var handle = s_loadedLibs.GetOrAdd(libName, name =>
        {
            try { return NativeLibrary.Load(name); }
            catch (Exception ex)
            {
                throw new ScriptException($"无法加载库 \"{name}\": {ex.Message}", 0);
            }
        });

        var funcPtr = s_funcPtrCache.GetOrAdd((libName, symbol.ExternalName), _ =>
        {
            try { return NativeLibrary.GetExport(handle, symbol.ExternalName); }
            catch (Exception ex)
            {
                throw new ScriptException($"在 \"{libName}\" 中未找到导出函数 \"{symbol.ExternalName}\": {ex.Message}", 0);
            }
        });

        return CreateCallable(symbol, funcPtr);
    }

    [RequiresDynamicCode("Calls System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(nint, Type)")]
    private ICallable CreateCallable(FunctionSymbol symbol, IntPtr funcPtr)
    {
        var paramTypes = symbol.Parameters.Select(p => p.Type).ToArray();
        var returnType = symbol.ReturnType;
        var nativeParamTypes = paramTypes.Select(GetNativeType).ToArray();
        var nativeReturnType = GetNativeReturnType(returnType);

        var sig = new DelegateSignature(nativeParamTypes, nativeReturnType);
        var delegateType = s_delegateTypeCache.GetOrAdd(sig,
            _ => DefineDelegateType(nativeParamTypes, nativeReturnType));

        var dlg = Marshal.GetDelegateForFunctionPointer(funcPtr, delegateType);

        // 预计算哪些参数需要释放非托管字符串
        var stringParamIndices = paramTypes
            .Select((t, i) => t.Equals(ScriptType.String) ? i : -1)
            .Where(i => i >= 0)
            .ToArray();
        var hasStringParams = stringParamIndices.Length > 0;

        Func<ImmutableArray<Value>, Value> thunk;
        if (hasStringParams)
        {
            thunk = args =>
            {
                var nativeArgs = new object?[paramTypes.Length];
                for (int i = 0; i < paramTypes.Length; i++)
                    nativeArgs[i] = MarshalValue(args[i], paramTypes[i]);

                try
                {
                    return UnmarshalReturnValue(dlg.DynamicInvoke(nativeArgs), returnType);
                }
                finally
                {
                    foreach (var i in stringParamIndices)
                    {
                        if (nativeArgs[i] is IntPtr ptrArg)
                            Marshal.FreeHGlobal(ptrArg);
                    }
                }
            };
        }
        else
        {
            thunk = args =>
            {
                var nativeArgs = new object?[paramTypes.Length];
                for (int i = 0; i < paramTypes.Length; i++)
                    nativeArgs[i] = MarshalValue(args[i], paramTypes[i]);
                return UnmarshalReturnValue(dlg.DynamicInvoke(nativeArgs), returnType);
            };
        }

        return new DelegateCallable((args, _, _) =>
            thunk(ImmutableArray.Create(args.ToArray())));
    }

    /// <summary>
    /// 动态定义委托类型，标注 Cdecl 调用约定，兼容各平台 C 函数
    /// </summary>
    [RequiresUnreferencedCode("Calls System.Reflection.Emit.ModuleBuilder.DefineType(String, TypeAttributes, Type)")]
    private static Type DefineDelegateType(Type[] paramTypes, Type returnType)
    {
        var typeName = $"NativeDelegate_{Interlocked.Increment(ref s_typeIndex)}";
        var typeBuilder = s_moduleBuilder.DefineType(typeName,
            TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        // [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        var ufpaCtor = typeof(UnmanagedFunctionPointerAttribute)
            .GetConstructor([typeof(CallingConvention)])!;
        typeBuilder.SetCustomAttribute(
            new CustomAttributeBuilder(ufpaCtor, [CallingConvention.Cdecl]));

        // .ctor(IntPtr)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(IntPtr)]);
        ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        // Invoke
        var invoke = typeBuilder.DefineMethod("Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            returnType, paramTypes);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        return typeBuilder.CreateType()!;
    }

    private readonly record struct DelegateSignature(Type[] ParamTypes, Type ReturnType);

    #region Marshal

    private static object? MarshalValue(Value value, ScriptType targetType)
    {
        if (targetType.Equals(ScriptType.Int) || targetType.Equals(ScriptType.Bool))
            return value.AsInt();
        if (targetType.Equals(ScriptType.Double))
            return value.AsDouble();
        if (targetType.Equals(ScriptType.String))
            return MarshalStringToNative(value.AsString());
        if (targetType.Equals(ScriptType.Ptr))
        {
            // struct instance → pass native pointer directly
            if (value.TryGetStructPtr(out var structPtr))
                return structPtr;
            return new IntPtr(value.AsPtr());
        }
        if (targetType is StructType)
        {
            if (value.TryGetStructPtr(out var structPtr))
                return structPtr;
            return new IntPtr(value.AsPtr());
        }
        return null;
    }

    private static Value UnmarshalReturnValue(object? result, ScriptType returnType)
    {
        if (returnType.Equals(ScriptType.Void))
            return Value.Void;
        if (returnType.Equals(ScriptType.Int))
            return Value.FromInt(Convert.ToInt32(result));
        if (returnType.Equals(ScriptType.Bool))
            return Value.FromBool(Convert.ToInt32(result) != 0);
        if (returnType.Equals(ScriptType.Double))
            return Value.FromDouble(Convert.ToDouble(result));
        if (returnType.Equals(ScriptType.Ptr))
            return Value.FromPtr(((IntPtr)result!).ToInt64());
        if (returnType is StructType st)
        {
            var instance = new EcsStruct(st.Definition, (IntPtr)result!);
            return Value.FromStruct(instance);
        }
        return Value.Void;
    }

    /// <summary>
    /// 将字符串按平台编码分配到非托管内存：Windows 用 UTF-16，Linux/macOS 用 ANSI（.NET 上映射为 UTF-8）
    /// </summary>
    private static IntPtr MarshalStringToNative(string value) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Marshal.StringToHGlobalUni(value)
            : Marshal.StringToHGlobalAnsi(value);

    private static Type GetNativeType(ScriptType type)
    {
        if (type.Equals(ScriptType.Int) || type.Equals(ScriptType.Bool))
            return typeof(int);
        if (type.Equals(ScriptType.Double))
            return typeof(double);
        if (type.Equals(ScriptType.String))
            return typeof(IntPtr);
        if (type.Equals(ScriptType.Ptr))
            return typeof(IntPtr);
        if (type is StructType)
            return typeof(IntPtr);
        return typeof(void);
    }

    private static Type GetNativeReturnType(ScriptType type)
    {
        if (type.Equals(ScriptType.Void))
            return typeof(void);
        return GetNativeType(type);
    }

    #endregion

    // 库句柄和函数指针为进程级静态缓存，进程退出时由 OS 回收，无需手动释放
}