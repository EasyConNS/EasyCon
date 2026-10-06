using EasyCon.Script;
using EasyCon.Script.Modules;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// M2 管线级联容错（SCRIPT_MODERNIZATION_PLAN.md）：模块失败不再快速终止整链——
/// 失败模块的错误、依赖者的级联拦截（ECX0402）、无关模块的正常编译，一次编译全部产出。
/// </summary>
[TestFixture]
public class PipelineCascadeTests
{
    [Test]
    public void FailedLib_ReportsCascade_ButCompilesIndependentLib()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsCascade_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            // bad（lib/ 顶层 = 自动加载包成员，根级库互见）：绑定错误；
            // ok（lib/sub/ 层级路径，IMPORT "sub/ok.ecs" 相对 lib/ 解析——非包成员，
            // 与 bad 无依赖边）；main 同时导入两者
            File.WriteAllText(Path.Combine(dir, "lib", "bad.ecs"),
                "FUNC bad():INT\n    RETURN $nope\nENDFUNC\n");
            Directory.CreateDirectory(Path.Combine(dir, "lib", "sub"));
            File.WriteAllText(Path.Combine(dir, "lib", "sub", "ok.ecs"),
                "FUNC ok():INT\n    RETURN 1\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "main.ecs"),
                "IMPORT \"bad.ecs\"\nIMPORT \"sub/ok.ecs\"\n$v = ok()\n");

            var project = ProjectCompiler.CompileProject(Path.Combine(dir, "main.ecs"),
                new CompileOptions { UseDiskCache = false, UseProcessCache = false });

            Assert.That(project.Success, Is.False, "bad 失败 → 项目整体失败");
            var codes = project.Diagnostics.Where(d => d.IsError).Select(d => d.Code).ToList();
            Assert.That(codes, Does.Contain(DiagnosticCodes.VariableNotFound), "bad 的绑定错误");
            Assert.That(codes, Does.Contain(DiagnosticCodes.DependencyFailed), "main 的级联拦截");
            Assert.That(project.Diagnostics.Count(d => d.Code == DiagnosticCodes.DependencyFailed),
                Is.EqualTo(1), "级联诊断仅一条（依赖者自身，无符号错误风暴）");
            Assert.That(codes, Has.None.EqualTo(DiagnosticCodes.FunctionNotFound),
                "不产出符号错误风暴（级联在绑定前拦截）");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Test]
    public void IndependentModules_BothCompile_WhenNoFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"EcsCascade_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib", "alpha.ecs"),
                "FUNC alpha():INT\n    RETURN 1\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "lib", "beta.ecs"),
                "FUNC beta():INT\n    RETURN 2\nENDFUNC\n");
            File.WriteAllText(Path.Combine(dir, "main.ecs"),
                "IMPORT \"alpha.ecs\"\nIMPORT \"beta.ecs\"\n$v = alpha() + beta()\n");

            var project = ProjectCompiler.CompileProject(Path.Combine(dir, "main.ecs"),
                new CompileOptions { UseDiskCache = false, UseProcessCache = false });

            Assert.That(project.Success, Is.True, string.Join("; ", project.Diagnostics));
            Assert.That(project.Image!.Functions.Select(f => f.Name), Does.Contain("$eval").Or.Contain("<main>"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
