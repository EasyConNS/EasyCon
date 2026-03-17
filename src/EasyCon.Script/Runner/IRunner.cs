using EasyScript;

namespace EasyCon.Script.Runner;

interface IRunner
{
    abstract bool HasKeyAction { get; }

    void Init(string code, IEnumerable<ExternalVariable> extVars);
    void Load(string fileName, IEnumerable<ExternalVariable> extVars);
    void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}