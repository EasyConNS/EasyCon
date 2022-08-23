namespace Compiler.Parsers.Combinators;

public enum ResultType
{
    Step,
    Stop
}

public abstract class Result<T>
{
    internal Result(ResultType type)
    {
        Type = type;
        ErrorCorrections = new List<ErrorCorrection>();
    }

    public List<ErrorCorrection> ErrorCorrections { get; private set; }
    public ResultType Type { get; private set; }
    public abstract T GetResult(ParserContext context);
}

public class ErrorCorrection { }