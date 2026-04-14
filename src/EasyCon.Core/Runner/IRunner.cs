using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

interface IRunner
{
    abstract bool HasKeyAction { get; }

    ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames);
    ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames);
    void Run(IOutputAdapter output, ICGamePad pad, Dictionary<string, Func<int>> externalGetters, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}