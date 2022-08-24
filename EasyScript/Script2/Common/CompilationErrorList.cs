using System.Collections;
using System.Collections.Generic;

namespace Compiler;

public class CompilationErrorList : IReadOnlyList<CompilationError>
{
    private CompilationErrorManager m_errorManager;
    private List<CompilationError> m_errors;

    public CompilationErrorList(CompilationErrorManager errorManager)
    {
        CodeContract.RequiresArgumentNotNull(errorManager, "errorManager");
        m_errors = new List<CompilationError>();
        m_errorManager = errorManager;
    }

    public CompilationError this[int index]
    {
        get { return m_errors[index]; }
    }

    public int Count
    {
        get { return m_errors.Count; }
    }

    public IEnumerator<CompilationError> GetEnumerator()
    {
        foreach (var item in m_errors)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void AddError(int id, SourceSpan errorPosition, params object[] args)
    {
        CodeContract.RequiresArgumentInRange(m_errorManager.ContainsErrorDefinition(id), "id", "Error id is invalid");

        var errorInfo = m_errorManager.GetErrorInfo(id);
        var errorMessage = String.Format(errorInfo.MessageTemplate, args);

        m_errors.Add(new CompilationError(errorInfo, errorPosition, errorMessage));
    }
}
