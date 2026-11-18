using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DofuxyaManager
{
    // Ecoute des boutons de souris (lateraux et molette) pour s'en servir comme raccourcis, et des
    // clics gauche et droit pour la roue des persos. L'ecoute ne fabrique aucun clic : elle observe, et
    // retient seulement le clic quand il sert a l'outil.
    internal class MouseHook
    {
        private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private readonly LowLevelProc callback;
        private readonly List<uint> swallowed = new List<uint>();
        private IntPtr hook = IntPtr.Zero;
        private bool swallowLeftUp;
        private bool swallowRightUp;

        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_ESCAPE = 0x1B;

        // Recoit le bouton (4 = molette, 5 et 6 = lateraux). Renvoie true si le bouton est un raccourci.
        public Func<uint, bool> Handler;

        // Recoivent chaque clic gauche ou droit (position a l'ecran, en pixels). Renvoient true pour
        // le retenir. Appeles pour tous les clics : ils doivent repondre tout de suite.
        public Func<int, int, bool> LeftHandler;
        public Func<int, int, bool> RightHandler;

        public static bool IsKeyDown(uint virtualKey)
        {
            return virtualKey != 0 && (GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;
        }

        public MouseHook()
        {
            callback = Proc;
        }

        public bool Install()
        {
            if (hook != IntPtr.Zero) return true;
            hook = SetWindowsHookEx(WH_MOUSE_LL, callback, GetModuleHandle(null), 0);
            return hook != IntPtr.Zero;
        }

        public void Uninstall()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        // Modificateurs actuellement enfonces, au format des raccourcis.
        public static uint CurrentMods()
        {
            uint mods = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= Native.MOD_CONTROL;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= Native.MOD_ALT;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= Native.MOD_SHIFT;
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)
            {
                mods |= Native.MOD_WIN;
            }
            return mods;
        }

        private IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    int message = wParam.ToInt32();
                    if (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN)
                    {
                        Func<int, int, bool> click = message == WM_LBUTTONDOWN ? LeftHandler : RightHandler;
                        if (click != null && click(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4)))
                        {
                            if (message == WM_LBUTTONDOWN) swallowLeftUp = true;
                            else swallowRightUp = true;
                            return new IntPtr(1);
                        }
                    }
                    else if (message == WM_LBUTTONUP && swallowLeftUp)
                    {
                        swallowLeftUp = false;
                        return new IntPtr(1);
                    }
                    else if (message == WM_RBUTTONUP && swallowRightUp)
                    {
                        swallowRightUp = false;
                        return new IntPtr(1);
                    }
                    uint button = 0;
                    bool down = false;
                    if (message == WM_XBUTTONDOWN || message == WM_XBUTTONUP)
                    {
                        int data = Marshal.ReadInt32(lParam, 8);
                        int which = (data >> 16) & 0xFFFF;
                        if (which == 1) button = 5;
                        else if (which == 2) button = 6;
                        down = message == WM_XBUTTONDOWN;
                    }
                    else if (message == WM_MBUTTONDOWN || message == WM_MBUTTONUP)
                    {
                        button = 4;
                        down = message == WM_MBUTTONDOWN;
                    }

                    if (button != 0)
                    {
                        if (down)
                        {
                            Func<uint, bool> handler = Handler;
                            if (handler != null && handler(button))
                            {
                                if (!swallowed.Contains(button)) swallowed.Add(button);
                                return new IntPtr(1);
                            }
                        }
                        else if (swallowed.Remove(button))
                        {
                            return new IntPtr(1);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }
    }

    // Previent quand la fenetre au premier plan change.
    internal class ForegroundWatcher
    {
        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild,
            uint thread, uint time);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
            WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private readonly WinEventProc callback;
        private IntPtr hook = IntPtr.Zero;

        public Action<IntPtr> Changed;

        public ForegroundWatcher()
        {
            callback = Proc;
        }

        public void Install()
        {
            if (hook != IntPtr.Zero) return;
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, 0);
        }

        public void Uninstall()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        private void Proc(IntPtr eventHook, uint eventType, IntPtr hWnd, int idObject, int idChild,
            uint thread, uint time)
        {
            try
            {
                Action<IntPtr> changed = Changed;
                if (changed != null) changed(hWnd);
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class Dwm
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        // Coins arrondis (Windows 11). Sans effet ailleurs.
        public static void RoundCorners(IntPtr hWnd)
        {
            try
            {
                int preference = 2;
                DwmSetWindowAttribute(hWnd, 33, ref preference, 4);
            }
            catch (Exception)
            {
            }
        }

        // Barre de titre sombre (Windows 10 recent et Windows 11). Sans effet ailleurs.
        public static void UseDarkTitleBar(IntPtr hWnd)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(hWnd, 20, ref enabled, 4) != 0)
                {
                    DwmSetWindowAttribute(hWnd, 19, ref enabled, 4);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
