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
    // Un raccourci attribue a une action.
    internal class Shortcut
    {
        public Hotkey Hotkey;
        public string Label;
        public Action Action;
    }

    // Raccourcis, suivi du tour, personnage principal et ordre manuel.
    internal partial class MainWindow
    {
        // ---------- Raccourcis ----------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            handle = new WindowInteropHelper(this).Handle;
            Dwm.UseDarkTitleBar(handle);
            Dwm.RoundCorners(handle);
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != null) source.AddHook(WndProc);

            mouseHook.Handler = OnMouseButton;
            if (!mouseHook.Install())
            {
                lastMessage = "Les boutons de souris ne peuvent pas servir de raccourcis sur ce poste "
                    + "(écoute refusée par Windows).";
            }
            foregroundWatcher.Changed = OnForegroundChanged;
            foregroundWatcher.Install();

            // Windows previent cette fenetre quand une fenetre s'ouvre, se ferme, change de titre
            // ou clignote : la liste se met a jour sans surveillance permanente.
            try
            {
                shellMessage = (int)Native.RegisterWindowMessage("SHELLHOOK");
                Native.RegisterShellHookWindow(handle);
            }
            catch (Exception)
            {
                shellMessage = 0;
            }

            turnWatcher.Received = OnGameNotification;
            turnWatcher.StateChanged = delegate
            {
                lastMessage = "Suivi du tour : " + turnWatcher.State + ".";
                UpdateStatus();
            };
            if (settings.AutoFollow) turnWatcher.Start();

            RebuildShortcuts();
            OnForegroundChanged(Native.GetForegroundWindow());
            UpdateStatus();
        }

        // Liste des raccourcis en vigueur : navigation + acces directs des fenetres ouvertes.
        private void RebuildShortcuts()
        {
            List<Shortcut> list = new List<Shortcut>();
            AddShortcut(list, settings.Next, "fenêtre suivante", delegate { Step(1); });
            AddShortcut(list, settings.Previous, "fenêtre précédente", delegate { Step(-1); });
            AddShortcut(list, settings.Leader, "personnage principal", delegate { GoToLeader(); });
            foreach (GameWindow window in windows)
            {
                if (!window.IsNamed) continue;
                GameWindow target = window;
                AddShortcut(list, window.Direct, window.Name, delegate { GoTo(target); });
            }
            shortcuts = list;
        }

        private static void AddShortcut(List<Shortcut> list, Hotkey hotkey, string label, Action action)
        {
            if (hotkey == null || hotkey.IsEmpty) return;
            Shortcut shortcut = new Shortcut();
            shortcut.Hotkey = hotkey;
            shortcut.Label = label;
            shortcut.Action = action;
            list.Add(shortcut);
        }

        // Les raccourcis s'appliquent-ils en ce moment ?
        private bool IsInScope()
        {
            if (!settings.OnlyInDofus) return true;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == handle) return true;
            if (FindWindow(foreground) != null) return true;
            return Scanner.IsDofusProcessWindow(foreground);
        }

        // Enregistre ou libere les raccourcis clavier selon la portee choisie.
        private void SyncKeyboard(bool rebuild)
        {
            if (handle == IntPtr.Zero) return;
            bool wanted = captureButton == null && IsInScope();
            if (!rebuild && wanted == keyboardActive) return;

            foreach (int id in registered.Keys)
            {
                Native.UnregisterHotKey(handle, id);
            }
            registered.Clear();
            keyboardActive = false;
            if (!wanted) return;

            List<string> refused = new List<string>();
            int nextId = 1;
            for (int i = 0; i < shortcuts.Count; i++)
            {
                Shortcut shortcut = shortcuts[i];
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                {
                    if (shortcuts[j].Hotkey.SameAs(shortcut.Hotkey)) duplicate = true;
                }
                if (duplicate)
                {
                    refused.Add(shortcut.Hotkey + " (" + shortcut.Label + ", en double)");
                    continue;
                }
                if (shortcut.Hotkey.IsMouse) continue;
                int id = nextId++;
                if (Native.RegisterHotKey(handle, id, shortcut.Hotkey.Mods | Native.MOD_NOREPEAT, shortcut.Hotkey.Vk))
                {
                    registered[id] = shortcut;
                }
                else
                {
                    refused.Add(shortcut.Hotkey + " (" + shortcut.Label + ")");
                }
            }
            keyboardActive = true;
            refusedText = refused.Count == 0 ? ""
                : "Raccourcis refusés (déjà pris par un autre logiciel ou en double) : "
                    + string.Join(", ", refused.ToArray()) + ".";
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                Shortcut shortcut;
                if (registered.TryGetValue(wParam.ToInt32(), out shortcut))
                {
                    shortcut.Action();
                }
                handled = true;
            }
            else if (shellMessage != 0 && msg == shellMessage)
            {
                int code = (int)(wParam.ToInt64() & 0xFFFF);
                if (code == 1 || code == 2 || code == 6)
                {
                    // Fenetre creee, detruite, ou titre modifie.
                    scanTimer.Stop();
                    scanTimer.Start();
                }
                else if (code == 0x8006)
                {
                    OnWindowFlash(lParam);
                }
            }
            return IntPtr.Zero;
        }

        // Une fenetre clignote dans la barre des taches : si c'est un personnage et que le suivi
        // du tour est actif, on l'amene devant.
        private void OnWindowFlash(IntPtr flashing)
        {
            flashCount++;
            if (!settings.AutoFollow) return;
            GameWindow window = FindWindow(flashing);
            if (window == null || !window.IsNamed) return;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == flashing) return;
            // Seulement quand on est deja dans Dofus (ou dans l'outil).
            if (foreground != handle && FindWindow(foreground) == null) return;
            // Un clignotement juste apres notre propre demande est son echec, pas un debut de tour.
            if (flashing == lastGoToHandle && (DateTime.UtcNow - lastGoTo).TotalMilliseconds < 700) return;
            Dispatcher.BeginInvoke(new Action(delegate { GoTo(window); }));
        }

        // Une notification Windows vient d'arriver. Si elle vient de Dofus et nomme un personnage
        // ouvert, on amene sa fenetre devant.
        private void OnGameNotification(string app, List<string> texts)
        {
            string joined = string.Join(" | ", texts.ToArray());
            bool fromDofus = app != null && app.ToLowerInvariant().Contains("dofus");
            if (!fromDofus)
            {
                foreach (string text in texts)
                {
                    if (text != null && text.ToLowerInvariant().Contains("dofus")) fromDofus = true;
                }
            }
            if (!fromDofus) return;

            recentNotifications.Add(DateTime.Now.ToString("HH:mm:ss") + " [" + app + "] " + joined);
            if (recentNotifications.Count > 10) recentNotifications.RemoveAt(0);
            if (!settings.AutoFollow) return;

            // Le nom le plus long d'abord, pour ne pas confondre deux noms qui se ressemblent.
            GameWindow target = null;
            foreach (GameWindow window in windows)
            {
                if (!window.IsNamed) continue;
                if (joined.IndexOf(window.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (target == null || window.Name.Length > target.Name.Length) target = window;
            }
            if (target == null) return;

            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == target.Handle) return;
            if (foreground != handle && FindWindow(foreground) == null) return;
            GoTo(target);
        }

        private void MaximizeRetroWindows()
        {
            foreach (GameWindow window in windows) MaximizeIfRetro(window);
        }

        // Agrandit une fenetre Retro, sauf si elle l'est deja ou si elle est en plein ecran.
        private static void MaximizeIfRetro(GameWindow window)
        {
            if (!window.IsRetro || Native.IsZoomed(window.Handle) || Native.IsFullscreen(window.Handle)) return;
            Native.ShowWindow(window.Handle, Native.SW_MAXIMIZE);
        }

        private void SaveWindowPosition()
        {
            if (WindowState != WindowState.Normal) return;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            // Une fenetre reduite est rangee tres loin hors de l'ecran : on ignore cette position.
            if (Left < SystemParameters.VirtualScreenLeft - 50 || Top < SystemParameters.VirtualScreenTop - 50) return;
            if (Left == settings.WindowX && Top == settings.WindowY) return;
            settings.WindowX = Left;
            settings.WindowY = Top;
            settings.Save();
        }

        // ---------- Personnage principal ----------

        private string LeaderName()
        {
            if (settings.LeaderKey.Length == 0) return "Aucun";
            int separator = settings.LeaderKey.IndexOf('\t');
            return separator >= 0 ? settings.LeaderKey.Substring(separator + 1) : settings.LeaderKey;
        }

        private void OpenLeaderMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem none = new MenuItem();
            none.Header = "Aucun";
            none.Click += delegate { SetLeader(""); };
            menu.Items.Add(none);
            foreach (GameWindow window in windows)
            {
                if (!window.IsNamed) continue;
                string key = window.Key;
                MenuItem item = new MenuItem();
                item.Header = window.Name;
                item.Click += delegate { SetLeader(key); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = leaderButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void SetLeader(string key)
        {
            settings.LeaderKey = key;
            settings.Save();
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAll(true); }));
        }

        // Prepare l'invitation de l'equipe : le meneur passe devant et les commandes /invite des autres
        // personnages sont copiees dans le presse-papiers. Le joueur les colle lui-meme : l'outil
        // n'envoie rien au jeu.
        private void GroupTeam()
        {
            GameWindow leader = FindLeader();
            if (leader == null)
            {
                List<GameWindow> named = NamedWindows();
                if (named.Count > 0) leader = named[0];
            }
            if (leader == null)
            {
                lastMessage = "Aucun personnage détecté à grouper.";
                UpdateStatus();
                return;
            }

            List<string> commands = new List<string>();
            int otherGame = 0;
            foreach (GameWindow window in windows)
            {
                if (!window.IsNamed || window == leader) continue;
                if (window.Game != leader.Game)
                {
                    otherGame++;
                    continue;
                }
                commands.Add("/invite " + window.Name);
            }
            if (commands.Count == 0)
            {
                lastMessage = "Personne à inviter : " + leader.Name + " est le seul personnage de " + leader.Game + ".";
                UpdateStatus();
                return;
            }

            bool copied = false;
            for (int attempt = 0; attempt < 3 && !copied; attempt++)
            {
                try
                {
                    Clipboard.SetText(string.Join(";", commands.ToArray()));
                    copied = true;
                }
                catch (Exception)
                {
                    Thread.Sleep(60);
                }
            }
            if (!copied)
            {
                lastMessage = "Le presse-papiers est occupé par un autre logiciel : réessaie.";
                UpdateStatus();
                return;
            }

            GoTo(leader);
            lastMessage = commands.Count + " commande(s) /invite copiée(s). Dans le chat de " + leader.Name
                + " : Ctrl + V, puis Entrée.";
            if (otherGame > 0)
            {
                lastMessage += " " + otherGame + " personnage(s) d'un autre jeu non inclus.";
            }
            UpdateStatus();
        }

        // Le personnage principal, s'il est ouvert.
        private GameWindow FindLeader()
        {
            if (settings.LeaderKey.Length == 0) return null;
            foreach (GameWindow window in windows)
            {
                if (window.IsNamed && window.Key == settings.LeaderKey) return window;
            }
            return null;
        }

        private void GoToLeader()
        {
            GameWindow leader = FindLeader();
            if (leader != null)
            {
                GoTo(leader);
                return;
            }
            lastMessage = settings.LeaderKey.Length == 0
                ? "Aucun personnage principal choisi (onglet Raccourcis)."
                : "Le personnage principal n'est pas ouvert.";
            UpdateStatus();
        }

        // ---------- Ordre manuel ----------

        private List<GameWindow> NamedWindows()
        {
            List<GameWindow> list = new List<GameWindow>();
            foreach (GameWindow window in windows)
            {
                if (window.IsNamed) list.Add(window);
            }
            return list;
        }

        // L'ordre manuel s'applique quand tous les personnages ouverts ont un rang fixe.
        private bool IsManualOrder()
        {
            int named = 0;
            foreach (GameWindow window in windows)
            {
                if (!window.IsNamed) continue;
                named++;
                if (window.ManualRank <= 0) return false;
            }
            return named > 0;
        }

        private void ClearManualOrder()
        {
            foreach (GameWindow window in windows)
            {
                if (window.IsNamed && window.ManualRank != 0)
                {
                    window.ManualRank = 0;
                    store.Save(window);
                }
            }
        }

        private void BeginDrag(GameWindow window, Border badge, MouseButtonEventArgs e)
        {
            dragWindow = window;
            badge.CaptureMouse();
            Border card;
            if (rowBorders.TryGetValue(window, out card)) card.Opacity = 0.55;
            e.Handled = true;
        }

        // Rang de la carte survolee par le pointeur.
        private int DropIndex(double y, List<GameWindow> named)
        {
            for (int i = 0; i < named.Count; i++)
            {
                Border card;
                if (!rowBorders.TryGetValue(named[i], out card)) continue;
                double top = card.TranslatePoint(new Point(0, 0), rowsPanel).Y;
                if (y < top + card.ActualHeight + 3) return i;
            }
            return named.Count - 1;
        }

        private void ContinueDrag(MouseEventArgs e)
        {
            if (dragWindow == null) return;
            List<GameWindow> named = NamedWindows();
            int target = DropIndex(e.GetPosition(rowsPanel).Y, named);
            for (int i = 0; i < named.Count; i++)
            {
                Border card;
                if (!rowBorders.TryGetValue(named[i], out card)) continue;
                bool isTarget = i == target && named[i] != dragWindow;
                bool active = named[i].Handle == lastActive;
                card.BorderBrush = Ui.Paint(isTarget ? Ui.ColText : (active ? Ui.ColAccent : Ui.ColRow));
            }
        }

        private void EndDrag(Border badge, MouseButtonEventArgs e)
        {
            if (dragWindow == null) return;
            GameWindow moved = dragWindow;
            dragWindow = null;
            List<GameWindow> named = NamedWindows();
            int target = DropIndex(e.GetPosition(rowsPanel).Y, named);
            badge.ReleaseMouseCapture();
            int from = named.IndexOf(moved);
            if (from < 0 || target == from || target < 0)
            {
                Border card;
                if (rowBorders.TryGetValue(moved, out card)) card.Opacity = 1;
                UpdateHighlight();
                return;
            }
            named.RemoveAt(from);
            named.Insert(Math.Min(target, named.Count), moved);
            for (int i = 0; i < named.Count; i++)
            {
                named[i].ManualRank = i + 1;
                store.Save(named[i]);
            }
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAll(true); }));
        }

        // Bouton de souris presse (4 = molette, 5 et 6 = lateraux). Renvoie true s'il sert de raccourci.
        private bool OnMouseButton(uint button)
        {
            uint mods = MouseHook.CurrentMods();
            if (captureButton != null)
            {
                if (Native.GetForegroundWindow() != handle) return false;
                Hotkey captured = new Hotkey();
                captured.Vk = button;
                captured.Mods = mods;
                Dispatcher.BeginInvoke(new Action(delegate { FinishCapture(captured); }));
                return true;
            }
            if (!IsInScope()) return false;
            foreach (Shortcut shortcut in shortcuts)
            {
                if (shortcut.Hotkey.IsMouse && shortcut.Hotkey.Vk == button && shortcut.Hotkey.Mods == mods)
                {
                    Dispatcher.BeginInvoke(shortcut.Action);
                    return true;
                }
            }
            return false;
        }

        private void OnForegroundChanged(IntPtr foreground)
        {
            GameWindow window = FindWindow(foreground);
            if (window != null && window.IsNamed && lastActive != foreground)
            {
                lastActive = foreground;
                UpdateHighlight();
            }
            SyncKeyboard(false);
        }

        // Passe a la fenetre suivante (+1) ou precedente (-1) du cycle, en boucle.
        private void Step(int direction)
        {
            List<GameWindow> cycle = CycleList();
            int count = cycle.Count;
            if (count == 0) return;

            IntPtr foreground = Native.GetForegroundWindow();
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                if (cycle[i].Handle == foreground)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (cycle[i].Handle == lastActive)
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0) index = direction > 0 ? -1 : 0;

            int target = ((index + direction) % count + count) % count;
            GoTo(cycle[target]);
        }

        private void GoTo(GameWindow window)
        {
            lastGoTo = DateTime.UtcNow;
            lastGoToHandle = window.Handle;
            if (Native.Activate(window.Handle))
            {
                lastActive = window.Handle;
                UpdateHighlight();
            }
            else
            {
                lastMessage = "Windows a refusé de mettre « " + window.Name + " » au premier plan.";
                UpdateStatus();
            }
        }

        // Saisie d'un raccourci : clic sur le champ, puis appui sur la touche ou le bouton de souris.
        private void StartCapture(Button button, Action<Hotkey> apply)
        {
            CancelCapture();
            captureButton = button;
            captureApply = apply;
            SyncKeyboard(true);
            button.Content = "Appuie…";
            lastMessage = "Saisie d'un raccourci : touche, combinaison ou bouton latéral de souris. "
                + "Échap annule, Retour arrière ou Suppr l'efface.";
            UpdateStatus();
        }

        private void CancelCapture()
        {
            if (captureButton == null) return;
            FinishCapture(null);
        }

        private void FinishCapture(Hotkey result)
        {
            if (captureButton == null) return;
            Action<Hotkey> apply = captureApply;
            captureButton = null;
            captureApply = null;
            if (result != null && apply != null) apply(result);
            lastMessage = "";
            if (nextButton != null) nextButton.Content = settings.Next.ToString();
            if (previousButton != null) previousButton.Content = settings.Previous.ToString();
            if (leaderHotkeyButton != null) leaderHotkeyButton.Content = settings.Leader.ToString();
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAll(true); }));
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (captureButton == null) return;
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin
                || key == Key.ImeProcessed || key == Key.DeadCharProcessed || key == Key.None)
            {
                return;
            }
            if (key == Key.Escape)
            {
                FinishCapture(null);
                return;
            }
            Hotkey hotkey = new Hotkey();
            if (key != Key.Back && key != Key.Delete)
            {
                ModifierKeys modifiers = Keyboard.Modifiers;
                if ((modifiers & ModifierKeys.Control) != 0) hotkey.Mods |= Native.MOD_CONTROL;
                if ((modifiers & ModifierKeys.Alt) != 0) hotkey.Mods |= Native.MOD_ALT;
                if ((modifiers & ModifierKeys.Shift) != 0) hotkey.Mods |= Native.MOD_SHIFT;
                if ((modifiers & ModifierKeys.Windows) != 0) hotkey.Mods |= Native.MOD_WIN;
                hotkey.Vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            FinishCapture(hotkey);
        }
    }
}
