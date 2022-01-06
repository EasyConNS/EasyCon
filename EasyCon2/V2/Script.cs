#if lua
using NLua;
using NLua.Exceptions;
using System.Text;

namespace EasyCon2
{
    internal class ScriptTest
    {
        public static void ExecuteScript(string code)
        {
            using var state = new Lua();
            state.State.Encoding = Encoding.UTF8;
            state.UseTraceback = true;
            state.DoString(@"import = function () end");

            // TODO
            var obj = new TestObj();
            state.RegisterFunction("msg", obj, obj.GetType().GetMethod("Msg"));
            state.RegisterFunction("static_msg", typeof(TestObj).GetMethod("StaticMsg"));

            try
            {
                state.DoString(code);
            }
            catch (LuaScriptException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
#endif