using EasyScript;

namespace EasyCon.Script.Symbols;

/// <summary>
/// 用于在 SsaValue.Aux 中存储 GamePadKey 的适配符号。
/// </summary>
public sealed class GamePadKeySymbol(GamePadKey key) : Symbol(key.ToString())
{
    public readonly GamePadKey Key = key;
}

/// <summary>
/// 用于在 SsaValue.Aux 中存储运行时值名称的适配符号。
/// </summary>
public sealed class RuntimeValueNameSymbol(string name) : Symbol(name);