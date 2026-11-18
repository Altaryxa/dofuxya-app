using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DofuxyaManager
{
    // Fonctions Windows utilisees par l'outil. Elles n'agissent que sur les fenetres
    // (titre, premier plan, barre des taches) : rien n'est envoye au jeu.
    internal static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_F3 = 0x72;
        public const uint GW_OWNER = 4;
        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_WIN = 0x0008;
        public const uint WM_SETICON = 0x0080;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeoutMs, out IntPtr result);

        // Pose une icone sur une fenetre (barre de titre, barre des taches, Alt+Tab).
        public static void SetWindowIcon(IntPtr hWnd, IntPtr bigIcon, IntPtr smallIcon)
        {
            IntPtr ignored;
            if (bigIcon != IntPtr.Zero)
            {
                SendMessageTimeout(hWnd, WM_SETICON, new IntPtr(1), bigIcon, SMTO_ABORTIFHUNG, 200, out ignored);
            }
            if (smallIcon != IntPtr.Zero)
            {
                SendMessageTimeout(hWnd, WM_SETICON, new IntPtr(0), smallIcon, SMTO_ABORTIFHUNG, 200, out ignored);
            }
        }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint command);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;

        // Vrai si la fenetre est en plein ecran : sans barre de titre et couvrant tout son ecran.
        // Une fenetre simplement agrandie garde sa barre de titre et ne compte pas.
        public static bool IsFullscreen(IntPtr hWnd)
        {
            if (IsIconic(hWnd)) return false;
            if ((GetWindowLong(hWnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;
            RECT window;
            if (!GetWindowRect(hWnd, out window)) return false;
            IntPtr monitor = MonitorFromWindow(hWnd, 2);
            if (monitor == IntPtr.Zero) return false;
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;
            return window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top
                && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO info);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

        [DllImport("user32.dll")]
        public static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        // Arrete le clignotement orange du bouton de la barre des taches.
        public static void StopFlash(IntPtr hWnd)
        {
            try
            {
                FLASHWINFO info = new FLASHWINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                info.hwnd = hWnd;
                info.dwFlags = 0;
                info.uCount = 0;
                info.dwTimeout = 0;
                FlashWindowEx(ref info);
            }
            catch (Exception)
            {
            }
        }

        // Amene une fenetre au premier plan, sans fabriquer d'appui clavier ni de clic.
        // Windows refuse parfois la demande et fait clignoter le bouton en orange : on se rattache
        // donc d'abord a la fenetre active, qui a le droit de ceder sa place, avant de demander.
        public static bool Activate(IntPtr hWnd)
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            IntPtr foreground = GetForegroundWindow();
            if (foreground == hWnd) return true;

            uint ignored;
            uint currentThread = GetCurrentThreadId();
            uint foregroundThread = foreground != IntPtr.Zero ? GetWindowThreadProcessId(foreground, out ignored) : 0;
            bool attached = false;
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }
            BringWindowToTop(hWnd);
            bool ok = SetForegroundWindow(hWnd);
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
            if (!ok || GetForegroundWindow() != hWnd)
            {
                try
                {
                    SwitchToThisWindow(hWnd, true);
                }
                catch (Exception)
                {
                }
            }
            StopFlash(hWnd);
            return ok || GetForegroundWindow() == hWnd;
        }
    }

    [ComImport]
    [Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList2
    {
        void HrInit();
        void AddTab(IntPtr hWnd);
        void DeleteTab(IntPtr hWnd);
        void ActivateTab(IntPtr hWnd);
        void SetActiveAlt(IntPtr hWnd);
        // Signale a la barre des taches qu'une fenetre est en plein ecran : elle passe alors dessous.
        void MarkFullscreenWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    internal class TaskbarListClass
    {
    }
}
