using System.Linq.Expressions;

namespace Compiler.Parsers.Combinators;

class ConvertHelper<TFrom, TTo>
{
    private static Func<TFrom, TTo> s_castFunc;

    static ConvertHelper()
    {
        var source = Expression.Parameter(typeof(TFrom), "source");
        s_castFunc = Expression.Lambda<Func<TFrom, TTo>>(Expression.Convert(source, typeof(TTo)), source).Compile();
    }

    public static TTo Convert(TFrom source)
    {
        return s_castFunc(source);
    }
}