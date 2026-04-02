using EasyScript;

namespace EasyCon.Core.Runner;

interface IRunner
{
    abstract bool HasKeyAction { get; }

    void Init(string code, IEnumerable<string> extVarNames, Dictionary<string, Func<int>> externalGetters);
    void Load(string fileName, IEnumerable<string> extVarNames, Dictionary<string, Func<int>> externalGetters);
    void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}