using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DofuxyaManager
{
    // Liaison avec le compte dofuxya.com (F-50), pour tous les joueurs et facultative : l'outil marche
    // pareil sans. Une fois lie, il lit les persos du compte (nom en vert, lien vers la fiche) et
    // peut rappeler chaque jour l'Almanax aux persos qui ne l'ont pas fait (option, desactivee par defaut).
    internal partial class MainWindow
    {
        // Persos du compte lie, par jeu et nom (« Dofus 3\tKeax », sans tenir compte des majuscules).
        private Dictionary<string, SiteCharacter> siteCharacters =
            new Dictionary<string, SiteCharacter>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer siteTimer = new DispatcherTimer();
        private bool siteBusy;
        // Vrai quand la derniere lecture des persos n'a pas joint le site (reseau coupe, site arrete…).
        private bool siteUnreachable;
        private bool linking;
        private Button linkButton;
        private TextBlock linkText;
        // Action du prochain clic sur une bulle de la zone de notification (null : ouvrir l'outil).
        private Action balloonAction;

        private void StartSite()
        {
            if (tray != null)
            {
                tray.BalloonTipClicked += delegate
                {
                    Action action = balloonAction;
                    balloonAction = null;
                    if (action != null) action();
                };
                tray.BalloonTipClosed += delegate { balloonAction = null; };
            }
            // Toutes les 10 minutes : persos du site et, au premier passage du jour, l'Almanax.
            siteTimer.Interval = TimeSpan.FromMinutes(10);
            siteTimer.Tick += delegate { RefreshSite(true); };
            siteTimer.Start();
            DispatcherTimer first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            first.Tick += delegate
            {
                first.Stop();
                RefreshSite(true);
            };
            first.Start();
        }

        // Relit les persos du compte lie et, si besoin, l'Almanax du jour. Sans effet si l'outil n'est
        // pas lie ou qu'une lecture est en cours. Si le site ne repond pas, la pastille le signale
        // (prochain essai dans 10 minutes).
        private void RefreshSite(bool almanax)
        {
            if (siteBusy || linking || !SiteLink.IsLinkedTo(settings.SiteUrl)) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            bool wantAlmanax = almanax && settings.AlmanaxReminder && settings.AlmanaxSeen != today;
            siteBusy = true;
            string site = settings.SiteUrl;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<SiteCharacter> characters = null;
                AlmanaxDay day = null;
                bool revoked = false;
                bool unreachable = false;
                try
                {
                    characters = SiteLink.Characters(site);
                    if (wantAlmanax) day = SiteLink.Almanax(site);
                }
                catch (SiteLink.SiteError error)
                {
                    revoked = error.Status == 401;
                    // Pas de reponse, ou erreur du serveur : le site n'est pas joignable pour l'instant.
                    unreachable = characters == null && (error.Status == 0 || error.Status >= 500);
                }
                catch (Exception)
                {
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    siteBusy = false;
                    if (characters != null) siteUnreachable = false;
                    else if (unreachable || revoked) siteUnreachable = unreachable;
                    if (revoked)
                    {
                        siteCharacters.Clear();
                        lastMessage = "Le site refuse la liaison de l'outil : relie-le depuis l'onglet Options.";
                        UpdateSiteUi();
                        RebuildRows();
                        UpdateStatus();
                        return;
                    }
                    UpdateSiteUi();
                    UpdateStatus();
                    if (characters != null) UseSiteCharacters(characters);
                    if (day != null) RemindAlmanax(day);
                }));
            });
        }

        private void UseSiteCharacters(List<SiteCharacter> characters)
        {
            Dictionary<string, SiteCharacter> next = new Dictionary<string, SiteCharacter>(StringComparer.OrdinalIgnoreCase);
            foreach (SiteCharacter character in characters) next[character.Game + "\t" + character.Name] = character;
            bool changed = next.Count != siteCharacters.Count;
            foreach (KeyValuePair<string, SiteCharacter> pair in next)
            {
                SiteCharacter known;
                if (!siteCharacters.TryGetValue(pair.Key, out known) || known.Id != pair.Value.Id) changed = true;
            }
            siteCharacters = next;
            if (changed) RebuildRows();
        }

        // Perso du site qui correspond a la fenetre (meme jeu, meme nom), ou null.
        private SiteCharacter SiteCharacterOf(GameWindow window)
        {
            SiteCharacter character;
            return window.IsNamed && siteCharacters.TryGetValue(window.Key, out character) ? character : null;
        }

        private void OpenCharacterSheet(SiteCharacter character)
        {
            string game = character.Game == Scanner.Retro ? "retro" : "dofus";
            OpenSite("/" + game + "/personnages?personnage=" + Uri.EscapeDataString(character.Id));
        }

        private void OpenSite(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(settings.SiteUrl + path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                lastMessage = "Impossible d'ouvrir le navigateur : " + ex.Message;
                UpdateStatus();
            }
        }

        // ---------- Almanax ----------

        // Une bulle par jour, seulement s'il reste des persos qui n'ont pas fait l'Almanax.
        private void RemindAlmanax(AlmanaxDay day)
        {
            settings.AlmanaxSeen = DateTime.Now.ToString("yyyy-MM-dd");
            settings.Save();
            if (day.Missing.Count == 0 || tray == null || day.Item.Length == 0) return;
            string offering = (day.Quantity > 1 ? day.Quantity + " × " : "") + day.Item;
            if (day.Bonus.Length > 0) offering += " · " + day.Bonus;
            int shown = Math.Min(3, day.Missing.Count);
            string names = string.Join(", ", day.Missing.GetRange(0, shown).ToArray());
            if (day.Missing.Count > shown) names += " et " + (day.Missing.Count - shown) + " autre(s)";
            try
            {
                balloonAction = delegate { OpenSite("/dofus/almanax"); };
                tray.ShowBalloonTip(10000, "Almanax du jour", offering + "\nÀ faire : " + names + ".",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception)
            {
                balloonAction = null;
            }
        }

        // ---------- Onglet Options ----------

        private void AddSiteOptions(StackPanel page)
        {
            page.Children.Add(MakeSectionTitle("Site dofuxya.com",
                "Facultatif. Une fois l'outil lié à ton compte du site, chaque fenêtre est reliée à ton "
                + "personnage du même nom : son nom s'affiche en vert dans l'onglet Équipe et un clic dessus "
                + "ouvre sa fiche. L'outil ne lit que la liste de tes personnages et l'Almanax du jour."));
            DockPanel row = new DockPanel();
            row.Margin = new Thickness(0, 0, 0, 12);
            linkButton = Ui.MakeButton("", false, OnLinkButton);
            linkButton.Width = 160;
            DockPanel.SetDock(linkButton, Dock.Right);
            row.Children.Add(linkButton);
            linkText = Ui.Text("", 14, false, Ui.ColText);
            linkText.VerticalAlignment = VerticalAlignment.Center;
            linkText.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(linkText);
            page.Children.Add(row);
            page.Children.Add(MakeOption("Rappeler l'Almanax du jour (une bulle par jour)",
                settings.AlmanaxReminder, delegate(bool value)
                {
                    settings.AlmanaxReminder = value;
                    settings.Save();
                    if (value) RefreshSite(true);
                }, "Au premier lancement de la journée, une bulle donne l'offrande et le bonus du jour, et les "
                    + "personnages Dofus de ton compte qui n'ont pas encore fait l'Almanax. Un clic sur la bulle "
                    + "ouvre la page Almanax du site. Rien ne s'affiche si tout est fait."));
            UpdateSiteUi();
        }

        private void UpdateSiteUi()
        {
            if (linkButton == null) return;
            bool linked = SiteLink.IsLinkedTo(settings.SiteUrl);
            bool refused = linked && SiteLink.Refused;
            linkButton.IsEnabled = !linking;
            linkButton.Content = linking ? "En attente…" : refused ? "Relier" : linked ? "Délier" : "Lier au site";
            linkText.Text = linking
                ? "Autorise l'outil dans la page ouverte dans ton navigateur."
                : refused ? "Le site refuse la liaison (appareil retiré de ton profil ?)."
                : linked ? "Lié à ton compte du site." : "Non lié.";
        }

        private void OnLinkButton()
        {
            if (linking) return;
            // Liaison refusee par le site : on relie (la nouvelle cle remplace l'ancienne).
            if (SiteLink.IsLinkedTo(settings.SiteUrl) && !SiteLink.Refused)
            {
                SiteLink.Unlink();
                siteCharacters.Clear();
                siteUnreachable = false;
                lastMessage = "Outil délié du site. Tu peux aussi retirer l'accès depuis ton profil sur le site.";
                UpdateSiteUi();
                RebuildRows();
                UpdateStatus();
                return;
            }
            linking = true;
            UpdateSiteUi();
            string site = settings.SiteUrl;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                bool linked = false;
                try
                {
                    linked = SiteLink.Link(site, delegate { return quitting; });
                    result = linked
                        ? "Outil lié à ton compte du site."
                        : "Liaison abandonnée : rien n'a été autorisé dans les 10 minutes.";
                }
                catch (Exception ex)
                {
                    result = "Liaison impossible : " + ex.Message;
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    linking = false;
                    lastMessage = result;
                    UpdateSiteUi();
                    UpdateStatus();
                    if (linked) RefreshSite(true);
                }));
            });
        }
    }
}
