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

        // ---------- Site ----------

        // Le meme bouton lie l'outil au site, puis envoie les releves.
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
            else SendToSite();
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
                        ? "Outil lié à ton compte du site."
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

        // Releves a envoyer : seulement les valeurs lues (total de succes du perso, pas du compte).
        private List<Dictionary<string, object>> CollectReadings()
        {
            List<Dictionary<string, object>> readings = new List<Dictionary<string, object>>();
            foreach (GameWindow window in NamedWindows())
            {
                Reading r = window.LastReading;
                if (r == null || window.IsRetro) continue;
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["name"] = window.Name;
                if (r.Level > 0) item["level"] = r.Level;
                if (r.Omega > 0) item["omega"] = r.Omega;
                if (r.Success > 0 && !r.SuccessFromAccount) item["achievementPoints"] = r.Success;
                if (item.Count > 1) readings.Add(item);
            }
            return readings;
        }

        private void SendToSite()
        {
            List<Dictionary<string, object>> readings = CollectReadings();
            if (readings.Count == 0)
            {
                lastMessage = "Rien à envoyer : fais d'abord un relevé.";
                UpdateStatus();
                return;
            }
            siteBusy = true;
            SetBusy(siteButton, "Envoi…");
            string site = settings.SiteUrl;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Dictionary<string, object>> current = null;
                string error = null;
                try
                {
                    current = SiteLink.Preview(site, readings);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate { ConfirmAndApply(site, readings, current, error); }));
            });
        }

        private void ConfirmAndApply(string site, List<Dictionary<string, object>> readings,
            List<Dictionary<string, object>> current, string error)
        {
            if (error != null)
            {
                siteBusy = false;
                lastMessage = "Envoi impossible : " + error;
                UpdateSiteButton();
                UpdateStatus();
                return;
            }
            Dictionary<string, Dictionary<string, object>> byName =
                new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> character in current) byName[Convert.ToString(character["name"])] = character;

            List<string> changes = new List<string>();
            List<string> missing = new List<string>();
            List<Dictionary<string, object>> toSend = new List<Dictionary<string, object>>();
            foreach (Dictionary<string, object> reading in readings)
            {
                string name = Convert.ToString(reading["name"]);
                Dictionary<string, object> site_;
                if (!byName.TryGetValue(name, out site_) || !Convert.ToBoolean(site_["found"]))
                {
                    missing.Add(name);
                    continue;
                }
                List<string> parts = new List<string>();
                Compare(parts, "niveau", site_, "level", reading);
                Compare(parts, "oméga", site_, "omega", reading);
                Compare(parts, "succès", site_, "achievementPoints", reading);
                if (parts.Count == 0) continue;
                changes.Add(name + " : " + string.Join(", ", parts.ToArray()));
                toSend.Add(reading);
            }

            if (toSend.Count == 0)
            {
                siteBusy = false;
                lastMessage = "Le site est déjà à jour."
                    + (missing.Count > 0 ? " Introuvables sur le site : " + string.Join(", ", missing.ToArray()) + "." : "");
                UpdateSiteButton();
                UpdateStatus();
                return;
            }
            string text = "Mettre à jour le site ?\n\n" + string.Join("\n", changes.ToArray());
            if (missing.Count > 0) text += "\n\nIntrouvables sur le site (ignorés) : " + string.Join(", ", missing.ToArray());
            if (MessageBox.Show(this, text, "Envoyer au site", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.Yes) != MessageBoxResult.Yes)
            {
                siteBusy = false;
                lastMessage = "Envoi annulé.";
                UpdateSiteButton();
                UpdateStatus();
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    int updated = SiteLink.Apply(site, toSend);
                    result = updated + " perso(s) mis à jour sur le site.";
                }
                catch (Exception ex)
                {
                    result = "Envoi impossible : " + ex.Message;
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

        // « niveau 199 → 200 » quand la valeur relevee differe de celle du site.
        private static void Compare(List<string> parts, string label, Dictionary<string, object> site,
            string key, Dictionary<string, object> reading)
        {
            object read;
            if (!reading.TryGetValue(key, out read)) return;
            object before;
            site.TryGetValue(key, out before);
            int newValue = Convert.ToInt32(read);
            if (before != null && Convert.ToInt32(before) == newValue) return;
            CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");
            parts.Add(label + " " + (before == null ? "—" : Convert.ToInt32(before).ToString("N0", french))
                + " → " + newValue.ToString("N0", french));
        }

        private static void SetBusy(Button button, string text)
        {
            if (button == null) return;
            button.IsEnabled = false;
            button.Content = text;
        }
    }
}
