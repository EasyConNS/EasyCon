using System.Diagnostics;
using EasyCon.Script;
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
var compileDiags = comp.Compile(null);
Console.WriteLine($"Compile: {sw.ElapsedMilliseconds}ms");

sw.Restart();
var result = comp.Evaluate(output, null, [], CancellationToken.None);
Console.WriteLine($"Evaluate: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Diagnostics: {result.Diagnostics.Length}");

// Second run - with cache
Console.WriteLine("\n--- Second run ---");
sw.Restart();
var comp2 = Compilation.Create(mainTree);
Console.WriteLine($"Compilation.Create: {sw.ElapsedMilliseconds}ms");

sw.Restart();
comp2.Compile(null);
Console.WriteLine($"Compile: {sw.ElapsedMilliseconds}ms");

sw.Restart();
var result2 = comp2.Evaluate(output, null, [], CancellationToken.None);
Console.WriteLine($"Evaluate: {sw.ElapsedMilliseconds}ms");

// 10 iterations
Console.WriteLine("\n--- 10 Evaluate iterations ---");
sw.Restart();
for (int i = 0; i < 10; i++)
{
    var c = Compilation.Create(mainTree);
    c.Evaluate(output, null, [], CancellationToken.None);
}
Console.WriteLine($"10 iterations: {sw.ElapsedMilliseconds}ms  avg={sw.ElapsedMilliseconds/10}ms");

class MockOutput : IOutputAdapter
{
    public void Print(string message, bool newline) { }
    public void Alert(string message) { }
}
