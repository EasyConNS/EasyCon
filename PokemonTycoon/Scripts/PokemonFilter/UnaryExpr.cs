using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Media;

namespace PokemonTycoon.Scripts.PokemonFilter
{
    abstract partial class Filter
    {
        protected enum UnaryOperator
        {
            [Description("!")]
            Not,
            [Description("#")]
            ScreenShot,
            [Description("#!")]
            ScreenShotNot,
            [Description("$")]
            ExCount,
            [Description("$$")]
            ExCountDetect,
        }

        abstract class UnaryExpr : Filter
        {
            protected Filter _child;
            protected string _desc;

            public override Bool3VL Result => _child.Result;

            static Dictionary<UnaryOperator, Type> _derivedTypes = new Dictionary<UnaryOperator, Type>();

            static UnaryExpr()
            {
                foreach (var type in Utils.GetDerivedTypes(typeof(UnaryExpr)))
                {
                    var op = (Attribute.GetCustomAttribute(type, typeof(UnaryOperatorAttribute)) as UnaryOperatorAttribute).Operator;
                    _derivedTypes[op] = type;
                }
            }

            public UnaryExpr(Filter child, string desc)
            {
                _child = child;
                _desc = desc;
            }

            public override void Init(Vars vars = null)
            {
                ScriptVars = vars;
                _child.Init(vars);
            }

            public override void Reset()
            {
                _child.Reset();
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                return _child.Check(script, page);
            }

            public static UnaryExpr Create(UnaryOperator op, Filter child, string desc = "")
            {
                return Activator.CreateInstance(_derivedTypes[op], child, desc) as UnaryExpr;
            }

            protected override string ToString(bool noBracket = true)
            {
                var op = (Attribute.GetCustomAttribute(GetType(), typeof(UnaryOperatorAttribute)) as UnaryOperatorAttribute).Operator;
                return $"{op.GetDesc()}{_desc}({_child.ToString(true)})";
            }
        }

        [AttributeUsage(AttributeTargets.Class)]
        class UnaryOperatorAttribute : Attribute
        {
            public UnaryOperator Operator { get; private set; }

            public UnaryOperatorAttribute(UnaryOperator op)
            {
                Operator = op;
            }
        }

        [UnaryOperator(UnaryOperator.Not)]
        class Not : UnaryExpr
        {
            public override Bool3VL Result => !_child.Result;

            public Not(Filter child, string desc)
                : base(child, desc)
            { }
        }

        [UnaryOperator(UnaryOperator.ScreenShot)]
        class ScreenShot : UnaryExpr
        {
            public ScreenShot(Filter child, string desc)
                : base(child, desc)
            { }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                var b = base.Check(script, page);
                if (b && Result == Bool3VL.True)
                    script.ScreenShot("Filter True");
                return b;
            }
        }

        [UnaryOperator(UnaryOperator.ScreenShotNot)]
        class ScreenShotNot : UnaryExpr
        {
            public ScreenShotNot(Filter child, string desc)
                : base(child, desc)
            { }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                var b = base.Check(script, page);
                if (b && Result == Bool3VL.False)
                    script.ScreenShot("Filter False");
                return b;
            }
        }

        [UnaryOperator(UnaryOperator.ExCount)]
        class ExCount : UnaryExpr
        {
            public ExCount(Filter child, string desc)
                : base(child, desc)
            { }

            public override void Init(Vars vars = null)
            {
                base.Init(vars);
                ScriptVars.ExCount[_desc] = 0;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                var b = base.Check(script, page);
                if (b && Result == Bool3VL.True)
                    ScriptVars.ExCount[_desc]++;
                return b;
            }
        }

        [UnaryOperator(UnaryOperator.ExCountDetect)]
        class ExCountDetect : UnaryExpr
        {
            public ExCountDetect(Filter child, string desc)
                : base(child, desc)
            { }

            public override void Init(Vars vars = null)
            {
                base.Init(vars);
                ScriptVars.ExCount[_desc] = 0;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                var b = base.Check(script, page);
                if (b && Result == Bool3VL.True)
                {
                    ScriptVars.ExCount[_desc]++;
                    script.Light(ScriptCore.Taskbar.Progress);
                    SystemSounds.Hand.Play();
                }
                return b;
            }
        }
    }
}
