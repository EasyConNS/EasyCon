using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using PokemonTycoon.Scripts;
using PTDevice;
using System.Text.RegularExpressions;
using System.Media;
using PokemonTycoon.Graphic;
using System.ComponentModel;
using Microsoft.WindowsAPICodePack.Taskbar;

namespace PokemonTycoon
{
    public partial class MainForm : Form
    {
        internal class TabScripting : TabModule, IScriptOutput
        {
            MainForm form;

            public Thread ScriptThread { get; private set; }
            public Script Script { get; private set; }
            public Color CurrentLight { get; private set; } = Color.White;
            public TaskbarProgressBarState CurrentTaskbarState { get; private set; } = TaskbarProgressBarState.NoProgress;
            Queue<string> _logs = new Queue<string>();
            Queue<Tuple<RichTextBox, object, Color?>> _messages = new Queue<Tuple<RichTextBox, object, Color?>>();
            Dictionary<RichTextBox, bool> _messageNewLine = new Dictionary<RichTextBox, bool>();
            int _logDisabled = 0;
            public bool LogEnabled
            {
                get => _logDisabled == 0;
                set => _logDisabled += value ? -1 : 1;
            }
            public NintendoSwitch NS => form.NS;
            const string PresetPath = @"Data\Presets.txt";
            List<Tuple<string, string>> _presets;
            CheckBox[] _checkboxRaidStars;

            class TurboKey
            {
                public string Name;
                public NintendoSwitch.Key Key;

                public override string ToString()
                {
                    return Name;
                }
            }

            public TabScripting(MainForm form)
            {
                this.form = form;
            }

            public override void Init()
            {
                _checkboxRaidStars = new CheckBox[] { form.checkBoxScriptRaidS1, form.checkBoxScriptRaidS2, form.checkBoxScriptRaidS3, form.checkBoxScriptRaidS4, form.checkBoxScriptRaidS5 };
                form.textBoxScriptRaidStartDate.Text = DateTime.Now.ToString("yyyy.MM.dd");

                // load turbo keys
                form.comboBoxScriptTurboKey.Items.Clear();
                foreach (var info in typeof(ScriptCore.Keys).GetFields())
                {
                    TurboKey key = new TurboKey();
                    key.Key = info.GetValue(null) as NintendoSwitch.Key;
                    key.Name = info.GetCustomAttribute<DescriptionAttribute>().Description;
                    form.comboBoxScriptTurboKey.Items.Add(key);
                }
                form.comboBoxScriptTurboKey.SelectedIndex = 0;

                // init fossil comboboxes
                form.comboBoxScriptFossilFirst.Items.Clear();
                form.comboBoxScriptFossilFirst.Items.Add("化石鸟");
                form.comboBoxScriptFossilFirst.Items.Add("化石鱼");
                form.comboBoxScriptFossilFirst.SelectedIndex = 0;
                form.comboBoxScriptFossilSecond.Items.Clear();
                form.comboBoxScriptFossilSecond.Items.Add("化石龙");
                form.comboBoxScriptFossilSecond.Items.Add("化石海兽");
                form.comboBoxScriptFossilSecond.SelectedIndex = 0;

                LoadPresets();
                UpdateRaidEndDate();
            }

            public override void Activate()
            {
                var list = Shadow.GetSavedShadows().Select(u => new MaxHunt.TargetPM(u.ID, u.Variance)).ToList();
                list.Add(new MaxHunt.TargetPM(0, '\0'));
                list.Sort();
                form.comboBoxScriptRaidTarget.Items.Clear();
                form.comboBoxScriptRaidTarget.Items.AddRange(list.ToArray());
            }

