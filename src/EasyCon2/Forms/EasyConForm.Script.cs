using EasyCon2.Graphic;
using EasyCon2.Helper;
using EasyScript;
using EasyScript.Parsing;
using System.Media;

namespace EasyCon2.Forms;

partial class EasyConForm
{

    private readonly Scripter _program = new();
    private bool scriptCompiling = false;
    private bool scriptRunning = false;
    private Thread thd;

    private async Task<bool> ScriptCompile()
    {
        StatusShowLog("开始编译...");
        scriptCompiling = true;
        try
        {
            // 在这里根据图像处理窗口的情况，创建一个ExternalVariable的数组或List传给Parse函数
            // 每个ExternalVariable对应一个图像标签，name为名字，get为用来获取结果的函数，set暂时没有语句支持所以先省略                

            _program.Parse(textBoxScript.Text, captureVideo.LoadedLabels.
                Select(il => new ExternalVariable(il.name, () =>
                {
                    il.Search(out var md);
                    return (int)md;
                }))
                );
            textBoxScript.Text = _program.ToCode();
            textBoxScript.Select(0, 0);
            return true;
        }
        catch (ParseException ex)
        {
            SystemSounds.Hand.Play();
            StatusShowLog("编译失败");
            ScriptSelectLine(ex.Index);
            MessageBox.Show($"{ex.Message}: 行{ex.Index + 1}");
            return false;
        }
        finally
        {
            scriptCompiling = false;
            StatusShowLog("编译完成");
        }
    }

    private async void ScriptRun()
    {
        if (! await ScriptCompile())
            return;
        if (_program.HasKeyAction)
        {
            if (!SerialCheckConnect())
                return;
            if (!CheckFirmwareVersion())
                return;
            if (!NS.RemoteStop())
            {
                MessageBox.Show($"需要先停止烧录脚本运行，请点击<远程停止>按钮");
                return;
            }
        }

        thd = new Thread(_RunScript);
        thd?.Start();
    }

    private DateTime _startTime = DateTime.MinValue;

    private void _RunScript()
    {
        scriptRunning = true;
        try
        {
            _startTime = DateTime.Now;
            Invoke(delegate
            {
                virtController.ControllerEnabled = false;
                StatusShowLog("开始运行");
            });
            Print("-- 开始运行 --", Color.Lime);
            _program.Run(this, new GamePadAdapter(NS));
            Print("-- 运行结束 --", Color.Lime);
            StatusShowLog("运行结束");
            SystemSounds.Beep.Play();
        }
        catch (ThreadInterruptedException)
        {
            Print("-- 运行终止 --", Color.Orange);
            StatusShowLog("运行终止");
            SystemSounds.Beep.Play();
        }
        catch (AggregateException ex)
        {
            Invoke(delegate
            {
                string str = "";
                foreach (Exception ex1 in ex.InnerExceptions)
                {
                    if (ex1 is ParseException pex)
                    {
                        str += $"{pex.Message}: 行{pex.Index + 1}";
                        ScriptSelectLine(pex.Index);
                    }
                }
                SystemSounds.Hand.Play();
                MessageBox.Show(str);
                StatusShowLog(str);
            });
        }
        catch (ScriptException ex)
        {
            Print($"[L{ex.Address}] " + ex.Message, Color.OrangeRed);
            Print("-- 运行出错 --", Color.OrangeRed);
            StatusShowLog("运行出错");
            SystemSounds.Hand.Play();
        }
        catch (Exception exx)
        {
            Print(exx.Message, Color.OrangeRed);
            Print("-- 运行出错 --", Color.OrangeRed);
            StatusShowLog("运行出错");
            SystemSounds.Hand.Play();
        }
        finally
        {
            NS.Reset();
            _startTime = DateTime.MinValue;
            scriptRunning = false;
        }
    }

    private void ScriptStop()
    {
        thd?.Interrupt();
        scriptRunning = false;
        StatusShowLog("运行被终止");
        SystemSounds.Beep.Play();
    }

}
