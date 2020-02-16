using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    class Compiler
    {
        List<Statement.Generator> _generators = new List<Statement.Generator>();
        Stack<Next> _nexts = new Stack<Next>();

        public Compiler()
        {
            var types = (from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                         from assemblyType in domainAssembly.GetTypes()
                         where assemblyType.IsSubclassOf(typeof(Statement))
                         select assemblyType).ToArray();
            foreach (var type in types)
            {
                var method = type.GetMethod("TryCompile");
                if (method != null)
                    _generators.Add(method.CreateDelegate(typeof(Statement.Generator)) as Statement.Generator);
            }
            // next
            _generators.Add(str =>
            {
                if (str.Equals("next", StringComparison.OrdinalIgnoreCase))
                {
                    if (_nexts.Count == 0)
                        throw new CompileException("找不到对应的For语句");
                    return _nexts.Pop();
                }
                return null;
            });
        }

        Statement CompileLine(string text)
        {
            Match m;
            string indent;
            string comment;
            m = Regex.Match(text, @"^(\s*)([^\s]?.*)$");
            indent = m.Groups[1].Value;
            text = m.Groups[2].Value;
            m = Regex.Match(text, @"(\s*#.*)$");
            if (m.Success)
            {
                comment = m.Groups[1].Value;
                text = text.Substring(0, text.Length - comment.Length);
            }
            else
            {
                comment = string.Empty;
                text = text.Trim();
            }
            try
            {
                foreach (var gen in _generators)
                {
                    var cmd = gen(text);
                    if (cmd != null)
                    {
                        cmd.Indent = indent;
                        cmd.Comment = comment;
                        return cmd;
                    }
                }
            }
            catch (OverflowException)
            {
                throw new CompileException("数值溢出");
            }
            throw new CompileException("格式错误");
        }

        public List<Statement> Compile(string text)
        {
            List<Statement> list = new List<Statement>();
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                try
                {
                    var cmd = CompileLine(lines[i]);
                    if (cmd != null)
                        list.Add(cmd);
                    if (cmd is ForLoop)
                        _nexts.Push((cmd as ForLoop).Next);
                }
                catch (CompileException ex)
                {
                    ex.Index = i;
                    throw;
                }
            }
            for (int i = 0; i < list.Count; i++)
                list[i].Address = i;
            if (_nexts.Count > 0)
                throw new CompileException("For语句需要Next结束", _nexts.Peek().Address);
            return list;
        }
    }

    public class CompileException : Exception
    {
        public int Index;

        public CompileException(string message, int index = -1)
            : base(message)
        {
            Index = index;
        }
    }
}
