using EasyCon.Capture;
using EasyCon.Script;
using EasyCon2.Helper;
using EasyCon.Script.Assembly;
using EasyCon.Script.Parsing;
using OpenCvSharp.Extensions;
using System.IO;
using System.Media;

namespace EasyCon2.Forms;

delegate void ScriptStatuHandler(bool isRunning);

partial class EasyConForm
{
    private bool scriptCompiling = false;
    private bool scriptRunning = false;

    CancellationTokenSource cts = new();
    event ScriptStatuHandler ScriptRunningChanged;
    private async Task<bool> ScriptCompile()
    {
        StatusShowLog("开始编译...");
        scriptCompiling = true;
        try
        {
            // 在这里根据图像处理窗口的情况，创建一个ExternalVariable的数组或List传给Parse函数
            // 每个ExternalVariable对应一个图像标签，name为名字，get为用来获取结果的函数，set暂时没有语句支持所以先省略                

            _program.Parse(textEditor.Text, captureVideo.LoadedLabels.
                Select(il => new ExternalVariable(il.name, () =>
                {
                    using var ss = BitmapConverter.ToMat(captureVideo.GetImage());
                    il.Search(ss, out var md);
                    return (int)md;
                }))
                );
            textEditor.Text = _program.ToCode().Trim();
            scriptTitleLabel.Text = textEditor.IsModified ? $"{fileName}(已编辑)" : fileName;
            textEditor.Select(0, 0);
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
        if (!await ScriptCompile())
            return;
        if (_program.HasKeyAction)
        {
            if (!SerialCheckConnect())
                return;
            if (!CheckFwVersion())
                return;
            if (!NS.RemoteStop())
            {
                MessageBox.Show($"需要先停止烧录脚本运行，请点击<远程停止>按钮");
                return;
            }
        }

        virtController.ControllerEnabled = false;
        StatusShowLog("开始运行");

        scriptRunning = true;
        ScriptRunningChanged?.Invoke(true);

        cts = new();
        Task.Run(
            _RunScript
            );
    }

    private DateTime _startTime = DateTime.MinValue;

    private void _RunScript()
    {
        try
        {
            _startTime = DateTime.Now;

            logTxtBox.Print("-- 开始运行 --", Color.Lime);
            _program.Run(cts.Token, this, new GamePadAdapter(NS));
            logTxtBox.Print("-- 运行结束 --", Color.Lime);
            StatusShowLog("运行结束");
            //SystemSounds.Beep.Play();
        }
        catch (ThreadInterruptedException)
        {
            logTxtBox.Print("-- 运行终止 --", Color.Orange);
            StatusShowLog("运行终止");
            SystemSounds.Beep.Play();
        }
        catch (ScriptException ex)
        {
            logTxtBox.Print($"[L{ex.Address}]：{ex.Message}", Color.OrangeRed);
            logTxtBox.Print("-- 运行出错 --", Color.OrangeRed);
            StatusShowLog("运行出错");
            SystemSounds.Hand.Play();
        }
        catch (Exception exx)
        {
            logTxtBox.Print(exx.Message, Color.OrangeRed);
            logTxtBox.Print("-- 运行出错 --", Color.OrangeRed);
            StatusShowLog("运行出错");
            SystemSounds.Hand.Play();
        }
        finally
        {
            NS.Reset();
            _startTime = DateTime.MinValue;
            scriptRunning = false;
            ScriptRunningChanged?.Invoke(false);
        }
    }

    private void ScriptStop()
    {
        cts?.Cancel();
        scriptRunning = false;
        StatusShowLog("运行被终止");
        SystemSounds.Beep.Play();
    }

    private void ScriptReset()
    {
        if (scriptCompiling || scriptRunning)
            return;
        _program.Reset();
    }

    private bool CheckFwVersion()
    {
        if (NS.GetVersion() < Board.Version)
        {
            StatusShowLog("需要更新固件");
            SystemSounds.Hand.Play();
            MessageBox.Show("固件版本不符，请重新刷入" + FirmwarePath);
            return false;
        }
        return true;
    }

    private async Task<bool> GenerateFirmware(Board board)
    {
        if (!await ScriptCompile())
            return false;
        try
        {
            StatusShowLog("开始生成固件...");
            var bytes = _program.Assemble(烧录自动运行ToolStripMenuItem.Checked);
            var filename = board.GenerateFirmware(bytes);
            StatusShowLog("固件生成完毕");
            SystemSounds.Beep.Play();
            MessageBox.Show("固件生成完毕！已保存为" + Path.GetFileName(filename));
            return true;
        }
        catch (AssembleException ex)
        {
            StatusShowLog("固件生成失败");
            SystemSounds.Hand.Play();
            MessageBox.Show(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            StatusShowLog("固件生成失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("固件生成失败！" + ex.Message);
            return false;
        }
    }

    private void ScriptFlash(int maxSize = 0)
    {
        try
        {
            StatusShowLog("开始烧录...");
            var bytes = _program.Assemble(烧录自动运行ToolStripMenuItem.Checked);
            File.WriteAllBytes("temp.bin", bytes);
            if (bytes.Length > maxSize)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！长度超出限制");
                return;
            }
            if (!NS.Flash(bytes))
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！请检查设备连接后重试");
                return;
            }
            StatusShowLog("烧录完毕");
            SystemSounds.Beep.Play();
            MessageBox.Show($"烧录完毕！已使用存储空间({bytes.Length}/{maxSize})");
        }
        catch (AssembleException ex)
        {
            StatusShowLog("烧录失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("烧录失败！" + ex.Message);
            ScriptSelectLine(ex.Index);
            return;
        }
        catch (ParseException ex)
        {
            StatusShowLog("烧录失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("烧录失败！" + ex.Message);
            ScriptSelectLine(ex.Index);
            return;
        }
    }
}
