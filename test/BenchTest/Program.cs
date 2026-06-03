using System.Diagnostics;
using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Binding.Ssa;
using EasyCon.Script.Syntax;
using EasyScript;

var output = new MockOutput();

// Warmup
SyntaxTree.Parse("RETURN 1");

Console.WriteLine("=== Evaluate profiling ===\n");

var mainTree = SyntaxTree.Parse(File.ReadAllText("D:/repositories/ecstest/main.ecs"));

// First run - everything cold
var sw = Stopwatch.StartNew();
var comp = Compilation.Create(mainTree);
Console.WriteLine($"Compilation.Create: {sw.ElapsedMilliseconds}ms");

sw.Restart();
var compileResult = comp.Compile(null);
Console.WriteLine($"Compile: {sw.ElapsedMilliseconds}ms");

sw.Restart();
using (var evaluator = new SsaEvaluator(compileResult.Program!, CancellationToken.None) { IoAdapter = output })
{
    evaluator.Evaluate();
}
Console.WriteLine($"Evaluate: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Diagnostics: {compileResult.Diagnostics.Length}");

// Second run - with cache
Console.WriteLine("\n--- Second run ---");
sw.Restart();
var comp2 = Compilation.Create(mainTree);
Console.WriteLine($"Compilation.Create: {sw.ElapsedMilliseconds}ms");

sw.Restart();
var compileResult2 = comp2.Compile(null);
Console.WriteLine($"Compile: {sw.ElapsedMilliseconds}ms");

sw.Restart();
using (var evaluator2 = new SsaEvaluator(compileResult2.Program!, CancellationToken.None) { IoAdapter = output })
{
    evaluator2.Evaluate();
}
Console.WriteLine($"Evaluate: {sw.ElapsedMilliseconds}ms");

// 10 iterations
Console.WriteLine("\n--- 10 Evaluate iterations ---");
sw.Restart();
for (int i = 0; i < 10; i++)
{
    var c = Compilation.Create(mainTree);
    var cr = c.Compile(null);
    using var e = new SsaEvaluator(cr.Program!, CancellationToken.None) { IoAdapter = output };
    e.Evaluate();
}
Console.WriteLine($"10 iterations: {sw.ElapsedMilliseconds}ms  avg={sw.ElapsedMilliseconds/10}ms");

class MockOutput : IIoAdapter
{
    public void Print(string message, bool newline) { }
    public void Alert(string message) { }
    public string ReadLine() => "";
    public bool TryReadLine(out string line) { line = ""; return true; }
}
