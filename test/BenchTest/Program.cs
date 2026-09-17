using System.Diagnostics;
using EasyCon.Core.Capabilities;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyScript;

var output = new MockOutput();
var caps = new CapabilitySet { Console = new ConsoleIoAdapter(output) };

Console.WriteLine("=== Compile + EcxVm profiling ===\n");

// 统一编译链路（docs/Pipeline.md）：CompileFile → EcxImage → EcxVm 桥
var mainPath = "D:/repositories/ecstest/main.ecs";

// First run - everything cold
var sw = Stopwatch.StartNew();
var result = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
Console.WriteLine($"CompileFile (cold): {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Diagnostics: {result.Diagnostics.Length}");

sw.Restart();
EcxVm.Run(result.Image!, caps, CancellationToken.None, [], result.NativeSymbols);
Console.WriteLine($"EcxVm.Run: {sw.ElapsedMilliseconds}ms");

// Second run
Console.WriteLine("\n--- Second run ---");
sw.Restart();
var result2 = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
Console.WriteLine($"CompileFile: {sw.ElapsedMilliseconds}ms");

sw.Restart();
EcxVm.Run(result2.Image!, caps, CancellationToken.None, [], result2.NativeSymbols);
Console.WriteLine($"EcxVm.Run: {sw.ElapsedMilliseconds}ms");

// 10 iterations
Console.WriteLine("\n--- 10 compile+run iterations ---");
sw.Restart();
for (int i = 0; i < 10; i++)
{
    var r = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
    EcxVm.Run(r.Image!, caps, CancellationToken.None, [], r.NativeSymbols);
}
Console.WriteLine($"10 iterations: {sw.ElapsedMilliseconds}ms  avg={sw.ElapsedMilliseconds / 10}ms");

class MockOutput : IIoAdapter
{
    public void Print(string message, bool newline) { }
    public void Alert(string message) { }
    public string ReadLine() => "";
    public bool TryReadLine(out string line) { line = ""; return true; }
}
