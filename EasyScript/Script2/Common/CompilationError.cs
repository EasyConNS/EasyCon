using System.Diagnostics;

namespace Compiler;

[DebuggerDisplay("{ToString()}")]
public class CompilationError
{
    public CompilationError(CompilationErrorInfo errorInfo, SourceSpan errorPosition, string errorMessage)
    {
        Info = errorInfo;
        ErrorPosition = errorPosition;
        Message = errorMessage;
    }

    public CompilationErrorInfo Info { get; private set; }
    public string Message { get; private set; }
    public SourceSpan ErrorPosition { get; private set; }

    public override string ToString()
    {
        return String.Format("{0} : {1}  Line: {2} Column: {3}", Info.Id, Message, ErrorPosition.Line, ErrorPosition.Column);
    }
}