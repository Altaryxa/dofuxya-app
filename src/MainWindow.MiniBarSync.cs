using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace DofuxyaManager
{
    // Mini-barre flottante : contenu, orientation et position.
    internal partial class MainWindow
    {
        // Affiche, met a jour ou ferme la mini-barre selon le reglage.
        private void SyncMiniBar()
        {
            try
            {
                if (!settings.MiniBar)
                {
                    if (miniBar != null)
                    {
                        miniBar.Close();
                        miniBar = null;
                    }
                    return;
                }
                List<GameWindow> ordered = NamedWindows();
                if (miniBar == null)
                {
                    miniBar = new MiniBar(GoTo, SaveMiniBarPosition, ToggleMiniBarOrientation);
                    Rect screen = SystemParameters.WorkArea;
                    bool saved = !double.IsNaN(settings.MiniX) && !double.IsNaN(settings.MiniY)
                        && settings.MiniX >= SystemParameters.VirtualScreenLeft
                        && settings.MiniY >= SystemParameters.VirtualScreenTop
                        && settings.MiniX < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
                        && settings.MiniY < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
                    miniBar.Left = saved ? settings.MiniX : screen.Left + (screen.Width - 440) / 2;
                    miniBar.Top = saved ? settings.MiniY : screen.Top + 8;
                    miniBar.SetVertical(settings.MiniVertical);
                    miniBar.Update(ordered, lastActive, icons);
                    miniBar.Show();
                }
                else
                {
                    miniBar.SetVertical(settings.MiniVertical);
                    miniBar.Update(ordered, lastActive, icons);
                }
            }
            catch (Exception ex)
            {
                lastMessage = "Mini-barre : " + ex.Message;
            }
        }

        private void ToggleMiniBarOrientation()
        {
            settings.MiniVertical = !settings.MiniVertical;
            settings.Save();
            if (miniVerticalToggle != null) miniVerticalToggle.IsChecked = settings.MiniVertical;
            SyncMiniBar();
        }

        private void SaveMiniBarPosition()
        {
            if (miniBar == null) return;
            settings.MiniX = miniBar.Left;
            settings.MiniY = miniBar.Top;
            settings.Save();
        }
    }
}
