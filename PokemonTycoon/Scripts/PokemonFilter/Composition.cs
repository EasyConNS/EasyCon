using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace PokemonTycoon.Scripts.PokemonFilter
{
    abstract partial class Filter
    {
        protected enum LogicalOperator
        {
            [Description("&")]
            And,
            [Description("|")]
            Or,
        }

        abstract class Composition : Filter
        {
            public abstract IEnumerable<Filter> GetChildren();

            public override void Init(Vars vars = null)
            {
                ScriptVars = vars;
                GetChildren()?.ForEach(u => u.Init(vars));
            }

            public override void Reset()
            {
                GetChildren()?.ForEach(u => u.Reset());
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                bool anyChanged = false;
                GetChildren()?.ForEach(u =>
                {
                    if (u.Check(script, page))
                        anyChanged = true;
                });
                if (anyChanged)
                    return GetChildren().All(u => u.Result.IsKnown);
                return false;
            }
        }

        class Logic : Composition
        {
            IEnumerable<Tuple<LogicalOperator, Filter>> _units;

            public override Bool3VL Result
            {
                get
                {
                    var b = Bool3VL.True;
                    foreach (var t in _units)
                    {
                        switch (t.Item1)
                        {
                            case LogicalOperator.And:
                                b &= t.Item2.Result;
                                break;
                            case LogicalOperator.Or:
                                b |= t.Item2.Result;
                                break;
                        }
                    }
                    return b;
                }
            }

            public Logic(IEnumerable<Tuple<LogicalOperator, Filter>> units)
            {
                _units = units;
            }

            public override IEnumerable<Filter> GetChildren()
            {
                return _units.Select(u => u.Item2);
            }

            protected override string ToString(bool noBracket)
            {
                StringBuilder s = new StringBuilder();
                int i = 0;
                foreach (var unit in _units)
                {
                    if (i > 0)
                    {
                        s.Append(' ');
                        s.Append(unit.Item1.GetDesc());
                        s.Append(' ');
                    }
                    s.Append(unit.Item2.ToString(false));
                    i++;
                }
                if (!noBracket && i > 1)
                    return $"({s})";
                return s.ToString();
            }
        }
    }
}
