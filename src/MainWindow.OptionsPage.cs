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
    // Onglet Options, et les elements de reglage communs aux onglets.
    internal partial class MainWindow
    {
        // Onglet Options : fenetres, demarrage, outils.
        private FrameworkElement BuildOptionsPage()
        {
            StackPanel page = new StackPanel();
            page.Margin = new Thickness(22, 20, 22, 20);
            page.Width = 560;
            page.HorizontalAlignment = HorizontalAlignment.Left;
            page.VerticalAlignment = VerticalAlignment.Top;

            page.Children.Add(MakeSectionTitle("Fenêtres"));
            page.Children.Add(MakeOption("Renommer les fenêtres avec le nom du personnage", settings.Rename,
                delegate(bool value)
                {
                    settings.Rename = value;
                    settings.Save();
                    RefreshAll(true);
                }));
            page.Children.Add(MakeOption("Ranger la barre des tâches dans l'ordre d'initiative", settings.AutoTaskbar,
                delegate(bool value)
                {
                    settings.AutoTaskbar = value;
                    settings.Save();
                    lastTaskbarOrder = "";
                    RefreshAll(true);
                }));

            page.Children.Add(MakeOption("Mini-barre flottante (ordre du tour toujours visible)",
                settings.MiniBar, delegate(bool value)
                {
                    settings.MiniBar = value;
                    settings.Save();
                    SyncMiniBar();
                }));
            miniVerticalToggle = MakeOption("Mini-barre verticale", settings.MiniVertical, delegate(bool value)
            {
                settings.MiniVertical = value;
                settings.Save();
                SyncMiniBar();
            }, "Un double-clic sur la poignée de la mini-barre change aussi son orientation.");
            page.Children.Add(miniVerticalToggle);

            page.Children.Add(MakeOption("Suivre le tour : amener devant le personnage qui doit jouer",
                settings.AutoFollow, delegate(bool value)
                {
                    settings.AutoFollow = value;
                    settings.Save();
                    if (value) turnWatcher.Start();
                    else turnWatcher.Stop();
                }, "Pour Dofus Retro, l'outil lit les notifications de Windows. À régler une fois :\n"
                    + "1. dans le jeu, activer les notifications en arrière-plan ;\n"
                    + "2. dans Windows, laisser les notifications du jeu activées ;\n"
                    + "3. dans Paramètres › Confidentialité et sécurité › Notifications, autoriser les "
                    + "applications à accéder aux notifications.\n\n"
                    + "Ne s'applique que quand tu es déjà dans Dofus."));

            page.Children.Add(MakeOption("Agrandir automatiquement les fenêtres Dofus Retro à leur ouverture",
                settings.AutoMaximizeRetro, delegate(bool value)
                {
                    settings.AutoMaximizeRetro = value;
                    settings.Save();
                    if (value) MaximizeRetroWindows();
                }));

            if (settings.ScreenReading)
            {
                page.Children.Add(MakeOption("Lire l'écran du jeu et mettre le site à jour",
                    settings.AutoReading, delegate(bool value)
                    {
                        settings.AutoReading = value;
                        settings.Save();
                    }, "L'outil lit à l'écran des fenêtres Dofus le niveau et l'oméga, les succès débloqués et les "
                        + "quêtes terminées (chat, notifications), sans rien envoyer au jeu. Si l'outil est lié au "
                        + "site, ce qui change y part tout seul, au plus une fois par minute. Les messages retenus "
                        + "sont notés dans evenements.txt ; aucune image n'est gardée."));
            }

            AddSiteOptions(page);

            page.Children.Add(MakeSectionTitle("Démarrage"));
            page.Children.Add(MakeOption("Lancer avec Windows, réduit dans la zone de notification",
                IsAutoStartEnabled(), delegate(bool value)
                {
                    SetAutoStart(value);
                    UpdateStatus();
                }));

            updateText = MakeHint("");
            page.Children.Add(updateText);

            page.Children.Add(MakeSectionTitle("Aide"));
            WrapPanel tools = new WrapPanel();
            Button aboutButton = Ui.MakeButton("À propos", false, ShowAbout);
            aboutButton.Margin = new Thickness(0, 0, 8, 8);
            tools.Children.Add(aboutButton);
            page.Children.Add(tools);
            return page;
        }

        // Titre de section : petites capitales vertes, pour se detacher des lignes de reglage.
        // "info" ajoute un « i » dont l'infobulle explique la section.
        private static FrameworkElement MakeSectionTitle(string text, string info = null)
        {
            StackPanel title = new StackPanel();
            title.Orientation = Orientation.Horizontal;
            title.Margin = new Thickness(0, 10, 0, 14);
            TextBlock label = Ui.Text(text.ToUpper(), 12, true, Ui.ColAccent);
            label.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(label);
            if (info != null) title.Children.Add(Ui.InfoBadge(info));
            return title;
        }

        private static TextBlock MakeHint(string text)
        {
            TextBlock block = Ui.Text(text, 12.5, false, Ui.ColMuted);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 2, 0, 18);
            return block;
        }

        private static FrameworkElement MakeSettingRow(string label, Button field)
        {
            DockPanel row = new DockPanel();
            row.Margin = new Thickness(0, 0, 0, 10);
            field.Width = 240;
            DockPanel.SetDock(field, Dock.Right);
            row.Children.Add(field);
            TextBlock text = Ui.Text(label, 14, false, Ui.ColText);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            return row;
        }

        // Case a cocher d'option ; "info" ajoute un « i » et la meme infobulle sur toute la ligne.
        private static ToggleButton MakeOption(string label, bool value, Action<bool> changed, string info = null)
        {
            ToggleButton toggle = Ui.MakeCheck(label, value, changed);
            toggle.Margin = new Thickness(0, 0, 0, 12);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            if (info != null)
            {
                StackPanel content = new StackPanel();
                content.Orientation = Orientation.Horizontal;
                TextBlock text = new TextBlock();
                text.Text = label;
                text.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(text);
                content.Children.Add(Ui.InfoBadge(info));
                toggle.Content = content;
                Ui.SetTip(toggle, info);
            }
            return toggle;
        }
    }
}
