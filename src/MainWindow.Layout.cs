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
    // Interface : en-tete, onglets, cartes des personnages, mini-barre.
    internal partial class MainWindow
    {
        // ---------- Interface ----------

        // Cadre de fenetre personnalise : l'en-tete sert de barre de titre.
        private void ApplyChrome()
        {
            try
            {
                WindowChrome chrome = new WindowChrome();
                chrome.CaptionHeight = 50;
                chrome.ResizeBorderThickness = new Thickness(0);
                chrome.GlassFrameThickness = new Thickness(0, 0, 0, 1);
                chrome.CornerRadius = new CornerRadius(0);
                chrome.UseAeroCaptionButtons = false;
                WindowChrome.SetWindowChrome(this, chrome);
                StateChanged += delegate
                {
                    // Une fenetre agrandie deborde de l'ecran de quelques pixels : on compense.
                    if (outerBorder != null)
                    {
                        outerBorder.Margin = WindowState == WindowState.Maximized
                            ? new Thickness(7) : new Thickness(0);
                    }
                };
            }
            catch (Exception)
            {
            }
        }

        private UIElement BuildLayout()
        {
            DockPanel root = new DockPanel();

            // En-tete : logo, titre, boutons de fenetre.
            Border header = new Border();
            header.Background = Ui.Paint(Ui.ColDeep);
            header.Height = 56;
            header.Padding = new Thickness(16, 0, 6, 0);
            DockPanel headerPanel = new DockPanel();

            StackPanel windowButtons = new StackPanel();
            windowButtons.Orientation = Orientation.Horizontal;
            windowButtons.VerticalAlignment = VerticalAlignment.Center;
            windowButtons.Children.Add(MakeWindowButton("M 0,6 L 11,6", "Réduire",
                delegate { WindowState = WindowState.Minimized; }));
            windowButtons.Children.Add(MakeWindowButton("M 0,0 L 11,11 M 11,0 L 0,11",
                "Masquer (l'outil reste actif dans la zone de notification)", delegate { Close(); }));
            DockPanel.SetDock(windowButtons, Dock.Right);
            headerPanel.Children.Add(windowButtons);

            ImageSource logo = LoadLogo();
            if (logo != null)
            {
                Image logoImage = new Image();
                logoImage.Source = logo;
                logoImage.Width = 28;
                logoImage.Height = 28;
                logoImage.Margin = new Thickness(0, 0, 12, 0);
                logoImage.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(logoImage, Dock.Left);
                headerPanel.Children.Add(logoImage);
            }
            TextBlock title = Ui.Text("Dofuxya", 20, true, Ui.ColText);
            title.VerticalAlignment = VerticalAlignment.Center;
            headerPanel.Children.Add(title);
            header.Child = headerPanel;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Onglets.
            Border tabsBar = new Border();
            tabsBar.Background = Ui.Paint(Ui.ColDeep);
            tabsBar.BorderBrush = Ui.Paint(Ui.ColLine);
            tabsBar.BorderThickness = new Thickness(0, 0, 0, 1);
            tabsBar.Padding = new Thickness(12, 0, 12, 0);
            StackPanel tabsPanel = new StackPanel();
            tabsPanel.Orientation = Orientation.Horizontal;
            AddTab(tabsPanel, 0, "Équipe");
            AddTab(tabsPanel, 1, "Raccourcis");
            AddTab(tabsPanel, 2, "Options");
            tabsBar.Child = tabsPanel;
            DockPanel.SetDock(tabsBar, Dock.Top);
            root.Children.Add(tabsBar);

            // Pied : rappel des raccourcis, portee, messages.
            Border footer = new Border();
            footer.Background = Ui.Paint(Ui.ColDeep);
            footer.BorderBrush = Ui.Paint(Ui.ColLine);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(18, 12, 18, 12);
            StackPanel footerPanel = new StackPanel();

            DockPanel footerLine = new DockPanel();
            StackPanel scope = new StackPanel();
            scope.Orientation = Orientation.Horizontal;
            scope.VerticalAlignment = VerticalAlignment.Center;
            Border dot = new Border();
            dot.Width = 8;
            dot.Height = 8;
            dot.CornerRadius = new CornerRadius(4);
            dot.Background = Ui.Paint(Ui.ColAccent);
            dot.Margin = new Thickness(0, 0, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            scope.Children.Add(dot);
            scopeText.FontSize = 13;
            scopeText.Foreground = Ui.Paint(Ui.ColMuted);
            scopeText.VerticalAlignment = VerticalAlignment.Center;
            scope.Children.Add(scopeText);
            DockPanel.SetDock(scope, Dock.Right);
            footerLine.Children.Add(scope);

            StackPanel recap = new StackPanel();
            recap.Orientation = Orientation.Horizontal;
            recap.Children.Add(MakeChip(previousChip));
            recap.Children.Add(MakeFooterLabel("Précédent", 18));
            recap.Children.Add(MakeChip(nextChip));
            recap.Children.Add(MakeFooterLabel("Suivant", 0));
            footerLine.Children.Add(recap);
            footerPanel.Children.Add(footerLine);

            statusText.FontSize = 12;
            statusText.Foreground = Ui.Paint(Ui.ColMuted);
            statusText.TextWrapping = TextWrapping.Wrap;
            statusText.MaxWidth = 600;
            statusText.HorizontalAlignment = HorizontalAlignment.Left;
            statusText.Margin = new Thickness(0, 8, 0, 0);
            footerPanel.Children.Add(statusText);

            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Pages.
            Grid pages = new Grid();
            tabPages[0] = BuildTeamPage();
            tabPages[1] = BuildShortcutsPage();
            tabPages[2] = BuildOptionsPage();
            for (int i = 0; i < tabPages.Length; i++) pages.Children.Add(tabPages[i]);
            root.Children.Add(pages);
            SelectTab(0);

            outerBorder = new Border();
            outerBorder.BorderBrush = Ui.Paint(Ui.ColLine);
            outerBorder.BorderThickness = new Thickness(1);
            outerBorder.Background = Ui.Paint(Ui.ColGround);
            outerBorder.Child = root;
            return outerBorder;
        }

        private Button MakeWindowButton(string geometry, string tip, Action onClick)
        {
            System.Windows.Shapes.Path glyph = new System.Windows.Shapes.Path();
            glyph.Data = Geometry.Parse(geometry);
            glyph.Stroke = Ui.Paint(Ui.ColMuted);
            glyph.StrokeThickness = 1.6;
            glyph.Width = 11;
            glyph.Height = 11;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;

            Button button = Ui.MakeFlat(null, onClick);
            button.Content = glyph;
            button.Width = 44;
            button.Height = 40;
            button.Padding = new Thickness(0);
            Ui.SetTip(button, tip);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            return button;
        }

        private void AddTab(StackPanel panel, int index, string text)
        {
            Border underline = new Border();
            underline.BorderThickness = new Thickness(0, 0, 0, 3);
            underline.BorderBrush = Brushes.Transparent;
            Button button = Ui.MakeFlat(text, delegate { SelectTab(index); });
            button.FontSize = 15;
            button.Padding = new Thickness(16, 11, 16, 8);
            underline.Child = button;
            tabUnderlines[index] = underline;
            tabButtons[index] = button;
            panel.Children.Add(underline);
        }

        private void SelectTab(int index)
        {
            CancelCapture();
            for (int i = 0; i < tabPages.Length; i++)
            {
                bool selected = i == index;
                if (tabPages[i] != null)
                {
                    tabPages[i].Visibility = selected ? Visibility.Visible : Visibility.Hidden;
                }
                if (tabUnderlines[i] != null)
                {
                    tabUnderlines[i].BorderBrush = selected ? Ui.Paint(Ui.ColAccent) : Brushes.Transparent;
                }
                if (tabButtons[i] != null)
                {
                    tabButtons[i].Foreground = Ui.Paint(selected ? Ui.ColText : Ui.ColMuted);
                }
            }
        }

        private static Border MakeChip(TextBlock text)
        {
            text.FontSize = 12;
            text.FontWeight = FontWeights.Bold;
            text.Foreground = Ui.Paint(Ui.ColText);
            Border chip = new Border();
            chip.Background = Ui.Paint(Ui.ColRow);
            chip.BorderBrush = Ui.Paint(Ui.ColLine);
            chip.BorderThickness = new Thickness(1);
            chip.CornerRadius = new CornerRadius(6);
            chip.Padding = new Thickness(10, 5, 10, 5);
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.Child = text;
            return chip;
        }

        private static TextBlock MakeFooterLabel(string text, double rightMargin)
        {
            TextBlock block = Ui.Text(text, 13, false, Ui.ColMuted);
            block.VerticalAlignment = VerticalAlignment.Center;
            block.Margin = new Thickness(8, 0, rightMargin, 0);
            return block;
        }

        // Onglet Equipe : la liste des personnages ouverts.
        private FrameworkElement BuildTeamPage()
        {
            DockPanel page = new DockPanel();
            Grid.SetIsSharedSizeScope(page, true);

            DockPanel toolbar = new DockPanel();
            toolbar.Margin = new Thickness(18, 14, 18, 10);
            manualButton = Ui.MakeButton("Revenir au tri par initiative", false, delegate
            {
                ClearManualOrder();
                RefreshAll(true);
            });
            Ui.SetTip(manualButton, "L'ordre a été fixé à la main. Ce bouton rétablit le tri par initiative.");
            manualButton.Visibility = Visibility.Collapsed;

            Button groupButton = Ui.MakeButton("GROUPER L'ÉQUIPE", true, GroupTeam);
            Ui.SetTip(groupButton, "Amène le personnage principal devant et copie les commandes /invite des autres "
                + "personnages détectés. Colle-les ensuite dans le chat (Ctrl + V, puis Entrée).");
            groupButton.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(groupButton, Dock.Right);
            toolbar.Children.Add(groupButton);
            manualButton.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(manualButton, Dock.Right);
            toolbar.Children.Add(manualButton);
            countText.FontSize = 14;
            countText.FontWeight = FontWeights.SemiBold;
            countText.Foreground = Ui.Paint(Ui.ColMuted);
            countText.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(countText);
            DockPanel.SetDock(toolbar, Dock.Top);
            page.Children.Add(toolbar);

            Grid columns = new Grid();
            columns.Margin = new Thickness(31, 0, 31, 6);
            AddColumns(columns);
            AddHeaderCell(columns, 0, "#");
            AddHeaderCell(columns, 2, "PERSONNAGE");
            AddHeaderCell(columns, 3, "INITIATIVE");
            AddHeaderCell(columns, 4, "RACCOURCI");
            AddHeaderCell(columns, 5, "CYCLE");
            DockPanel.SetDock(columns, Dock.Top);
            page.Children.Add(columns);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.Padding = new Thickness(18, 0, 18, 12);
            scroll.Content = rowsPanel;
            page.Children.Add(scroll);
            return page;
        }

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

        // Titre de section : petites capitales vertes suivies d'un trait, pour se detacher des lignes
        // de reglage. "info" ajoute un « i » dont l'infobulle explique la section.
        private static FrameworkElement MakeSectionTitle(string text, string info = null)
        {
            DockPanel title = new DockPanel();
            title.Margin = new Thickness(0, 10, 0, 14);
            TextBlock label = Ui.Text(text.ToUpper(), 12, true, Ui.ColAccent);
            label.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(label, Dock.Left);
            title.Children.Add(label);
            if (info != null)
            {
                FrameworkElement badge = Ui.InfoBadge(info);
                DockPanel.SetDock(badge, Dock.Left);
                title.Children.Add(badge);
            }
            Border line = new Border();
            line.Height = 1;
            line.Background = Ui.Paint(Ui.ColLine);
            line.VerticalAlignment = VerticalAlignment.Center;
            line.Margin = new Thickness(12, 1, 0, 0);
            title.Children.Add(line);
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

        private static void AddColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            ColumnDefinition nameColumn = new ColumnDefinition();
            nameColumn.Width = GridLength.Auto;
            nameColumn.MinWidth = 150;
            nameColumn.SharedSizeGroup = "nom";
            grid.ColumnDefinitions.Add(nameColumn);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        }

        private static void AddHeaderCell(Grid grid, int column, string text)
        {
            TextBlock cell = Ui.Text(text, 11, true, Ui.ColMuted);
            if (column >= 3) cell.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private void RebuildRows()
        {
            CancelCapture();
            dragWindow = null;
            rowsPanel.Children.Clear();
            rowBorders.Clear();
            rowRanks.Clear();
            if (windows.Count == 0)
            {
                TextBlock empty = Ui.Text(
                    "Aucune fenêtre Dofus détectée. Lance le jeu : la liste se met à jour toute seule.",
                    13, false, Ui.ColMuted);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.MaxWidth = 560;
                empty.HorizontalAlignment = HorizontalAlignment.Left;
                empty.Margin = new Thickness(4, 8, 4, 0);
                rowsPanel.Children.Add(empty);
                SyncMiniBar();
                return;
            }
            int rank = 0;
            foreach (GameWindow window in windows)
            {
                if (window.IsNamed) rank++;
                rowsPanel.Children.Add(BuildRow(window, window.IsNamed ? rank : 0));
            }
            UpdateHighlight();
        }

        private UIElement BuildRow(GameWindow window, int rank)
        {
            bool named = window.IsNamed;

            Border border = new Border();
            border.Background = Ui.Paint(Ui.ColRow);
            border.BorderBrush = Ui.Paint(Ui.ColRow);
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(10);
            border.Margin = new Thickness(0, 0, 0, 6);
            border.Padding = new Thickness(12, 9, 12, 9);
            rowBorders[window] = border;

            Grid grid = new Grid();
            AddColumns(grid);

            // Rang dans l'ordre du tour.
            Border rankBadge = new Border();
            rankBadge.Width = 32;
            rankBadge.Height = 32;
            rankBadge.CornerRadius = new CornerRadius(8);
            rankBadge.Background = Ui.Paint(Ui.ColDeep);
            rankBadge.HorizontalAlignment = HorizontalAlignment.Left;
            rankBadge.VerticalAlignment = VerticalAlignment.Center;
            TextBlock rankText = Ui.Text(rank > 0 ? rank.ToString() : "–", 15, true, Ui.ColMuted);
            rankText.HorizontalAlignment = HorizontalAlignment.Center;
            rankText.VerticalAlignment = VerticalAlignment.Center;
            rankBadge.Child = rankText;
            rowRanks[window] = rankText;
            if (named)
            {
                rankBadge.Cursor = Cursors.SizeNS;
                Ui.SetTip(rankBadge, "Glisser pour changer l'ordre à la main.");
                rankBadge.MouseLeftButtonDown += delegate(object dragSender, MouseButtonEventArgs dragArgs)
                {
                    BeginDrag(window, rankBadge, dragArgs);
                };
                rankBadge.MouseMove += delegate(object dragSender, MouseEventArgs dragArgs)
                {
                    ContinueDrag(dragArgs);
                };
                rankBadge.MouseLeftButtonUp += delegate(object dragSender, MouseButtonEventArgs dragArgs)
                {
                    EndDrag(rankBadge, dragArgs);
                };
            }
            Grid.SetColumn(rankBadge, 0);
            grid.Children.Add(rankBadge);

            // Icone de classe : un clic ouvre le choix de l'icone.
            Button iconButton = Ui.MakeFlat(null, null);
            iconButton.Width = 46;
            iconButton.Height = 46;
            iconButton.Padding = new Thickness(0);
            iconButton.HorizontalAlignment = HorizontalAlignment.Left;
            iconButton.IsEnabled = named && icons.Keys.Count > 0;
            Ui.SetTip(iconButton, "Changer l'icône. « (auto) » utilise la classe du personnage.");
            ImageSource preview = icons.GetPreview(window.IconKey);
            if (preview != null)
            {
                Image image = new Image();
                image.Source = preview;
                image.Width = 40;
                image.Height = 40;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                iconButton.Content = image;
            }
            else
            {
                Border disc = new Border();
                disc.Width = 40;
                disc.Height = 40;
                disc.CornerRadius = new CornerRadius(20);
                disc.Background = Ui.Paint(Ui.ColDeep);
                string initials = named ? window.Name.Substring(0, Math.Min(2, window.Name.Length)) : "?";
                TextBlock initialsText = Ui.Text(initials, 14, true, Ui.ColMuted);
                initialsText.HorizontalAlignment = HorizontalAlignment.Center;
                initialsText.VerticalAlignment = VerticalAlignment.Center;
                disc.Child = initialsText;
                iconButton.Content = disc;
            }
            iconButton.Click += delegate { OpenIconMenu(iconButton, window); };
            Grid.SetColumn(iconButton, 1);
            grid.Children.Add(iconButton);

            StackPanel names = new StackPanel();
            names.VerticalAlignment = VerticalAlignment.Center;
            names.Margin = new Thickness(0, 0, 14, 0);
            TextBlock nameText = Ui.Text(named ? window.Name : "(en attente d'un personnage)", 15, true, Ui.ColText);
            names.Children.Add(nameText);
            string detail = window.ClassName.Length > 0 ? window.ClassName + " · " + window.Game : window.Game;
            if (named && window.Key == settings.LeaderKey) detail += " · principal";
            names.Children.Add(Ui.Text(detail, 12, false, Ui.ColMuted));
            Grid.SetColumn(names, 2);
            grid.Children.Add(names);

            TextBox initiative = new TextBox();
            initiative.Text = window.Initiative.ToString();
            initiative.Tag = window;
            initiative.IsEnabled = named;
            initiative.Background = Ui.Paint(Ui.ColField);
            initiative.Foreground = Ui.Paint(Ui.ColText);
            initiative.CaretBrush = Ui.Paint(Ui.ColText);
            initiative.BorderBrush = Ui.Paint(Ui.ColLine);
            initiative.FontSize = 15;
            initiative.FontWeight = FontWeights.Bold;
            initiative.TextAlignment = TextAlignment.Center;
            initiative.Padding = new Thickness(4, 8, 4, 8);
            initiative.Margin = new Thickness(6, 0, 6, 0);
            initiative.VerticalAlignment = VerticalAlignment.Center;
            initiative.LostKeyboardFocus += OnInitiativeLostFocus;
            initiative.KeyDown += OnInitiativeKeyDown;
            Grid.SetColumn(initiative, 3);
            grid.Children.Add(initiative);

            Button direct = Ui.MakeField(window.Direct.ToString(), null);
            direct.FontSize = 12;
            direct.Padding = new Thickness(4, 8, 4, 8);
            direct.IsEnabled = named;
            direct.Margin = new Thickness(6, 0, 6, 0);
            Ui.SetTip(direct, "Raccourci qui amène directement cette fenêtre au premier plan.");
            direct.Click += delegate
            {
                StartCapture(direct, delegate(Hotkey hotkey)
                {
                    window.Direct = hotkey;
                    store.Save(window);
                });
            };
            Grid.SetColumn(direct, 4);
            grid.Children.Add(direct);

            ToggleButton cycle = Ui.MakeCheck(null, window.InCycle, delegate(bool value)
            {
                window.InCycle = value;
                store.Save(window);
                UpdateStatus();
            });
            cycle.HorizontalAlignment = HorizontalAlignment.Center;
            Ui.SetTip(cycle, "Inclure dans le cycle suivante / précédente.");
            Grid.SetColumn(cycle, 5);
            grid.Children.Add(cycle);

            border.Child = grid;
            return border;
        }

        private void OpenIconMenu(Button anchor, GameWindow window)
        {
            ContextMenu menu = new ContextMenu();
            MenuItem auto = new MenuItem();
            auto.Header = "(auto)";
            auto.Click += delegate { ChooseIcon(window, ""); };
            menu.Items.Add(auto);
            foreach (string key in icons.Keys)
            {
                string choice = key;
                MenuItem item = new MenuItem();
                item.Header = choice;
                ImageSource preview = icons.GetPreview(choice);
                if (preview != null)
                {
                    Image image = new Image();
                    image.Source = preview;
                    image.Width = 18;
                    image.Height = 18;
                    item.Icon = image;
                }
                item.Click += delegate { ChooseIcon(window, choice); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ChooseIcon(GameWindow window, string key)
        {
            if (key == window.IconOverride) return;
            window.IconOverride = key;
            store.Save(window);
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAll(true); }));
        }

        private void OnInitiativeKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitInitiative((TextBox)sender);
                e.Handled = true;
            }
        }

        private void OnInitiativeLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitInitiative((TextBox)sender);
        }

        private void CommitInitiative(TextBox box)
        {
            GameWindow window = box.Tag as GameWindow;
            if (window == null) return;
            int value;
            if (!int.TryParse(box.Text.Trim(), out value) || value < 0)
            {
                box.Text = window.Initiative.ToString();
                return;
            }
            if (value == window.Initiative) return;
            window.Initiative = value;
            store.Save(window);
            ClearManualOrder();
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAll(true); }));
        }

        private void UpdateStatus()
        {
            countText.Text = windows.Count + " fenêtre(s) ouverte(s) · " + CycleList().Count + " dans le cycle";
            previousChip.Text = settings.Previous.ToString();
            nextChip.Text = settings.Next.ToString();
            scopeText.Text = settings.OnlyInDofus ? "Raccourcis actifs dans Dofus" : "Raccourcis actifs partout";

            string text = refusedText;
            if (lastMessage.Length > 0) text += (text.Length > 0 ? "\n" : "") + lastMessage;
            statusText.Text = text;
            statusText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (manualButton != null)
            {
                manualButton.Visibility = IsManualOrder() ? Visibility.Visible : Visibility.Collapsed;
            }
            if (leaderButton != null) leaderButton.Content = LeaderName();
            if (updateText != null)
            {
                updateText.Text = "Version " + Version + " · mise à jour : " + Updater.State;
            }
        }

        // Met en valeur la ligne de la derniere fenetre Dofus active.
        private void UpdateHighlight()
        {
            foreach (KeyValuePair<GameWindow, Border> pair in rowBorders)
            {
                bool active = pair.Key.Handle == lastActive;
                pair.Value.BorderBrush = Ui.Paint(active ? Ui.ColAccent : Ui.ColRow);
                pair.Value.Background = Ui.Paint(active ? Ui.ColRowActive : Ui.ColRow);
                TextBlock rankText;
                if (rowRanks.TryGetValue(pair.Key, out rankText))
                {
                    rankText.Foreground = Ui.Paint(active ? Ui.ColAccent : Ui.ColMuted);
                }
            }
            SyncMiniBar();
        }

        // ---------- Mini-barre flottante ----------

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