            public void UpdateUI()
            {
                while (true)
                {
                    string str;
                    lock (_logs)
                    {
                        if (_logs.Count > 0)
                            str = _logs.Dequeue();
                        else
                            break;
                    }
                    Logger.WriteLine(str);
                }
                form.Invoke((Action)delegate
                {
                    if (ScriptThread != null && ScriptThread.IsAlive && Script != null)
                    {
                        form.buttonScriptStop.Enabled = true;
                        form.labelTimer.Text = (DateTime.Now - Script.StartTime).ToString(@"hh\:mm\:ss");
                        form.richTextBoxSummary.Text = Script.Summary;
                        int p = ((int)(form.progressBar1.Maximum * Script.Progress)).Clamp(0, form.progressBar1.Maximum);
                        if (form.progressBar1.Value != p)
                        {
                            form.progressBar1.Maximum++;
                            form.progressBar1.Value = p + 1;
                            form.progressBar1.Maximum--;
                            form.progressBar1.Value = p;
                        }
                    }
                    else
                    {
                        form.buttonScriptStop.Enabled = false;
                        form.progressBar1.Value = 0;
                    }
                    // light
                    form.labelTestLight.BackColor = CurrentLight;
                    form.labelTimer.ForeColor = CurrentLight;
                    // taskbar
                    const int Max = 1000;
                    TaskbarManager.Instance.SetProgressState(CurrentTaskbarState, form.Handle);
                    if (CurrentTaskbarState == TaskbarProgressBarState.NoProgress)
                        TaskbarManager.Instance.SetProgressValue(0, 1, form.Handle);
                    else if (CurrentTaskbarState != TaskbarProgressBarState.Indeterminate)
                    {
                        if (Script != null)
                            TaskbarManager.Instance.SetProgressValue((int)(Script.Progress * Max), Max, form.Handle);
                        else
                            TaskbarManager.Instance.SetProgressValue(1, 1, form.Handle);
                    }
                    // message
                    HashSet<RichTextBox> boxes = new HashSet<RichTextBox>();
                    while (_messages.Count > 0)
                    {
                        Tuple<RichTextBox, object, Color?> tuple;
                        lock (_messages)
                        {
                            tuple = _messages.Dequeue();
                        }
                        var box = tuple.Item1;
                        var message = tuple.Item2;
                        var color = tuple.Item3;
                        if (!boxes.Contains(box))
                        {
                            box.SuspendLayout();
                            boxes.Add(box);
                        }
                        while (box.TextLength >= 1000000)
                        {
                            box.Select(0, box.GetFirstCharIndexFromLine(10));
                            box.ReadOnly = false;
                            box.SelectedText = string.Empty;
                            box.ReadOnly = true;
                        }
                        box.SelectionStart = box.TextLength;
                        box.SelectionLength = 0;
                        box.SelectionColor = color ?? box.ForeColor;
                        box.AppendText(message.ToString());
                    }
                    foreach (var box in boxes)
                    {
                        box.ScrollToCaret();
                        box.ResumeLayout();
                        box.Invalidate();
                    }
                });
            }

