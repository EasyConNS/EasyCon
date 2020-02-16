using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Drawing;
using System.ComponentModel;

namespace PokemonTycoon.Scripts.PokemonFilter
{
    abstract partial class Filter
    {
        protected enum CompareOperator
        {
            [Description("=")]
            Equal,
            [Description("!=")]
            NotEqual,
            [Description("<=")]
            LessOrEqual,
            [Description("<")]
            LessThan,
            [Description(">=")]
            GreaterOrEqual,
            [Description(">")]
            GreaterThan,
        }

        abstract class Checker : Filter
        {
            protected CompareOperator _Operator;
            protected string _Val;
            protected Bool3VL _Result;
            public override Bool3VL Result => _Result;

            protected abstract string InitChecker(string name, string val);

            static Dictionary<string, Type> _derivedTypes = new Dictionary<string, Type>();
            static Dictionary<Type, CompareOperator[]> _validOperators = new Dictionary<Type, CompareOperator[]>();

            static Checker()
            {
                foreach (var type in Utils.GetDerivedTypes(typeof(Checker)))
                {
                    var validNames = (Attribute.GetCustomAttribute(type, typeof(ValidNameAttribute)) as ValidNameAttribute).Names;
                    var validOperators = (Attribute.GetCustomAttribute(type, typeof(ValidOperatorAttribute)) as ValidOperatorAttribute)?.Operators;
                    foreach (var name in validNames)
                        _derivedTypes[name] = type;
                    _validOperators[type] = validOperators;
                }
            }

            public static Checker Create(string name, CompareOperator op = CompareOperator.Equal, string val = null)
            {
                Checker checker = null;
                name = name.ToLower();
                if (!_derivedTypes.ContainsKey(name))
                    throw new FormatException($"未知的变量名'{name}'");
                Type type = _derivedTypes[name];
                if (_validOperators.ContainsKey(type) && !_validOperators[type]?.Contains(op) == true)
                    throw new FormatException($"不支持的运算符'{op.GetDesc()}'");
                checker = Activator.CreateInstance(type) as Checker;
                checker._Operator = op;
                checker._Val = val;
                var errmsg = checker.InitChecker(name, val);
                if (errmsg != null)
                    throw new FormatException($"格式错误'{errmsg}'");
                return checker;
            }

            public override void Reset()
            {
                _Result = Bool3VL.Unknown;
            }

            protected override string ToString(bool noBracket)
            {
                var name = (Attribute.GetCustomAttribute(GetType(), typeof(ValidNameAttribute)) as ValidNameAttribute).Names[0];
                if (_Val == null)
                    return name;
                else
                    return $"{name} {_Operator.GetDesc()} {_Val}";
            }
        }

        [AttributeUsage(AttributeTargets.Class)]
        class ValidNameAttribute : Attribute
        {
            public string[] Names { get; private set; }

            public ValidNameAttribute(params string[] names)
            {
                Names = names;
            }
        }

        [AttributeUsage(AttributeTargets.Class)]
        class ValidOperatorAttribute : Attribute
        {
            public CompareOperator[] Operators { get; private set; }

            public ValidOperatorAttribute(params CompareOperator[] operators)
            {
                Operators = operators;
            }
        }

        [ValidName("true")]
        [ValidOperator(CompareOperator.Equal)]
        class True : Checker
        {
            public override Bool3VL Result => Bool3VL.True;

            public override void Reset()
            { }

            protected override string InitChecker(string name, string val)
            {
                return null;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                return false;
            }
        }

        [ValidName("false")]
        [ValidOperator(CompareOperator.Equal)]
        class False : Checker
        {
            public override Bool3VL Result => Bool3VL.False;

            public override void Reset()
            { }

            protected override string InitChecker(string name, string val)
            {
                return null;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                return false;
            }
        }

        [ValidName("iv")]
        [ValidOperator(CompareOperator.Equal, CompareOperator.GreaterThan, CompareOperator.GreaterOrEqual)]
        class IVChecker : Checker
        {
            const string StatAbbr = "habcds";

            List<int> _pattern = new List<int>();
            int _vcount = 0;
            static List<int> _cache;

            public override void Reset()
            {
                base.Reset();
                _cache = null;
            }

