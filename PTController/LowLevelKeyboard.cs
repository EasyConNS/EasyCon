using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PTController
{
    public class LowLevelKeyboard
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        LowLevelKeyboardProc _proc;
        IntPtr _hookID = IntPtr.Zero;
        Dictionary<int, bool> _keyDownResult = new Dictionary<int, bool>();
        Dictionary<int, Func<bool>> _keyDownHandlers = new Dictionary<int, Func<bool>>();
        Dictionary<int, Func<bool>> _keyUpHandlers = new Dictionary<int, Func<bool>>();

        static LowLevelKeyboard _instance;

        public static LowLevelKeyboard GetInstance()
        {
            if (_instance == null)
                _instance = new LowLevelKeyboard();
            return _instance;
        }

        LowLevelKeyboard()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        ~LowLevelKeyboard()
        {
            UnhookWindowsHookEx(_hookID);
        }

        IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                if (KeyEvent(Marshal.ReadInt32(lParam), true))
                    return (IntPtr)1;
            }
            else if (nCode >= 0 && (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
            {
                if (KeyEvent(Marshal.ReadInt32(lParam), false))
                    return (IntPtr)1;
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        bool KeyEvent(int vkCode, bool keyDown)
        {
            //Debug.WriteLine(((Keys)vkCode).GetName() + " " + keyDown);
            var dict = keyDown ? _keyDownHandlers : _keyUpHandlers;
            if (!dict.ContainsKey(vkCode))
                return false;
            if (keyDown && _keyDownResult.ContainsKey(vkCode))
                return _keyDownResult[vkCode];
            var b = dict[vkCode]?.Invoke() == true;
            if (keyDown)
                _keyDownResult[vkCode] = b;
            else
                _keyDownResult.Remove(vkCode);
            return b;
        }

        public void RegisterKeyEvent(int vkCode, Func<bool> keydown, Func<bool> keyup)
        {
            _keyDownHandlers[vkCode] = keydown;
            _keyUpHandlers[vkCode] = keyup;
        }

        public void UnregisterKeyEvent(int vkCode)
        {
            _keyDownHandlers.Remove(vkCode);
            _keyUpHandlers.Remove(vkCode);
        }

        public void UnregisterKeyEventAll()
        {
            _keyDownHandlers.Clear();
            _keyUpHandlers.Clear();
        }
    }
}
