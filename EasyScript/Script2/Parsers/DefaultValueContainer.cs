namespace Compiler.Parsers;

class DefaultValueContainer<T>
{
    static DefaultValueContainer()
    {
        //if (typeof(T) == typeof(Lexeme))
        //{
        //    DefaultValue = (T)(object)Lexeme.CreateEmptyLexeme();
        //}
        //else
        {
            DefaultValue = default(T);
        }
    }

    public static readonly T DefaultValue;
}