            protected override string InitChecker(string name, string val)
            {
                while (_pattern.Count < 6)
                    _pattern.Add(-1);
                val = val.ToLower();
                if (Regex.Match(val, @"^[-v0]{6}$").Success)
                {
                    for (int i = 0; i < 6; i++)
                        _pattern[i] = val[i] == 'v' ? 31 : val[i] == '0' ? 0 : -1;
                    return null;
                }
                for (int i = 0; i < val.Length; i++)
                {
                    char c1 = val[i];
                    i++;
                    if (i >= val.Length)
                        return val;
                    char c2 = val[i];
                    if (c2 == 'v')
                    {
                        int n = c1 - '0';
                        if (n < 0 || n > 6)
                            return val;
                        _vcount = n;
                    }
                    else if (StatAbbr.Contains(c2))
                    {
                        if (c1 == '-')
                            _pattern[StatAbbr.IndexOf(c2)] = -2;
                        else if (c1 == '0')
                            _pattern[StatAbbr.IndexOf(c2)] = 0;
                        else
                            return val;
                    }
                    else
                        return val;
                }
                return null;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                if (_Result.IsKnown)
                    return false;
                if (page != Pokebox.SummaryPage.IV)
                    return false;
                var ivs = _cache ?? script.Module<Pokebox>().GetIVs();
                _cache = ivs;
                bool good = true;
                bool better = false;
                int vc = 0;
                for (int i = 0; i < 6; i++)
                {
                    if (_pattern[i] != -2 && ivs[i] == 31)
                        vc++;
                    if (_pattern[i] == -2)
                        better = better || ivs[i] >= 0;
                    else if (_pattern[i] >= 0)
                        good = good && (ivs[i] == _pattern[i]);
                }
                good = good && vc >= _vcount;
                better = better || vc > _vcount;
                int r = good ? better ? 1 : 0 : -1;
                _Result = Compare(_Operator, r, 0);
                return true;
            }
        }

        [ValidName("gender", "g", ValMale, ValFemale)]
        [ValidOperator(CompareOperator.Equal, CompareOperator.NotEqual)]
        class GenderChecker : Checker
        {
            const string ValMale = "male";
            const string ValFemale = "female";
            const string ValNone = "none";

            Pokemon.Gender _gender;

            string TryParseGender(string str, out Pokemon.Gender gender)
            {
                switch (str)
                {
                    case ValMale:
                        gender = Pokemon.Gender.Male;
                        return null;
                    case ValFemale:
                        gender = Pokemon.Gender.Female;
                        return null;
                    case ValNone:
                        gender = Pokemon.Gender.None;
                        return null;
                    default:
                        gender = Pokemon.Gender.None;
                        return str;
                }
            }

            protected override string InitChecker(string name, string val)
            {
                val = val?.ToLower();
                if (val == null)
                    return TryParseGender(name, out _gender);
                else
                    return TryParseGender(val, out _gender);
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                if (_Result.IsKnown)
                    return false;
                if (page != Pokebox.SummaryPage.IV && page != Pokebox.SummaryPage.Status)
                    return false;
                _Result = script.Module<Pokebox>().GetGender() == _gender;
                if (_Operator == CompareOperator.NotEqual)
                    _Result = !_Result;
                return true;
            }

            protected override string ToString(bool noBracket)
            {
                return $"gender = {_gender.GetDesc()}";
            }
        }

        [ValidName("ability", "ab")]
        [ValidOperator(CompareOperator.Equal, CompareOperator.NotEqual)]
        class AbilityChecker : Checker
        {
            string _abilityName;
            Bitmap _abilityImage;

            protected override string InitChecker(string name, string val)
            {
                _abilityName = val;
                _abilityImage = Pokebox.GetAbilityImage(_abilityName);
                if (_abilityImage == null)
                    throw new FormatException($"未找到特性'{_abilityName}'！");
                return null;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                if (_Result.IsKnown)
                    return false;
                if (page != Pokebox.SummaryPage.Status)
                    return false;
                _Result = script.Module<Pokebox>().CheckAbility(_abilityImage);
                if (_Operator == CompareOperator.NotEqual)
                    _Result = !_Result;
                return true;
            }
        }

        [ValidName("count")]
        class CountChecker : Checker
        {
            int _targetCount;

            protected override string InitChecker(string name, string val)
            {
                if (!int.TryParse(val, out _targetCount))
                    return val;
                return null;
            }

            public override bool Check(ScriptCore script, Pokebox.SummaryPage page)
            {
                if (_Result.IsKnown)
                    return false;
                _Result = Compare(_Operator, ScriptVars.Count, _targetCount);
                return true;
            }
        }
    }
}
