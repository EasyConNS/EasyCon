namespace EasyCon.Script.Symbols;

/// <summary>
/// 表示一个模块（lib 文件或主脚本），作为符号的命名集合。
/// </summary>
internal sealed class ModuleSymbol : Symbol
{
    public bool IsLib { get; }

    public ModuleSymbol(string name, bool isLib) : base(name)
    {
        IsLib = isLib;
    }

    public override string ToString() => IsLib ? $"Module(lib:{Name})" : $"Module(main:{Name})";
}