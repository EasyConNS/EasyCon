using EasyScript.Assembly;
using EasyScript.Parsing;
using System.IO;
using System.Media;

namespace EasyCon2.Forms;

partial class EasyConForm
{

    private async Task<bool> GenerateFirmware()
    {
        if (!await ScriptCompile())
            return false;
        try
        {
            StatusShowLog("开始生成固件...");
            var bytes = _program.Assemble(烧录自动运行ToolStripMenuItem.Checked);
            File.WriteAllBytes("temp.bin", bytes);
            string hexStr;
            var filename = GetFirmwareName(GetSelectedBoard().CoreName);
            if (filename == null)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("未找到固件！请确认程序Firmware目录下是否有对应固件文件！");
                return false;
            }
            try
            {
                hexStr = File.ReadAllText(FirmwarePath + filename);
            }
            catch (Exception ex)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show($"固件读取失败！{ex}");
                return false;
            }
            hexStr = HexWriter.WriteHex(hexStr, bytes, GetSelectedBoard().DataSize, GetSelectedBoard().Version);
            string name = Path.GetFileNameWithoutExtension(textBoxScript.Document.FileName);
            filename = filename.Replace(".", "+Script.");
            File.WriteAllText(filename, hexStr);
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
            ScriptSelectLine(ex.Index);
            return false;
        }
        catch (ParseException ex)
        {
            StatusShowLog("固件生成失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("固件生成失败！" + ex.Message);
            ScriptSelectLine(ex.Index);
            return false;
        }
    }

    private bool CheckFirmwareVersion()
    {
        if (!SerialCheckConnect())
            return false;
        int ver = NS.GetVersion();
        if (ver < GetSelectedBoard().Version)
        {
            StatusShowLog("需要更新固件");
            SystemSounds.Hand.Play();
            MessageBox.Show("固件版本不符，请重新刷入" + FirmwarePath);
            return false;
        }
        return true;
    }

    private void FlashScript()
    {
        try
        {
            StatusShowLog("开始烧录...");
            var bytes = _program.Assemble(烧录自动运行ToolStripMenuItem.Checked);
            File.WriteAllBytes("temp.bin", bytes);
            if (bytes.Length > GetSelectedBoard().DataSize)
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
            MessageBox.Show($"烧录完毕！已使用存储空间({bytes.Length}/{GetSelectedBoard().DataSize})");
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
