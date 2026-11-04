using EasyCon.Core.Runner;
using EasyCon.Script;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Tests.Support;
using System.Collections.Immutable;

namespace EasyCon.Tests;

/// <summary>
/// true/false 字面量（仅全小写）与数组初始化常量白名单（语法阶段限制）。
/// </summary>
[TestFixture]
public class BoolLiteralAndArrayConstTests
{
    private static (CompileResult CompileResult, Value Result, MockOutputAdapter Output) CompileAndEval(
        string code, ImmutableHashSet<string>? labelNames = null)
    {
        var output = new MockOutputAdapter();
        var result = Compilation.CompileSource(code, new CompileOptions
        {
            ExtVars = labelNames ?? ImmutableHashSet<string>.Empty,
            UseDiskCache = false,
            UseProcessCache = false,
        });
        if (result.Image == null)
            return (result, Value.Void, output);
        var value = EcxVm.Run(result.Image!, EcsTestHost.Capabilities(output),
            new CancellationTokenSource().Token, [], result.NativeSymbols);
        return (result, value, output);
    }

    private static Value EvalValue(string code, ImmutableHashSet<string>? labelNames = null)
        => CompileAndEval(code, labelNames).Result;

    #region 词法：true/false 仅全小写

    [TestCase("true", TokenType.TRUE)]
    [TestCase("false", TokenType.FALSE)]
    public void Lowercase_TrueFalse_Tokenize(string word, TokenType expected)
    {
        var tokens = SyntaxTree.ParseTokens(word);
        Assert.That(tokens[0].Type, Is.EqualTo(expected));
    }

    [TestCase("TRUE")]
    [TestCase("True")]
    [TestCase("FALSE")]
    [TestCase("False")]
    public void NonLowercase_TrueFalse_ReportsDedicatedDiagnostic(string word)
    {
        var tree = SyntaxTree.Parse($"$v = {word}");
        Assert.That(tree.Diagnostics.Any(d => d.Code == DiagnosticCodes.KeywordMustBeLowercase),
            $"'{word}' 应报 ECX0012");
    }

    #endregion

    #region 求值：布尔字面量

    [TestCase("RETURN true", true)]
    [TestCase("RETURN false", false)]
    public void Return_BoolLiteral(string code, bool expected)
    {
        Assert.That(EvalValue(code).AsBool(), Is.EqualTo(expected));
    }

    [Test]
    public void Bool_Variable()
    {
        Assert.That(EvalValue("$flag = true\nRETURN $flag").AsBool(), Is.True);
    }

    [Test]
    public void Bool_EqualityWithComparison()
    {
        Assert.That(EvalValue("RETURN true == (1 > 0)").AsBool(), Is.True);
        Assert.That(EvalValue("RETURN true == (1 > 2)").AsBool(), Is.False);
        Assert.That(EvalValue("RETURN true != false").AsBool(), Is.True);
    }

    [Test]
    public void Bool_LogicOperators()
    {
        Assert.That(EvalValue("RETURN true and true").AsBool(), Is.True);
        Assert.That(EvalValue("RETURN true and false").AsBool(), Is.False);
        Assert.That(EvalValue("RETURN true or false").AsBool(), Is.True);
        Assert.That(EvalValue("RETURN not false").AsBool(), Is.True);
    }

    [Test]
    public void Bool_InIfCondition()
    {
        Assert.That(EvalValue("""
            $r = 0
            IF true
                $r = 1
            ENDIF
            RETURN $r
            """).AsInt(), Is.EqualTo(1));
    }

    [Test]
    public void String_ConvertBool()
    {
        Assert.That(EvalValue("RETURN STRING(true)").AsString(), Is.EqualTo("true"));
        Assert.That(EvalValue("RETURN STRING(false)").AsString(), Is.EqualTo("false"));
    }

    #endregion

    #region 常量：编译期固定

    [Test]
    public void BoolConstant_Folds()
    {
        Assert.That(EvalValue("_FLAG = true\nRETURN _FLAG").AsBool(), Is.True);
        Assert.That(EvalValue("_FLAG = false\nRETURN _FLAG").AsBool(), Is.False);
    }

    [Test]
    public void BoolConstant_Equality()
    {
        Assert.That(EvalValue("_FLAG = true\nRETURN _FLAG == true").AsBool(), Is.True);
        Assert.That(EvalValue("_FLAG = true\nRETURN _FLAG == false").AsBool(), Is.False);
    }

