using EasyScript;
using Python.Runtime;
using System.Text;

namespace EasyCon.Script.Runner;

public sealed class PyRunner : IRunner
{
    private string Code = string.Empty;
    public static void Init(string dllpath)
    {
        Runtime.PythonDLL = dllpath;
        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();
    }

    public bool HasKeyAction() => true;

    public void Init(string code, IEnumerable<ExternalVariable> extVars)
    {
        Code = code;
    }

    public void Run(IOutputAdapter output, ICGamePad pad, CancellationToken token)
    {
        // 使用Python全局解释器锁（GIL）确保线程安全。
        // Py.GIL()返回一个实现了IDisposable接口的GIL上下文，使用using语句可以确保在代码块执行完毕后正确释放GIL。
        using (Py.GIL())
        {
            using(var printscope = Py.CreateScope())
            {
                // Define a Python class and function to capture stdout
                printscope.Exec(@"
import sys
class NetConsole:
    def __init__(self, writeCallback):
        self.writeCallback = writeCallback
    def write(self, message):
        self.writeCallback(message)
    def flush(self):
        pass # Needed for Python 3 compatibility
def setConsoleOut(writeCallback):
    sys.stdout = NetConsole(writeCallback)
");
                // Define the C# callback action to process Python output
                var linebreak = false;
                var outbuf = new StringBuilder();
                var count = 0;
                void writeCallback(string message)
                {
                    outbuf.Append(message);
                    count++;
                    if(count == 2)
                    {
                        var outmsg = outbuf.ToString();
                        output.Print(outmsg.Trim(), !linebreak);
                        linebreak = !outmsg.EndsWith('\n');
                        outbuf.Clear();
                        count = 0;
                    }
                }
                // Get the Python function and call it with the C# callback
                dynamic setConsoleOutFn = printscope.Get("setConsoleOut");
                setConsoleOutFn((Action<string>)writeCallback);
            }

            // create a Python scope
            using var scope = Py.CreateScope();
            var nspad = new NSPad(pad);
            scope.Set("NS", nspad.ToPython());
            scope.Exec(Code);
        }
    }

    public void Stop()
    {
        PythonEngine.Shutdown();
    }

    public string ToCode() => Code;
}

class NSPad(ICGamePad pad)
{
    private readonly ICGamePad pd = pad;
    public void press(string key, int duration) => pd.ClickButtons(NSKeys.Get(key), duration);
    public void down(string key) => pd.PressButtons(NSKeys.Get(key));
    public void up(string key) => pd.ReleaseButtons(NSKeys.Get(key));

    public void ChangeAmiibo(uint index)=>pd.ChangeAmiibo(index);
}