using System.Collections.ObjectModel;

namespace Compiler;

public class CompilationErrorInfoCollection : KeyedCollection<int, CompilationErrorInfo>
{

    protected override int GetKeyForItem(CompilationErrorInfo item)
    {
        return item.Id;
    }
}