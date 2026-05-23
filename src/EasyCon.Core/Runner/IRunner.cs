using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

public interface IRunner
{
    abstract bool HasKeyAction { get; }

    ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames);
    ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames);
    void Run(IOutputAdapter output, ICGamePad pad, OcrDelegate? ocr, FrameDelegate? frameProvider, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}