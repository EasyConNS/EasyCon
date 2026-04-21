using NLua;
using NLua.Exceptions;
using System.Diagnostics;
using System.Text;

namespace EasyCon.Core.Runner;

public sealed class LuaRunner
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

    public static void ExecuteFile(string filename)
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
            state.DoFile(filename);
        }
        catch (LuaScriptException ex)
        {
            Debug.WriteLine(ex.Message, "luaScriptException");
        }
    }
}

internal class TestObj
{

    public void Msg(string txt, string title = "")
    {
        if (title == "")
        {
            Debug.WriteLine(txt);
        }
        else
        {
            Debug.WriteLine(txt, title);
        }
    }

    public static void StaticMsg(string txt)
    {
        Debug.WriteLine(txt, "StaticMsg");
    }
}