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
    // Windows : barre des taches, lancement au demarrage, zone de notification, diagnostic.
    internal partial class MainWindow
    {
        // ---------- Barre des tâches ----------

        // Range la barre des taches quand l'ordre des personnages ouverts change.
        private void AutoReorderTaskbar()
        {
            if (!settings.AutoTaskbar) return;
            List<GameWindow> ordered = NamedWindows();
            StringBuilder order = new StringBuilder();
            foreach (GameWindow window in ordered) order.Append(window.Handle.ToInt64()).Append(';');
            string signature = order.ToString();
            if (signature == lastTaskbarOrder) return;
            lastTaskbarOrder = signature;
            if (ordered.Count > 1) ReorderTaskbar(ordered);
        }

        // Retire puis remet les boutons des personnages, dans l'ordre du tour.
        private void ReorderTaskbar(List<GameWindow> ordered)
        {
            try
            {
                ITaskbarList2 taskbar = Taskbar();
                foreach (GameWindow window in ordered)
                {
                    taskbar.DeleteTab(window.Handle);
                }
                Thread.Sleep(120);
                foreach (GameWindow window in ordered)
                {
                    taskbar.AddTab(window.Handle);
                    // Un bouton remis dans la barre perd son reglage « plein ecran » : on le refait.
                    fullscreenMarked.Remove(window.Handle);
                    Thread.Sleep(40);
                }
            }
            catch (Exception ex)
            {
                lastMessage = "Rangement de la barre des tâches impossible : " + ex.Message;
            }
            UpdateFullscreenMarks();
        }

        private ITaskbarList2 Taskbar()
        {
            if (taskbarList == null)
            {
                ITaskbarList2 created = (ITaskbarList2)new TaskbarListClass();
                created.HrInit();
                taskbarList = created;
            }
            return taskbarList;
        }

        // Une fenetre dont le bouton a ete retire puis remis dans la barre des taches n'est plus
        // reconnue par Windows comme un jeu en plein ecran : la barre reste alors visible par-dessus.
        // On signale donc nous-memes les fenetres de jeu en plein ecran (toutes, car celles rangees
        // avant de decocher l'option gardent le probleme jusqu'a leur fermeture).
        private void UpdateFullscreenMarks()
        {
            try
            {
                HashSet<IntPtr> alive = new HashSet<IntPtr>();
                foreach (GameWindow window in windows)
                {
                    IntPtr handle = window.Handle;
                    alive.Add(handle);
                    bool fullscreen = Native.IsFullscreen(handle);
                    if (fullscreen == fullscreenMarked.Contains(handle)) continue;
                    Taskbar().MarkFullscreenWindow(handle, fullscreen);
                    if (fullscreen) fullscreenMarked.Add(handle);
                    else fullscreenMarked.Remove(handle);
                }
                fullscreenMarked.RemoveWhere(delegate(IntPtr handle) { return !alive.Contains(handle); });
            }
            catch (Exception ex)
            {
                lastMessage = "Réglage du plein écran impossible : " + ex.Message;
            }
        }

        // ---------- Lancement avec Windows ----------

        // Supprime l'entree de demarrage des anciennes versions. Renvoie vrai si elle existait.
        private static bool MigrateOldAutoStart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null || key.GetValue(OldRunName) == null) return false;
                    key.DeleteValue(OldRunName, false);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key != null && key.GetValue(RunName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exe = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunName, "\"" + exe + "\" --reduit");
                        lastMessage = "Dofuxya se lancera avec Windows, réduit dans la zone de notification.";
                    }
                    else
                    {
                        key.DeleteValue(RunName, false);
                        lastMessage = "Dofuxya ne se lancera plus avec Windows.";
                    }
                }
            }
            catch (Exception ex)
            {
                lastMessage = "Réglage du lancement avec Windows impossible : " + ex.Message;
            }
        }

        // ---------- Zone de notification ----------

        private void CreateTray()
        {
            try
            {
                tray = new System.Windows.Forms.NotifyIcon();
                tray.Text = "Dofuxya";
                System.Drawing.Icon icon = null;
                try
                {
                    icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                }
                catch (Exception)
                {
                }
                tray.Icon = icon != null ? icon : System.Drawing.SystemIcons.Application;

                System.Windows.Forms.ContextMenu menu = new System.Windows.Forms.ContextMenu();
                menu.MenuItems.Add("À propos…", delegate { ShowAbout(); });
                menu.MenuItems.Add("Rafraîchir / trier", delegate { RefreshAll(true); });
                menu.MenuItems.Add("Configurer", delegate { ShowMain(); });
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Quitter", delegate { Quit(); });
                tray.ContextMenu = menu;
                tray.DoubleClick += delegate { ShowMain(); };
                tray.Visible = true;
            }
            catch (Exception)
            {
                tray = null;
            }
        }

        private void ShowMain()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // A propos. Le diagnostic (utile pour signaler un probleme) y est propose, plutot qu'en bouton.
        private void ShowAbout()
        {
            MessageBoxResult answer = MessageBox.Show(
                "Dofuxya " + Version + "\n\n"
                + "Organise les fenêtres Dofus 3 et Dofus Retro : ordre d'initiative, titres, icônes, raccourcis.\n"
                + "L'outil n'agit que sur les fenêtres : il n'envoie aucune touche ni aucun clic au jeu.\n\n"
                + "Outil de fan, non affilié à Ankama. Dofus est une marque d'Ankama.\n\n"
                + "Un problème ? « Oui » copie un diagnostic à joindre à ton signalement.",
                "À propos de Dofuxya", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) CopyDiagnostic();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (quitting || tray == null)
            {
                Shutdown();
                return;
            }
            // La croix masque la fenetre : l'outil reste actif dans la zone de notification.
            e.Cancel = true;
            CancelCapture();
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                try
                {
                    tray.ShowBalloonTip(4000, "Dofuxya",
                        "Toujours actif ici. Clic droit sur l'icône pour configurer ou quitter.",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                catch (Exception)
                {
                }
            }
        }

        private void Quit()
        {
            quitting = true;
            Close();
        }

        private void Shutdown()
        {
            timer.Stop();
            positionTimer.Stop();
            SaveWindowPosition();
            if (wheel != null)
            {
                wheel.CloseWheel();
                wheel.Close();
                wheel = null;
            }
            mouseHook.Uninstall();
            foregroundWatcher.Uninstall();
            turnWatcher.StateChanged = null;
            turnWatcher.Stop();
            scanTimer.Stop();
            if (handle != IntPtr.Zero)
            {
                try
                {
                    Native.DeregisterShellHookWindow(handle);
                }
                catch (Exception)
                {
                }
            }
            if (miniBar != null)
            {
                try
                {
                    miniBar.Close();
                }
                catch (Exception)
                {
                }
                miniBar = null;
            }
            if (handle != IntPtr.Zero)
            {
                foreach (int id in registered.Keys)
                {
                    Native.UnregisterHotKey(handle, id);
                }
            }
            registered.Clear();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            Application.Current.Shutdown();
        }

        // ---------- Diagnostic ----------

        private void CopyDiagnostic()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Dofuxya " + Version + " - diagnostic");
            text.AppendLine("Windows : " + Environment.OSVersion.VersionString);
            text.AppendLine("Mise à jour : " + Updater.State
                + " · fichier : " + Updater.ManifestUrl);
            text.AppendLine("Suivi du tour : " + (settings.AutoFollow ? "activé" : "désactivé") + " · état : "
                + turnWatcher.State + " · clignotements reçus : " + flashCount);
            text.AppendLine("Dernières notifications Dofus :");
            foreach (string line in recentNotifications) text.AppendLine("  " + line);
            text.AppendLine("Portée : " + (settings.OnlyInDofus ? "Dofus seulement" : "partout")
                + " · clavier actif : " + (keyboardActive ? "oui" : "non") + " · " + refusedText);
            foreach (Shortcut shortcut in shortcuts)
            {
                text.AppendLine("  raccourci " + shortcut.Hotkey + " -> " + shortcut.Label);
            }
            text.AppendLine("Roue des persos : " + (settings.WheelKey.IsEmpty ? "désactivée"
                : settings.WheelKey + " + clic droit"));
            text.AppendLine("Icônes disponibles : " + string.Join(", ", icons.Keys.ToArray()));
            text.AppendLine();
            text.AppendLine("Fenêtres dont le processus ou le titre contient « dofus » :");
            try
            {
                foreach (RawWindow raw in Scanner.Enumerate(true))
                {
                    text.AppendLine("- processus=[" + raw.ProcessName + "] classe=[" + raw.ClassName
                        + "] titre=[" + raw.Title + "] retenue=" + (raw.IsDofusProcess ? "oui" : "non"));
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("Erreur : " + ex.Message);
            }
            text.AppendLine();
            text.AppendLine("Interprétation :");
            foreach (GameWindow window in windows)
            {
                text.AppendLine("- nom=[" + window.Name + "] classe=[" + window.ClassName + "] jeu=["
                    + window.Game + "] icône=[" + window.IconKey + "] initiative=" + window.Initiative
                    + " cycle=" + (window.InCycle ? "oui" : "non") + " direct=[" + window.Direct
                    + "] titre d'origine=[" + window.RawTitle + "]");
            }
            try
            {
                Clipboard.SetText(text.ToString());
                lastMessage = "Diagnostic copié : colle-le (Ctrl + V) dans ton signalement.";
            }
            catch (Exception ex)
            {
                lastMessage = "Copie impossible : " + ex.Message;
            }
            UpdateStatus();
        }
    }
}
