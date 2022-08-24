namespace Compiler;

public class CompilationErrorManager
{
    private CompilationErrorInfoCollection m_errorInfoStore;

    public CompilationErrorManager()
    {
        m_errorInfoStore = new CompilationErrorInfoCollection();
    }    

    public void DefineError(int id, CompilationStage stage, string messageTemplate)
    {
        DefineError(id, 0, stage, messageTemplate);
    }   

    public void DefineError(int id, int level, CompilationStage stage, string messageTemplate)
    {
        CodeContract.RequiresArgumentInRange(!m_errorInfoStore.Contains(id), "id", "Error id is duplicated");

        var errorInfo = new CompilationErrorInfo(id, level, stage, messageTemplate);
        m_errorInfoStore.Add(errorInfo);
    }

    public CompilationErrorInfo GetErrorInfo(int id)
    {
        return m_errorInfoStore[id];
    }

    public bool ContainsErrorDefinition(int id)
    {
        return m_errorInfoStore.Contains(id);
    }        

    public CompilationErrorList CreateErrorList()
    {
        return new CompilationErrorList(this);
    }
}