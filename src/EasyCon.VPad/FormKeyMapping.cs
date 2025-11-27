using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace EasyVPad
{
    public partial class FormKeyMapping : Form
    {
        readonly Dictionary<CheckBox, string> _dict = new();

        public KeyMapping KeyMapping { get; set; }

        public FormKeyMapping(KeyMapping mapping)
        {
            InitializeComponent();

            KeyMapping = mapping;

            _dict[checkBoxA] = "A";
            _dict[checkBoxB] = "B";
            _dict[checkBoxX] = "X";
            _dict[checkBoxY] = "Y";
            _dict[checkBoxL] = "L";
            _dict[checkBoxR] = "R";
            _dict[checkBoxZL] = "ZL";
            _dict[checkBoxZR] = "ZR";
            _dict[checkBoxPlus] = "Plus";
            _dict[checkBoxMinus] = "Minus";
            _dict[checkBoxCapture] = "Capture";
            _dict[checkBoxHome] = "Home";
            _dict[checkBoxLClick] = "LClick";
            _dict[checkBoxRClick] = "RClick";
            _dict[checkBoxUp] = "Up";
            _dict[checkBoxDown] = "Down";
            _dict[checkBoxLeft] = "Left";
            _dict[checkBoxRight] = "Right";
            _dict[checkBoxUpLeft] = "UpLeft";
            _dict[checkBoxDownLeft] = "DownLeft";
            _dict[checkBoxUpRight] = "UpRight";
            _dict[checkBoxDownRight] = "DownRight";
            _dict[checkBoxLSUp] = "LSUp";
            _dict[checkBoxLSDown] = "LSDown";
            _dict[checkBoxLSLeft] = "LSLeft";
            _dict[checkBoxLSRight] = "LSRight";
            _dict[checkBoxRSUp] = "RSUp";
            _dict[checkBoxRSDown] = "RSDown";
            _dict[checkBoxRSLeft] = "RSLeft";
            _dict[checkBoxRSRight] = "RSRight";
            foreach (var box in _dict.Keys)
                box.CheckedChanged += (s, e) => Check(s as CheckBox);
        }

        private void FormKeyMapping_Activated(object sender, EventArgs e)
        {
            foreach (var pair in _dict)
            {
                var key = (Keys)(typeof(KeyMapping).GetProperty(pair.Value).GetValue(KeyMapping));
                SetName(pair.Key, key);
            }
        }

        CheckBox? _currentBox = null;
        bool _changing = false;
        void Check(CheckBox? checkBox)
        {
            if (_changing)
                return;
            _currentBox = checkBox;
            _changing = true;
            foreach (var box in _dict.Keys)
                box.Checked = box == checkBox;
            _changing = false;
        }

        void SetKey(Keys key)
        {
            if (_currentBox != null)
            {
                if (key == Keys.Escape)
                    key = Keys.None;
                object obj = KeyMapping;
                typeof(KeyMapping).GetProperty(_dict[_currentBox]).SetValue(obj, key);
                KeyMapping = (KeyMapping)obj;
                SetName(_currentBox, key);
                Check(null);
            }
        }

        void SetName(CheckBox checkBox, Keys key)
        {
            checkBox.Text = key == Keys.None ? "" : key.GetName();
        }

        private void FormKeyMapping_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            e.IsInputKey = true;
            SetKey(e.KeyCode);
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void FormKeyMapping_KeyDown(object sender, KeyEventArgs e)
        {
            SetKey(e.KeyCode);
        }
    }

    static class Extensions
    {
        public static string GetName(this Keys self)
        {
            return Enum.GetName(typeof(Keys), self) ?? "";
        }
    }
}