    [Test]
    public void Constant_FromConstantExpression_Folds()
    {
        Assert.That(EvalValue("_BASE = 10\n_OFFSET = _BASE * 2 + 1\nRETURN _OFFSET").AsInt(), Is.EqualTo(21));
    }

    [Test]
    public void Constant_NonConstantRightSide_Rejected()
    {
        var (result, _, _) = CompileAndEval("""
            FUNC f()
                RETURN 1
            ENDFUNC
            _BAD = f()
            RETURN 0
            """);
        Assert.That(result.Image, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidConstantExpression));
    }

    #endregion

    #region 数组初始化：仅允许常量元素

    [Test]
    public void IntArray_Works()
    {
        Assert.That(EvalValue("$a = [1, 2, 3]\nRETURN $a[1]").AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void NegativeNumber_Element()
    {
        Assert.That(EvalValue("$a = [1, -2, 3]\nRETURN $a[1]").AsInt(), Is.EqualTo(-2));
        Assert.That(EvalValue("$a = [1.5, -2.5]\nRETURN $a[1]").AsDouble(), Is.EqualTo(-2.5).Within(1e-9));
    }

    [Test]
    public void DoubleArray_Works()
    {
        Assert.That(EvalValue("$a = [1.5, 2.5]\nRETURN $a[1]").AsDouble(), Is.EqualTo(2.5).Within(1e-9));
    }

    [Test]
    public void StringArray_Works()
    {
        Assert.That(EvalValue("$a = [\"alpha\", \"beta\"]\nRETURN $a[1]").AsString(), Is.EqualTo("beta"));
    }

    [Test]
    public void BoolArray_Works()
    {
        Assert.That(EvalValue("$a = [true, false]\nRETURN $a[0]").AsBool(), Is.True);
        Assert.That(EvalValue("$a = [true, false]\nRETURN $a[1]").AsBool(), Is.False);
    }

    [Test]
    public void ConstantRef_Element()
    {
        Assert.That(EvalValue("_MAX = 9\n$a = [_MAX, 2]\nRETURN $a[0]").AsInt(), Is.EqualTo(9));
    }

    [Test]
    public void LargeConstArray_TemplatePath()
    {
        // >8 元素走常量模板降级路径（BytecodeEncoder.ArrayTemplate）
        var elements = string.Join(", ", Enumerable.Range(0, 20));
        Assert.That(EvalValue($"$a = [{elements}]\nRETURN $a[19]").AsInt(), Is.EqualTo(19));
    }

    [Test]
    public void TypedArray_Annotation_StillWorks()
    {
        Assert.That(EvalValue("$a:INT[] = [7, 8]\nRETURN $a[0]").AsInt(), Is.EqualTo(7));
        Assert.That(EvalValue("$a = []STRING\nRETURN LEN($a)").AsInt(), Is.EqualTo(0));
    }

    // ---- 白名单拒绝 ----

    [Test]
    public void Variable_Element_Rejected()
    {
        var (result, _, _) = CompileAndEval("$x = 1\n$a = [$x]\nRETURN 0");
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ArrayConstantRequired));
    }

    [Test]
    public void Call_Element_Rejected()
    {
        var (result, _, _) = CompileAndEval("""
            FUNC f()
                RETURN 1
            ENDFUNC
            $a = [f()]
            RETURN 0
            """);
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ArrayConstantRequired));
    }

    [Test]
    public void Label_Element_Rejected()
    {
        var (result, _, _) = CompileAndEval("$a = [@tag]\nRETURN 0",
            ImmutableHashSet<string>.Empty.Add("tag"));
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ArrayConstantRequired));
    }

    [Test]
    public void RuntimeMagicConstant_Element_Rejected()
    {
        var (result, _, _) = CompileAndEval("$a = [__APP__]\nRETURN 0");
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ArrayConstantRequired));
    }

    [Test]
    public void BuiltinCall_Element_Rejected()
    {
        var (result, _, _) = CompileAndEval("$a = [RAND(3)]\nRETURN 0");
        Assert.That(result.Diagnostics.Any(d => d.Code == DiagnosticCodes.ArrayConstantRequired));
    }

    #endregion
}