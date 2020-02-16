using PTDevice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    public abstract class Script : ScriptCore, IScriptOutput
    {
        public new NintendoSwitch NS => base.NS;
        public virtual string Summary { get; protected set; }
        public virtual double Progress { get; set; } = 1;

        public static T Create<T>(IScriptOutput output)
            where T : Script, new()
        {
            T script = new T();
            script._output = output;
            return script;
        }

        protected void Done()
        {
            throw new ScriptDoneException();
        }
    }

    public class ScriptException : Exception
    {
        public ScriptException()
            : base("脚本未完成")
        { }

        public ScriptException(string message)
            : base(message)
        { }
    }

    public class ScriptDoneException : Exception
    {
        public ScriptDoneException()
        { }
    }
}