            void _PushMessage(RichTextBox box, object message, Color? color)
            {
                lock (_messages)
                {
                    if (!_messageNewLine.ContainsKey(box))
                        _messageNewLine[box] = true;
                    if (message == null)
                    {
                        _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(box, Environment.NewLine, null));
                        _messageNewLine[box] = true;
                        return;
                    }
                    if (_messageNewLine[box])
                    {
                        _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(box, DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray));
                        _messageNewLine[box] = false;
                    }
                    _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(box, message, color));
                }
            }

            public void Log(object message)
            {
                if (!LogEnabled)
                    return;
                lock (_logs)
                {
                    _logs.Enqueue($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] {message}");
                }
                _PushMessage(form.richTextBoxLog, message, null);
                _PushMessage(form.richTextBoxLog, null, null);
            }

            public void PushMessage(object message = null, Color? color = null)
            {
                _PushMessage(form.richTextBoxMessage, message, color);
            }

            public void Light(Color color)
            {
                CurrentLight = color;
            }

            public void Light(TaskbarProgressBarState color)
            {
                CurrentTaskbarState = color;
            }

            public void Run(Script script, Action run, bool disableController = true)
            {
                var t = Stop();
                if (disableController)
                    form.formController.ControllerEnabled = false;
                ScriptThread = new Thread(() =>
                {
                    t?.Join();
                    try
                    {
                        Log($"-- Script Start --");
                        PushMessage($"-- Script Start --", Color.Lime);
                        PushMessage();
                        Light(Color.Lime);
                        Light(TaskbarProgressBarState.Indeterminate);
                        Script = script;
                        Script.StartTime = DateTime.Now;
                        try
                        {
                            run.Invoke();
                        }
                        catch (ScriptDoneException)
                        { }
                        Log($"-- Script End --");
                        PushMessage($"-- Script End --", Color.Lime);
                        PushMessage();
                        Light(Color.White);
                        Light(TaskbarProgressBarState.NoProgress);
                        SystemSounds.Beep.Play();
                    }
                    catch (ScriptException ex)
                    {
                        Log(ex.Message);
                        PushMessage(ex.Message, Color.OrangeRed);
                        PushMessage();
                        var str = $"-- Script Failed --";
                        Log(str);
                        PushMessage(str, Color.OrangeRed);
                        PushMessage();
                        Light(Color.Orange);
                        Light(TaskbarProgressBarState.Error);
                        SystemSounds.Hand.Play();
                    }
                    catch (ThreadAbortException)
                    {
                        var str = $"-- Script Stop --";
                        Log(str);
                        PushMessage(str, Color.Orange);
                        PushMessage();
                        Light(Color.Orange);
                        Light(TaskbarProgressBarState.NoProgress);
                    }
                    catch (Exception ex)
                    {
                        Log($"{ex.GetType().Name}:");
                        Log(ex.Message);
                        PushMessage($"{ex.GetType().Name}:", Color.OrangeRed);
                        PushMessage();
                        PushMessage(ex.Message, Color.OrangeRed);
                        PushMessage();
                        var str = $"-- Error --";
                        Log(str);
                        PushMessage(str, Color.OrangeRed);
                        PushMessage();
                        Light(Color.OrangeRed);
                        Light(TaskbarProgressBarState.Error);
                        throw;
                    }
                });
                ScriptThread.IsBackground = true;
                ScriptThread.Start();
            }

            public Thread Stop()
            {
                var thread = ScriptThread;
                ScriptThread?.Abort();
                ScriptThread = null;
                return thread;
            }

            public bool IsRunning()
            {
                return ScriptThread != null && ScriptThread.IsAlive;
            }

            public bool Try(Action action)
            {
                if (IsRunning())
                {
                    Stop();
                    return false;
                }
                action.Invoke();
                return true;
            }

            void LoadPresets()
            {
                if (_presets == null)
                {
                    _presets = new List<Tuple<string, string>>();
                    if (File.Exists(PresetPath))
                    {
                        try
                        {
                            var lines = File.ReadAllLines(PresetPath);
                            string name = null;
                            List<string> preset = new List<string>();
                            Action save = () =>
                            {
                                if (name == null)
                                {
                                    if (preset.Count > 0)
                                        throw new FormatException();
                                    return;
                                }
                                if (preset.Count == 0)
                                    throw new FormatException();
                                _presets.Add(new Tuple<string, string>(name, string.Join(Environment.NewLine, preset)));
                                preset.Clear();
                            };
                            for (int i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i];
                                if (line.Length == 0 && i == lines.Length - 1)
                                    break;
                                var m = Regex.Match(line, @"^\s*\[([^\[\]]+)\]\s*$");
                                if (m.Success)
                                {
                                    save();
                                    name = m.Groups[1].Value;
                                    continue;
                                }
                                preset.Add(line);
                            }
                            save();
                        }
                        catch (FormatException)
                        {
                            MessageBox.Show("孵蛋预设文件格式错误！");
                        }
                    }
                }
                form.comboBoxScriptEggPreset.Items.Clear();
                form.comboBoxScriptEggPreset.Items.AddRange(_presets.Select(u => u.Item1).ToArray());
            }

            void SavePresets()
            {
                using (FileStream fs = new FileStream(PresetPath, FileMode.Create))
                {
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        foreach (var item in _presets)
                        {
                            writer.WriteLine($"[{item.Item1}]");
                            writer.WriteLine($"{item.Item2}");
                        }
                    }
                }
            }

            public void UsePreset(string name)
            {
                int index = _presets.FindIndex(u => u.Item1 == name);
                if (index == -1)
                    return;
                form.textBoxScriptEggFilter.Text = _presets[index].Item2;
            }

            public void SavePreset()
            {
                var name = form.comboBoxScriptEggPreset.Text;
                if (string.IsNullOrWhiteSpace(name))
                    return;
                int index = _presets.FindIndex(u => u.Item1 == name);
                if (index != -1 && MessageBox.Show("名称已存在，是否覆盖？", "", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;
                var preset = form.textBoxScriptEggFilter.Text;
                if (index != -1)
                    _presets[index] = new Tuple<string, string>(name, preset);
                else
                    _presets.Add(new Tuple<string, string>(name, preset));
                SavePresets();
                LoadPresets();
            }

            public void DeletePreset()
            {
                var name = form.comboBoxScriptEggPreset.Text;
                int index = _presets.FindIndex(u => u.Item1 == name);
                if (index == -1 || MessageBox.Show("确认删除预设？", "", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    return;
                _presets.RemoveAt(index);
                SavePresets();
                LoadPresets();
            }

            public void Test()
            {
                var script = Script.Create<Test>(this);
                Run(script, script.Run, false);
            }

            public void EggHatch()
            {
                int count;
                if (!int.TryParse(form.textBoxScriptEggMaxCount.Text, out count))
                {
                    MessageBox.Show("最大数量格式错误！");
                    return;
                }
                var filterStr = form.textBoxScriptEggFilter.Text;
                var shinyegg = form.checkBoxScriptEggShinyEgg.Checked;
                var massrelease = form.checkBoxScriptEggBatchRelease.Checked;
                var script = Script.Create<EggHatch>(this);
                Run(script, () => script.Run(filterStr, count, shinyegg, massrelease));
            }

            internal void Release()
            {
                if (MessageBox.Show("确定要放生吗？", "", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    return;
                int count = (int)form.numericUpDownScriptReleaseBoxCount.Value;
                string filterStr = form.checkBoxScriptReleaseUseFilter.Checked ? form.textBoxScriptEggFilter.Text : "false";
                var script = Script.Create<Release>(this);
                Run(script, () => script.Run(count, filterStr));
            }

            internal void UpdateRaidEndDate()
            {
                try
                {
                    var days = (int)form.numericUpDownScriptRaidDays.Value;
                    var s = form.textBoxScriptRaidStartDate.Text.Split('.');
                    var year = int.Parse(s[0]);
                    var month = int.Parse(s[1]);
                    var day = int.Parse(s[2]);
                    form.labelScriptRaidEndDate.Text = (new DateTime(year, month, day) + TimeSpan.FromDays(days)).ToString("yyyy.MM.dd");
                }
                catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentOutOfRangeException)
                {
                    form.labelScriptRaidEndDate.Text = "";
                }
            }

            internal MaxHunt.Target GetRaidTarget()
            {
                MaxHunt.Target target = new MaxHunt.Target(form.listBoxScriptRaidTarget.Items.Cast<MaxHunt.TargetPM>().ToList());
                int n = 0;
                bool nochecked = true;
                for (int i = 0; i < 5; i++)
                {
                    target.Stars[i + 1] = _checkboxRaidStars[i].Checked;
                    nochecked = nochecked && !target.Stars[i + 1];
                    n += _checkboxRaidStars[i].Checked ? 1 : 0;
                }
                if (nochecked)
                    for (int i = 0; i < 5; i++)
                        target.Stars[i + 1] = true;
                return target;
            }

            internal MaxHunt.DenArgs GetDenArgs()
            {
                var args = new MaxHunt.DenArgs();
                args.Target = GetRaidTarget();
                args.Days = (int)form.numericUpDownScriptRaidDays.Value;
                args.Auto = form.checkBoxScriptRaidAuto.Checked;
                args.AutoBattle = form.checkBoxScriptRaidAutoBattle.Checked;
                args.AutoCatch = form.checkBoxScriptRaidAutoBattleCatch.Checked;
                return args;
            }

            internal MaxHunt.RaidArgs GetRaidArgs()
            {
                var args = new MaxHunt.RaidArgs();
                if (string.IsNullOrEmpty(form.textBoxScriptRaidCode.Text))
                    args.Code = -1;
                else if (!int.TryParse(form.textBoxScriptRaidCode.Text, out args.Code))
                {
                    MessageBox.Show("密码格式错误！");
                    return null;
                }
                if (string.IsNullOrEmpty(form.textBoxScriptRaidCountdown.Text))
                    args.Countdown = 0;
                else if (!int.TryParse(form.textBoxScriptRaidCountdown.Text, out args.Countdown))
                {
                    MessageBox.Show("等待格式错误！");
                    return null;
                }
                if (string.IsNullOrEmpty(form.textBoxScriptRaidSoftResetCount.Text))
                    args.SoftResetCount = -1;
                else if (!int.TryParse(form.textBoxScriptRaidSoftResetCount.Text, out args.SoftResetCount))
                {
                    MessageBox.Show("软重置次数格式错误！");
                    return null;
                }
                if (!int.TryParse(form.textBoxScriptRaidHardResetDays.Text, out args.HardResetDays))
                {
                    MessageBox.Show("硬重置天数格式错误！");
                    return null;
                }
                args.Target = GetRaidTarget();
                args.AccountIndex = (int)form.numericUpDownScriptRaidAccountIndex.Value;
                args.Single = form.checkBoxScriptRaidSingle.Checked;
                args.Restart = form.checkBoxScriptRaidRestart.Checked;
                return args;
            }

            internal void RaidSearch()
            {
                var target = GetRaidTarget();
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunSearch(target));
            }

            internal void RaidWatt()
            {
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunSearch(null));
            }

            internal void RaidLeap()
            {
                var args = GetDenArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunLeap(args));
            }

            internal void RaidSkip(bool fast = false)
            {
                var args = GetDenArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunSkip(args, fast));
            }

            internal void RaidSkipFast()
            {
                RaidSkip(true);
            }

            internal void RaidNG()
            {
                var args = GetDenArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunNG(args));
            }

            internal void RaidRestartLeap()
            {
                var args = GetDenArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunRestartLeap(args));
            }

            internal void RaidAutoBattle()
            {
                var args = GetDenArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunAutoBattle(args));
            }

            internal void RaidTargetAdd()
            {
                var item = form.comboBoxScriptRaidTarget.SelectedItem;
                if (item != null && !form.listBoxScriptRaidTarget.Items.Contains(item))
                    form.listBoxScriptRaidTarget.Items.Add(item);
            }

            internal void RaidTargetRemove()
            {
                foreach (var item in form.listBoxScriptRaidTarget.SelectedItems.Cast<MaxHunt.TargetPM>().ToArray())
                    form.listBoxScriptRaidTarget.Items.Remove(item);
            }

            internal void RaidTargetClear()
            {
                form.listBoxScriptRaidTarget.Items.Clear();
            }

            internal void RaidGetRareDen()
            {
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunGetRareDen());
            }

            internal void RaidCreate()
            {
                var args = GetRaidArgs();
                if (args == null)
                    return;
                var script = Script.Create<MaxHunt>(this);
                Run(script, () => script.RunCreateRaid(args));
            }

            internal void TowerRush()
            {
                var script = Script.Create<TowerRush>(this);
                Run(script, () => script.Run());
            }

            internal void Turbo()
            {
                int duration;
                if (!int.TryParse(form.textBoxScriptTurboDuration.Text, out duration))
                {
                    MessageBox.Show("持续时间格式错误！");
                    return;
                }
                int interval;
                if (!int.TryParse(form.textBoxScriptTurboInterval.Text, out interval))
                {
                    MessageBox.Show("间隔时间格式错误！");
                    return;
                }
                var key = (form.comboBoxScriptTurboKey.SelectedItem as TurboKey).Key;
                var script = Script.Create<Turbo>(this);
                Run(script, () => script.Run(key, duration, interval), false);
            }

            internal void Digging()
            {
                var script = Script.Create<Digging>(this);
                Run(script, () => script.Run());
            }

            internal void Fossil()
            {
                int count;
                if (!int.TryParse(form.textBoxScriptFossilCount.Text, out count))
                {
                    MessageBox.Show("重置数量格式错误！");
                    return;
                }
                string filterStr = form.checkBoxScriptFossilUseFilter.Checked ? form.textBoxScriptEggFilter.Text : "false";
                int first = form.comboBoxScriptFossilFirst.SelectedIndex;
                int second = form.comboBoxScriptFossilSecond.SelectedIndex;
                bool doublecheck = form.checkBoxScriptFossilDoubleCheck.Checked;
                bool movetobox = form.checkBoxScriptFossilMoveToBox.Checked;
                var script = Script.Create<Fossil>(this);
                Run(script, () => script.Run(count, filterStr, first, second, doublecheck, movetobox));
            }
        }
    }
}
