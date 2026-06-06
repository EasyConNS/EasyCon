using EasyCon.Script;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Core.Runner;

public interface IRunner
{
    abstract bool HasKeyAction { get; }

    ImmutableArray<Diagnostic> Init(string code, ImmutableHashSet<string> extVarNames);
    ImmutableArray<Diagnostic> Load(string fileName, ImmutableHashSet<string> extVarNames);
    void Run(IIoAdapter output, ICGamePad pad, OcrDelegate? ocr, OcrInitDelegate? ocrInit, Func<int> ocrConf, FrameDelegate? frameProvider, RoiDelegate? roiProvider, LabelMatchDelegate? labelMatch, ImmutableHashSet<string>? labelNames, CancellationToken token);

    string ToCode();

    byte[] Assemble(bool auto = true);
}