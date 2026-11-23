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
    // Onglet Raccourcis.
    internal partial class MainWindow
    {
        // Onglet Raccourcis : navigation et portee.
        private FrameworkElement BuildShortcutsPage()
        {
            StackPanel page = new StackPanel();
            page.Margin = new Thickness(22, 20, 22, 20);
            page.Width = 560;
            page.HorizontalAlignment = HorizontalAlignment.Left;
            page.VerticalAlignment = VerticalAlignment.Top;
            page.Children.Add(MakeSectionTitle("Navigation",
                "Clique sur un champ puis appuie sur une touche, une combinaison, un bouton latéral de la souris "
                + "ou le clic molette. Échap annule, Retour arrière ou Suppr efface.\n\n"
                + "Le raccourci propre à chaque personnage se règle dans l'onglet Équipe."));

            previousButton = Ui.MakeField(settings.Previous.ToString(), null);
            previousButton.Click += delegate
            {
                StartCapture(previousButton, delegate(Hotkey hotkey)
                {
                    settings.Previous = hotkey;
                    settings.Save();
                });
            };
            page.Children.Add(MakeSettingRow("Fenêtre précédente", previousButton));

            nextButton = Ui.MakeField(settings.Next.ToString(), null);
            nextButton.Click += delegate
            {
                StartCapture(nextButton, delegate(Hotkey hotkey)
                {
                    settings.Next = hotkey;
                    settings.Save();
                });
            };
            page.Children.Add(MakeSettingRow("Fenêtre suivante", nextButton));

            leaderHotkeyButton = Ui.MakeField(settings.Leader.ToString(), null);
            leaderHotkeyButton.Click += delegate
            {
                StartCapture(leaderHotkeyButton, delegate(Hotkey hotkey)
                {
                    settings.Leader = hotkey;
                    settings.Save();
                });
            };
            page.Children.Add(MakeSettingRow("Aller au personnage principal", leaderHotkeyButton));

            leaderButton = Ui.MakeField(LeaderName(), null);
            leaderButton.Click += delegate { OpenLeaderMenu(); };
            page.Children.Add(MakeSettingRow("Personnage principal", leaderButton));

            page.Children.Add(MakeSectionTitle("Roue des persos",
                "Dans Dofus, maintiens cette touche et fais un clic droit : la roue s'ouvre au curseur. Un clic "
                + "gauche sur un personnage passe sur sa fenêtre.\n\nRetour arrière ou Suppr désactive la roue."));
            wheelKeyButton = Ui.MakeField(settings.WheelKey.ToString(), null);
            wheelKeyButton.Click += delegate
            {
                StartCapture(wheelKeyButton, delegate(Hotkey hotkey)
                {
                    settings.WheelKey = hotkey;
                    settings.Save();
                }, true);
            };
            page.Children.Add(MakeSettingRow("Touche à maintenir + clic droit", wheelKeyButton));

            page.Children.Add(MakeSectionTitle("Portée"));
            page.Children.Add(MakeOption("Raccourcis actifs seulement dans Dofus", settings.OnlyInDofus,
                delegate(bool value)
                {
                    settings.OnlyInDofus = value;
                    settings.Save();
                    SyncKeyboard(true);
                    UpdateStatus();
                }, "Coché, les touches restent libres dans tes autres logiciels. Décoché, elles marchent partout."));
            return page;
        }
    }
}
