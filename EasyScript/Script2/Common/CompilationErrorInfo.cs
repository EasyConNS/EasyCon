namespace Compiler;

public record CompilationErrorInfo
{
    public CompilationErrorInfo(int id, int level, CompilationStage stage, string messageTemplate)
    {
        Id = id;
        Level = level;
        Stage = stage;
        MessageTemplate = messageTemplate;
    }

    public int Id { get; private set; }
    public int Level { get; private set; }
    public CompilationStage Stage { get; private set; }
    public string MessageTemplate { get; set; }
}

public enum CompilationStage
{
    None,
    PreProcessing,
    Scanning,
    Parsing,
    SemanticAnalysis,
    CodeGeneration,
    PostProcessing,
    Other
}