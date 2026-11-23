using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DofuxyaManager
{
    // Lecture d'ecran (F-50, reservee a l'auteur : visible seulement avec « lecture_ecran=1 » dans
    // reglages.txt). Elle ne lit que le niveau et l'omega, les succes debloques et les quetes terminees,
    // et envoie ce qui change au site, sans confirmation, au plus une fois par minute.
    // Journaux (aucune image) dans %AppData%\Dofuxya : evenements.txt (messages retenus),
    // releve-texte.txt (dernier releve manuel), lecture-continue.txt (cout de la lecture continue).
    internal partial class MainWindow
    {
        private Button readButton;
        private bool reading;
        private readonly DispatcherTimer autoTimer = new DispatcherTimer();
        // Lignes du chat deja vues, par fenetre : seules les nouvelles comptent.
        private readonly Dictionary<IntPtr, HashSet<string>> seenChat = new Dictionary<IntPtr, HashSet<string>>();
        private readonly ScreenWatcher watcher = new ScreenWatcher();

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

        // ---------- Lecture continue ----------

        private void StartAutoReading()
        {
            autoTimer.Interval = TimeSpan.FromSeconds(1);
            autoTimer.Tick += delegate { AutoRead(); };
            autoTimer.Start();
        }

        // Chaque seconde : verification legere de la fenetre Dofus au premier plan (voir ScreenWatcher),
        // et d'une fenetre en arriere-plan de temps en temps, en priorite basse pour laisser la main au jeu.
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
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (handle != IntPtr.Zero) result = watcher.Tick(handle, name);
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
                    bool changed = Apply(window, result) | Apply(other, otherResult);
                    if (changed) RebuildRows();
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
            DataLog.Append("lecture-continue.txt", DateTime.Now.ToString("HH:mm") + " : " + statTicks + " vérifications, "
                + (statMs / statTicks).ToString("0") + " ms en moyenne (au plus " + statMaxMs.ToString("0") + " ms), "
                + statReads + " avec lecture de texte, soit " + (statMs * 100 / elapsed).ToString("0.0")
                + " % d'un cœur du processeur" + Environment.NewLine);
            statStart = DateTime.UtcNow;
            statTicks = 0;
            statReads = 0;
            statMs = 0;
            statMaxMs = 0;
        }

        // Nouvelles lignes du chat sur la progression du perso de la fenetre : succes debloque, quete
        // terminee, niveau ou omega. Notees dans evenements.txt (les lignes qui en parlent sans etre
        // reconnues aussi, pour apprendre leur format) et affichees dans la barre d'etat.
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
            StringBuilder log = new StringBuilder();
            string message = null;
            foreach (string line in fresh)
            {
                string kind = "chat";
                string achievement = ScreenReader.UnlockedAchievement(line);
                string quest = ScreenReader.CompletedQuest(line);
                int value;
                bool omega;
                if (achievement != null)
                {
                    kind = "succès";
                    if (!window.AddUnlocked(achievement)) { result.Events.Add(line); continue; }
                    message = window.Name + " : succès « " + achievement + " » débloqué.";
                }
                else if (quest != null)
                {
                    kind = "quête";
                    if (!window.AddQuest(quest)) { result.Events.Add(line); continue; }
                    message = window.Name + " : quête « " + quest + " » terminée.";
                }
                else if (ScreenReader.LevelUpOf(line, window.Name, out value, out omega))
                {
                    kind = omega ? "oméga" : "niveau";
                    if (omega && value > result.Omega) { result.Omega = value; result.Level = 200; }
                    if (!omega && value <= 200 && value > result.Level) result.Level = value;
                    message = window.Name + " : " + (omega ? "oméga " : "niveau ") + value + ".";
                }
                log.Append(DataLog.Line(window.Name, kind, line));
                result.Events.Add(line);
            }
            DataLog.Append("evenements.txt", log.ToString());
            lastMessage = message ?? window.Name + " : " + fresh[fresh.Count - 1];
            return true;
        }

        // Succes lus dans la notification ou la fenetre des recompenses : retenus pour le perso, notes une fois.
        private bool NoteUnlocked(GameWindow window, Reading result)
        {
            StringBuilder log = new StringBuilder();
            foreach (string unlocked in result.Unlocked)
            {
                if (!window.AddUnlocked(unlocked)) continue;
                log.Append(DataLog.Line(window.Name, "succès", unlocked));
                lastMessage = window.Name + " : succès « " + unlocked + " » débloqué.";
            }
            DataLog.Append("evenements.txt", log.ToString());
            return log.Length > 0;
        }

        // ---------- Releve manuel (bouton « Relever ») ----------

        private void ReadScreens()
        {
            if (reading) return;
            List<GameWindow> targets = NamedWindows().FindAll(delegate(GameWindow w) { return !w.IsRetro; });
            if (targets.Count == 0)
            {
                lastMessage = "Aucune fenêtre Dofus à relever.";
                UpdateStatus();
                return;
            }
            reading = true;
            readButton.IsEnabled = false;
            readButton.Content = "Relevé…";
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
                DataLog.Write("releve-texte.txt", log.ToString());
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    foreach (KeyValuePair<GameWindow, Reading> pair in results) Apply(pair.Key, pair.Value);
                    reading = false;
                    readButton.IsEnabled = true;
                    readButton.Content = "Relever";
                    lastMessage = error != null
                        ? "Relevé impossible : " + error
                        : results.Count + " fenêtre(s) relevée(s) en " + seconds.ToString("0.0") + " s.";
                    RebuildRows();
                    UpdateStatus();
                }));
            });
        }

        // ---------- Envoi au site ----------
        // Ce qui a change depuis le dernier envoi (niveau, omega, succes debloques, quetes terminees)
        // part tout seul, au plus une fois par minute, si l'outil est lie au site.

        private const int SendEvery = 60;
        private int sendTick;
        private bool sending;
        private readonly Dictionary<string, string> sentValues = new Dictionary<string, string>();
        private readonly Dictionary<string, HashSet<string>> sentUnlocked = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, HashSet<string>> sentQuests = new Dictionary<string, HashSet<string>>();

        // Appele chaque seconde par la lecture continue.
        private void AutoSendTick()
        {
            if (++sendTick < SendEvery) return;
            sendTick = 0;
            if (!sending && SiteLink.IsLinked) SendToSite();
        }

        // Noms pas encore envoyes pour ce perso (50 au plus).
        private static List<string> Unsent(IEnumerable<string> names, Dictionary<string, HashSet<string>> sent, string character)
        {
            HashSet<string> done;
            if (!sent.TryGetValue(character, out done)) done = new HashSet<string>();
            List<string> fresh = new List<string>();
            foreach (string name in names)
            {
                if (!done.Contains(name) && !fresh.Contains(name) && fresh.Count < 50) fresh.Add(name);
            }
            return fresh;
        }

        private static void MarkSent(Dictionary<string, HashSet<string>> sent, string character, object names)
        {
            List<string> list = names as List<string>;
            if (list == null) return;
            HashSet<string> done;
            if (!sent.TryGetValue(character, out done)) sent[character] = done = new HashSet<string>();
            foreach (string name in list) done.Add(name);
        }

        // Releves a envoyer : seulement ce qui a change depuis le dernier envoi.
        private List<Dictionary<string, object>> CollectReadings()
        {
            List<Dictionary<string, object>> readings = new List<Dictionary<string, object>>();
            foreach (GameWindow window in NamedWindows())
            {
                if (window.IsRetro) continue;
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["name"] = window.Name;
                Reading r = window.LastReading;
                string sent;
                if (r != null && (!sentValues.TryGetValue(window.Name, out sent) || sent != r.Values()))
                {
                    if (r.Level > 0) item["level"] = r.Level;
                    if (r.Omega > 0) item["omega"] = r.Omega;
                }
                List<string> achievements = Unsent(window.Unlocked.ConvertAll(GameWindow.BaseName), sentUnlocked, window.Name);
                if (achievements.Count > 0) item["achievements"] = achievements;
                List<string> quests = Unsent(window.Quests, sentQuests, window.Name);
                if (quests.Count > 0) item["quests"] = quests;
                if (item.Count > 1) readings.Add(item);
            }
            return readings;
        }

        private void SendToSite()
        {
            List<Dictionary<string, object>> readings = CollectReadings();
            if (readings.Count == 0) return;
            // Ce qui part maintenant, pour le marquer envoye si le site l'accepte.
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (GameWindow window in NamedWindows())
            {
                if (window.LastReading != null) values[window.Name] = window.LastReading.Values();
            }
            sending = true;
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
                    sending = false;
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
                            if (item.TryGetValue("achievements", out list)) MarkSent(sentUnlocked, name, list);
                            if (item.TryGetValue("quests", out list)) MarkSent(sentQuests, name, list);
                        }
                        lastMessage = SendSummary(answer);
                    }
                    UpdateStatus();
                }));
            });
        }

        // « Envoyé au site : 2 perso(s) mis à jour, 3 succès, 1 quête. Non reconnus : … »
        private static string SendSummary(Dictionary<string, object> answer)
        {
            string text = "Envoyé au site : " + SiteLink.Number(answer, "updated") + " perso(s) mis à jour, "
                + SiteLink.Number(answer, "achievements") + " succès et " + SiteLink.Number(answer, "quests")
                + " quête(s) validé(s).";
            List<string> unknown = SiteLink.Texts(answer, "unknown");
            if (unknown.Count > 0) text += " Succès non reconnus : " + string.Join(", ", unknown.ToArray()) + ".";
            List<string> unknownQuests = SiteLink.Texts(answer, "unknownQuests");
            if (unknownQuests.Count > 0) text += " Quêtes non reconnues : " + string.Join(", ", unknownQuests.ToArray()) + ".";
            return text;
        }
    }
}
