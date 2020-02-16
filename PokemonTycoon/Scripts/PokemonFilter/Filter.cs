using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PokemonTycoon.Scripts.PokemonFilter
{
    abstract partial class Filter
    {
        public class Vars
        {
            public virtual int Count { get; set; }

            public readonly Dictionary<string, int> ExCount = new Dictionary<string, int>();
        }

        public Vars ScriptVars { get; private set; }

        public abstract Bool3VL Result { get; }

        public static Filter Parse(string str)
        {
            if (str == null)
                return new True();
            str = Regex.Replace(str, @"[\s\n\r\t]", "");
            var token = Expression.Parse(str);
            var filter = token.BuildFilter();
            return filter;
        }

        public virtual void Init(Vars vars = null)
        {
            ScriptVars = vars;
        }

        public abstract void Reset();

        public abstract bool Check(ScriptCore script, Pokebox.SummaryPage page);

        protected static bool Compare<T>(CompareOperator op, T v1, T v2)
            where T : IComparable<T>
        {
            switch (op)
            {
                case CompareOperator.Equal:
                    return v1.CompareTo(v2) == 0;
                case CompareOperator.NotEqual:
                    return v1.CompareTo(v2) != 0;
                case CompareOperator.LessThan:
                    return v1.CompareTo(v2) < 0;
                case CompareOperator.LessOrEqual:
                    return v1.CompareTo(v2) <= 0;
                case CompareOperator.GreaterThan:
                    return v1.CompareTo(v2) > 0;
                case CompareOperator.GreaterOrEqual:
                    return v1.CompareTo(v2) >= 0;
                default:
                    throw new Exception();
            }
        }

        public sealed override string ToString()
        {
            return ToString(true);
        }

        protected abstract string ToString(bool noBracket);
    }
}
