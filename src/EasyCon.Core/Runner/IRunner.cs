using EasyScript;

namespace EasyCon.Core.Runner;

interface IRunner
{
    abstract bool HasKeyAction { get; }

    void Init(string code, IEnumerable<string> extVarNames);
    void Load(string fileName, IEnumerable<string> extVarNames);
    void Run(IOutputAdapter output, ICGamePad pad, Dictionary<string, Func<int>> externalGetters, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}