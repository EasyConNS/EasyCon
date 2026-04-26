using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace EasyCon.Script;

internal sealed class NativeLoader : IDisposable
{
    private readonly Dictionary<string, IntPtr> _loadedLibs = [];
    private bool _disposed;
    private readonly ModuleBuilder _moduleBuilder;
    private int _typeIndex;

    public NativeLoader()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("NativeLoaderAssembly"),
            AssemblyBuilderAccess.Run);
        _moduleBuilder = assembly.DefineDynamicModule("NativeLoaderModule");
    }

    public ImmutableArray<ForeignFunction> LoadExternFunctions(
        ImmutableArray<FunctionSymbol> externSymbols)
    {
        var result = ImmutableArray.CreateBuilder<ForeignFunction>();

        foreach (var group in externSymbols.GroupBy(s => s.LibraryName))
        {
            var libName = group.Key;
            if (!_loadedLibs.TryGetValue(libName, out var handle))
            {
                try
                {
                    handle = NativeLibrary.Load(libName);
                }
                catch (Exception ex)
                {
                    throw new ScriptException($"无法加载库 \"{libName}\": {ex.Message}", 0);
                }
                _loadedLibs[libName] = handle;
            }

            foreach (var symbol in group)
            {
                IntPtr funcPtr;
                try
                {
                    funcPtr = NativeLibrary.GetExport(handle, symbol.Name);
                }
                catch (Exception ex)
                {
                    throw new ScriptException($"在 \"{libName}\" 中未找到导出函数 \"{symbol.Name}\": {ex.Message}", 0);
                }

                result.Add(CreateThunk(symbol, funcPtr));
            }
        }

        return result.ToImmutable();
    }

    private ForeignFunction CreateThunk(FunctionSymbol symbol, IntPtr funcPtr)
    {
        var paramTypes = symbol.Parameters.Select(p => p.Type).ToArray();
        var returnType = symbol.ReturnType;
        var nativeParamTypes = paramTypes.Select(GetNativeType).ToArray();
        var nativeReturnType = GetNativeReturnType(returnType);

        var delegateType = DefineDelegateType(nativeParamTypes, nativeReturnType);
        var dlg = Marshal.GetDelegateForFunctionPointer(funcPtr, delegateType);
        var invokeMethod = delegateType.GetMethod("Invoke")!;

        Func<ImmutableArray<Value>, Value> thunk = args =>
        {
            var nativeArgs = new object?[paramTypes.Length];

            for (int i = 0; i < paramTypes.Length; i++)
                nativeArgs[i] = MarshalValue(args[i], paramTypes[i]);

            try
            {
                var result = invokeMethod.Invoke(dlg, nativeArgs);
                return UnmarshalReturnValue(result, returnType);
            }
            finally
            {
                for (int i = 0; i < paramTypes.Length; i++)
                {
                    if (paramTypes[i].Equals(ScriptType.String) && nativeArgs[i] is IntPtr ptrArg)
                        Marshal.FreeHGlobal(ptrArg);
                }
            }
        };

        return new ForeignFunction
        {
            Name = symbol.Name,
            Parameters = symbol.Parameters
                .Select((p, i) => (p.Name, p.Type))
                .ToArray(),
            ReturnType = returnType,
            Invoke = thunk,
        };
    }

    /// <summary>
    /// 动态定义委托类型，标注 Cdecl 调用约定，兼容各平台 C 函数
    /// </summary>
    private Type DefineDelegateType(Type[] paramTypes, Type returnType)
    {
        var typeName = $"NativeDelegate_{_typeIndex++}";
        var typeBuilder = _moduleBuilder.DefineType(typeName,
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

        // BeginInvoke
        var beginInvoke = typeBuilder.DefineMethod("BeginInvoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            typeof(IAsyncResult),
            [.. paramTypes, typeof(AsyncCallback), typeof(object)]);
        beginInvoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        // EndInvoke
        var endInvoke = typeBuilder.DefineMethod("EndInvoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            returnType, [typeof(IAsyncResult)]);
        endInvoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        return typeBuilder.CreateType()!;
    }

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
            return new IntPtr(value.AsPtr());
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
        return typeof(void);
    }

    private static Type GetNativeReturnType(ScriptType type)
    {
        if (type.Equals(ScriptType.Void))
            return typeof(void);
        return GetNativeType(type);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var handle in _loadedLibs.Values)
        {
            try { NativeLibrary.Free(handle); } catch { }
        }
        _loadedLibs.Clear();
    }
}