using NLua;
using NLua.Exceptions;
using System.Diagnostics;
using System.Text;

namespace EasyCon.Script.Runner;
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
            Debug.WriteLine(ex.Message, "luaScriptException");
        }
    }
}

internal class TestObj
{

    public void Msg(object txt, object? title = null)
    {
        if (title == null)
        {
            Debug.WriteLine(txt.ToString());
        }
        else
        {
            Debug.WriteLine(txt.ToString(), title.ToString());
        }
    }

    public static void StaticMsg(object txt)
    {
        Debug.WriteLine(txt.ToString(), "StaticMsg");
    }
}