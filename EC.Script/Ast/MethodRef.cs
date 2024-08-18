using VBF.Compilers.Scanners;

namespace ECP.Ast;

public class MethodRef
{
    public MethodRef(LexemeValue name)
    {
        MethodName = name;
    }

    public LexemeValue MethodName { get; set; }
    public Method MethodInfo { get; set; }
}