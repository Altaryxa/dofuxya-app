using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace DofuxyaManager
{
    // Prototype de lecture d'ecran et d'envoi au site (F-50) : boutons « Relever » et « Envoyer au
    // site » de l'onglet Equipe, visibles seulement avec « lecture_ecran=1 » dans reglages.txt.
    // Le texte lu est ecrit dans %AppData%\Dofuxya\releve-texte.txt pour regler la lecture (aucune image).
    internal partial class MainWindow
    {
        private Button readButton;
        private Button siteButton;
        private bool reading;
        private bool siteBusy;
        private readonly System.Windows.Threading.DispatcherTimer autoTimer = new System.Windows.Threading.DispatcherTimer();
        // Lignes du chat deja vues, par fenetre : seules les nouvelles comptent.
        private readonly Dictionary<IntPtr, HashSet<string>> seenChat = new Dictionary<IntPtr, HashSet<string>>();

        // ---------- Releve continu ----------

        private readonly ScreenWatcher watcher = new ScreenWatcher();
        // Textes du panneau des succes deja notes dans succes-vus.txt (par perso).
        private readonly HashSet<string> seenPanel = new HashSet<string>();
        // Passage dans les fenetres en arriere-plan : une toutes les 6 secondes, chacune a son tour.
        private const int SweepEvery = 6;
        private int sweepTick;
        private int sweepIndex;
        // Mesure du cout de la lecture continue, ecrite chaque minute dans lecture-continue.txt.
        private int statTicks;
        private int statReads;
        private double statMs;
        private double statMaxMs;
        private DateTime statStart = DateTime.UtcNow;

        private void StartAutoReading()
        {
            autoTimer.Interval = TimeSpan.FromSeconds(1);
            autoTimer.Tick += delegate { AutoRead(); };
            autoTimer.Start();
        }

        // Chaque seconde : verification legere de la fenetre Dofus au premier plan (voir ScreenWatcher),
        // en priorite basse pour laisser la main au jeu.
        private void AutoRead()
        {
            if (!settings.ScreenReading || !settings.AutoReading) return;
            AutoSendTick();
            if (reading) return;
            GameWindow window = FindWindow(Native.GetForegroundWindow());
            if (window != null && (!window.IsNamed || window.IsRetro)) window = null;
            GameWindow other = NextBackgroundWindow(window);
            if (window == null && other == null) return;
            reading = true;
            IntPtr handle = window != null ? window.Handle : IntPtr.Zero;
            string name = window != null ? window.Name : "";
            IntPtr otherHandle = other != null ? other.Handle : IntPtr.Zero;
            string otherName = other != null ? other.Name : "";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Reading result = null;
                Reading otherResult = null;
                List<string> panelLines = new List<string>();
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (handle != IntPtr.Zero) result = watcher.Tick(handle, name, panelLines);
                    if (otherHandle != IntPtr.Zero) otherResult = watcher.Sweep(otherHandle, otherName);
                }
                catch (Exception)
                {
                }
                finally
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Normal;
                }
                double ms = clock.Elapsed.TotalMilliseconds;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    reading = false;
                    NoteCost(ms, result != null || otherResult != null);
                    NotePanelLines(name, panelLines);
                    bool changed = Apply(window, result) | Apply(other, otherResult);
                    if (changed) RebuildRows();
                    UpdateSiteButton();
                    UpdateStatus();
                }));
            });
        }

        // Fenetre en arriere-plan a visiter a ce tour-ci (une toutes les 6 secondes), ou null.
        private GameWindow NextBackgroundWindow(GameWindow foreground)
        {
            if (++sweepTick < SweepEvery) return null;
            sweepTick = 0;
            List<GameWindow> candidates = new List<GameWindow>();
            HashSet<IntPtr> open = new HashSet<IntPtr>();
            foreach (GameWindow window in windows)
            {
                open.Add(window.Handle);
                if (window != foreground && window.IsNamed && !window.IsRetro && !Native.IsIconic(window.Handle))
                {
                    candidates.Add(window);
                }
            }
            watcher.Forget(open);
            if (candidates.Count == 0) return null;
            sweepIndex = (sweepIndex + 1) % candidates.Count;
            return candidates[sweepIndex];
        }

        // Range un releve sur la carte du perso. Renvoie true si l'onglet doit etre redessine.
        private bool Apply(GameWindow window, Reading result)
        {
            if (window == null || result == null || !windows.Contains(window)) return false;
            string before = window.LastReading != null ? window.LastReading.Values() : "";
            result.KeepFrom(window.LastReading);
            window.LastReading = result;
            bool events = NoteChatEvents(window, result) | NoteUnlocked(window, result);
            return result.Values() != before || events;
        }

        private void NoteCost(double ms, bool read)
        {
            statTicks++;
            statMs += ms;
            statMaxMs = Math.Max(statMaxMs, ms);
            if (read) statReads++;
            double elapsed = (DateTime.UtcNow - statStart).TotalMilliseconds;
            if (elapsed < 60000) return;
            string line = DateTime.Now.ToString("HH:mm") + " : " + statTicks + " vérifications, "
                + (statMs / statTicks).ToString("0") + " ms en moyenne (au plus " + statMaxMs.ToString("0") + " ms), "
                + statReads + " avec lecture de texte, soit " + (statMs * 100 / elapsed).ToString("0.0")
                + " % d'un cœur du processeur" + Environment.NewLine;
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataFolder, "lecture-continue.txt"), line, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            statStart = DateTime.UtcNow;
            statTicks = 0;
            statReads = 0;
            statMs = 0;
            statMaxMs = 0;
        }

        // Textes lus dans le panneau des succes : notes une fois dans succes-vus.txt (base du futur
        // catalogue des succes).
        private void NotePanelLines(string name, List<string> lines)
        {
            StringBuilder text = new StringBuilder();
            foreach (string line in lines)
            {
                if (line.Length < 3 || !seenPanel.Add(name + "\t" + line)) continue;
                text.AppendLine(name + "\t" + line);
            }
            if (text.Length == 0) return;
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataFolder, "succes-vus.txt"), text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        // Nouvelles lignes du chat qui parlent de succes, de niveau ou d'omega : notees dans
        // evenements.txt (pour apprendre leur format) et affichees dans la barre d'etat.
        private bool NoteChatEvents(GameWindow window, Reading result)
        {
            if (result.ChatLines.Count == 0) return false;
            HashSet<string> seen;
            // Premiere lecture du chat de cette fenetre : ce qui s'y trouve deja ne compte pas.
            bool first = !seenChat.TryGetValue(window.Handle, out seen);
            if (first) seenChat[window.Handle] = seen = new HashSet<string>();
            List<string> fresh = new List<string>();
            foreach (string line in result.ChatLines)
            {
                string key = ScreenReader.ChatKey(line);
                if (key.Length < 6 || !seen.Add(key) || first) continue;
                if (ScreenReader.IsProgressLine(line)) fresh.Add(line);
            }
            if (fresh.Count == 0) return false;
            StringBuilder text = new StringBuilder();
            string message = null;
            foreach (string line in fresh)
            {
                // Le perso de la fenetre debloque un succes ou passe un niveau : on le retient.
                string kind = "chat";
                string achievement = ScreenReader.UnlockedAchievement(line);
                int value;
                bool omega;
                if (achievement != null)
                {
                    kind = "succès";
                    if (!window.AddUnlocked(achievement)) { result.Events.Add(line); continue; }
                    message = window.Name + " : succès « " + achievement + " » débloqué.";
                }
                else if (ScreenReader.LevelUpOf(line, window.Name, out value, out omega))
                {
                    kind = omega ? "oméga" : "niveau";
                    if (omega && value > result.Omega) { result.Omega = value; result.Level = 200; }
                    if (!omega && value <= 200 && value > result.Level) result.Level = value;
                    message = window.Name + " : " + (omega ? "oméga " : "niveau ") + value + ".";
                }
                text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + window.Name + "] [" + kind + "] " + line);
                result.Events.Add(line);
            }
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataFolder, "evenements.txt"), text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            lastMessage = message ?? window.Name + " : " + fresh[fresh.Count - 1];
            return true;
        }

        // Succes lus dans la fenetre des recompenses a recuperer : retenus pour le perso et notes une fois.
        private bool NoteUnlocked(GameWindow window, Reading result)
        {
            StringBuilder text = new StringBuilder();
            foreach (string unlocked in result.Unlocked)
            {
                if (!window.AddUnlocked(unlocked)) continue;
                text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + window.Name + "] [succès] " + unlocked);
                lastMessage = window.Name + " : succès « " + unlocked + " » débloqué.";
            }
            if (text.Length == 0) return false;
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataFolder, "evenements.txt"), text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            return true;
        }

        // ---------- Releve ----------

        private void ReadScreens()
        {
            if (reading) return;
            List<GameWindow> targets = new List<GameWindow>();
            foreach (GameWindow window in NamedWindows())
            {
                if (!window.IsRetro) targets.Add(window);
            }
            if (targets.Count == 0)
            {
                lastMessage = "Aucune fenêtre Dofus 3 à relever.";
                UpdateStatus();
                return;
            }
            reading = true;
            SetBusy(readButton, "Relevé…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                Dictionary<GameWindow, Reading> results = new Dictionary<GameWindow, Reading>();
                DateTime start = DateTime.Now;
                string error = null;
                foreach (GameWindow window in targets)
                {
                    try
                    {
                        results[window] = ScreenReader.Read(window.Handle, window.Name, log);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        break;
                    }
                }
                double seconds = (DateTime.Now - start).TotalSeconds;
                try
                {
                    File.WriteAllText(Path.Combine(AppPaths.DataFolder, "releve-texte.txt"), log.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    foreach (KeyValuePair<GameWindow, Reading> pair in results) pair.Key.LastReading = pair.Value;
                    reading = false;
                    lastMessage = error != null
                        ? "Relevé impossible : " + error
                        : results.Count + " fenêtre(s) relevée(s) en " + seconds.ToString("0.0") + " s.";
                    RebuildRows();
                    UpdateSiteButton();
                    UpdateStatus();
                }));
            });
        }

        // ---------- Site (F-50) ----------
        // Envoi automatique, sans confirmation : ce qui a change depuis le dernier envoi (niveau, omega,
        // total de succes, succes debloques) part au plus une fois par minute. Le bouton « Envoyer au
        // site » envoie tout de suite.

        private const int SendEvery = 60;
        private int sendTick;
        private readonly Dictionary<string, string> sentValues = new Dictionary<string, string>();
        private readonly Dictionary<string, HashSet<string>> sentUnlocked = new Dictionary<string, HashSet<string>>();

        private void UpdateSiteButton()
        {
            if (readButton != null) readButton.Content = "Relever";
            if (readButton != null) readButton.IsEnabled = !reading;
            if (siteButton == null || siteBusy) return;
            siteButton.IsEnabled = true;
            siteButton.Content = SiteLink.IsLinked ? "Envoyer au site" : "Lier au site";
        }

        private void OnSiteButton()
        {
            if (siteBusy) return;
            if (!SiteLink.IsLinked) LinkSite();
            else SendToSite(true);
        }

        // Appele chaque seconde par la lecture continue.
        private void AutoSendTick()
        {
            if (++sendTick < SendEvery) return;
            sendTick = 0;
            if (SiteLink.IsLinked && !siteBusy) SendToSite(false);
        }

        private void LinkSite()
        {
            siteBusy = true;
            SetBusy(siteButton, "En attente…");
            lastMessage = "Autorise l'appareil dans la page qui vient de s'ouvrir dans ton navigateur.";
            UpdateStatus();
            string site = settings.SiteUrl;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    result = SiteLink.Link(site, delegate { return false; })
                        ? "Outil lié à ton compte du site : les relevés y seront envoyés automatiquement."
                        : "Liaison abandonnée : rien n'a été autorisé dans les 10 minutes.";
                }
                catch (Exception ex)
                {
                    result = "Liaison impossible : " + ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    siteBusy = false;
                    lastMessage = result;
                    UpdateSiteButton();
                    UpdateStatus();
                }));
            });
        }

        private static string BaseName(string unlocked)
        {
            int paren = unlocked.LastIndexOf(" (");
            return paren > 0 ? unlocked.Substring(0, paren) : unlocked;
        }

        // Releves a envoyer : seulement ce qui a change depuis le dernier envoi (tout, si « all »).
        private List<Dictionary<string, object>> CollectReadings(bool all)
        {
            List<Dictionary<string, object>> readings = new List<Dictionary<string, object>>();
            foreach (GameWindow window in NamedWindows())
            {
                if (window.IsRetro) continue;
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["name"] = window.Name;
                Reading r = window.LastReading;
                string sent;
                if (r != null && (all || !sentValues.TryGetValue(window.Name, out sent) || sent != r.Values()))
                {
                    if (r.Level > 0) item["level"] = r.Level;
                    if (r.Omega > 0) item["omega"] = r.Omega;
                    if (r.Success > 0 && !r.SuccessFromAccount) item["achievementPoints"] = r.Success;
                }
                HashSet<string> done;
                if (!sentUnlocked.TryGetValue(window.Name, out done)) done = new HashSet<string>();
                List<string> fresh = new List<string>();
                foreach (string unlocked in window.Unlocked)
                {
                    string name = BaseName(unlocked);
                    if (!done.Contains(name) && fresh.Count < 50) fresh.Add(name);
                }
                if (fresh.Count > 0) item["achievements"] = fresh;
                if (item.Count > 1) readings.Add(item);
            }
            return readings;
        }

        private void SendToSite(bool now)
        {
            List<Dictionary<string, object>> readings = CollectReadings(now);
            if (readings.Count == 0)
            {
                if (now)
                {
                    lastMessage = "Rien de nouveau à envoyer au site.";
                    UpdateStatus();
                }
                return;
            }
            // Ce qui part maintenant, pour le marquer envoye si le site l'accepte.
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (GameWindow window in NamedWindows())
            {
                if (window.LastReading != null) values[window.Name] = window.LastReading.Values();
            }
            siteBusy = true;
            if (now) SetBusy(siteButton, "Envoi…");
            string site = settings.SiteUrl;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> answer = null;
                string error = null;
                try
                {
                    answer = SiteLink.Apply(site, readings);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    siteBusy = false;
                    if (error != null)
                    {
                        lastMessage = "Envoi au site impossible : " + error;
                    }
                    else
                    {
                        foreach (Dictionary<string, object> item in readings)
                        {
                            string name = Convert.ToString(item["name"]);
                            string value;
                            if (values.TryGetValue(name, out value)) sentValues[name] = value;
                            object list;
                            if (!item.TryGetValue("achievements", out list)) continue;
                            HashSet<string> done;
                            if (!sentUnlocked.TryGetValue(name, out done)) sentUnlocked[name] = done = new HashSet<string>();
                            foreach (string achievement in (List<string>)list) done.Add(achievement);
                        }
                        lastMessage = SendSummary(answer);
                    }
                    UpdateSiteButton();
                    UpdateStatus();
                }));
            });
        }

        // « Envoyé au site : 2 perso(s) mis à jour, 3 succès. Non reconnus : … »
        private static string SendSummary(Dictionary<string, object> answer)
        {
            object updated, achievements, unknown;
            int persos = answer.TryGetValue("updated", out updated) ? Convert.ToInt32(updated) : 0;
            int added = answer.TryGetValue("achievements", out achievements) ? Convert.ToInt32(achievements) : 0;
            string text = "Envoyé au site : " + persos + " perso(s) mis à jour, " + added + " succès validé(s).";
            System.Collections.ArrayList names = answer.TryGetValue("unknown", out unknown)
                ? unknown as System.Collections.ArrayList : null;
            if (names != null && names.Count > 0)
            {
                List<string> list = new List<string>();
                foreach (object name in names) list.Add(Convert.ToString(name));
                text += " Succès non reconnus : " + string.Join(", ", list.ToArray()) + ".";
            }
            return text;
        }

        private static void SetBusy(Button button, string text)
        {
            if (button == null) return;
            button.IsEnabled = false;
            button.Content = text;
        }
    }
}
