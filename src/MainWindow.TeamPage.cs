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
    // Onglet Equipe : une ligne par fenetre de jeu (rang, icone, nom, initiative).
    internal partial class MainWindow
    {
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

        // Derniere colonne (Cycle) : la case y est calee a droite, a la meme distance du bord de la
        // carte que le rang a gauche.
        private const double CycleColumn = 44;

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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CycleColumn) });
        }

        private static void AddHeaderCell(Grid grid, int column, string text)
        {
            TextBlock cell = Ui.Text(text, 11, true, Ui.ColMuted);
            if (column >= 3) cell.HorizontalAlignment = HorizontalAlignment.Center;
            if (column == 0)
            {
                // Centre au-dessus des pastilles de rang (32 px, calees a gauche).
                cell.Width = 32;
                cell.TextAlignment = TextAlignment.Center;
                cell.HorizontalAlignment = HorizontalAlignment.Left;
            }
            if (column == 5)
            {
                // Centre au-dessus de la case (22 px, calee a droite de la colonne).
                cell.Width = CycleColumn;
                cell.TextAlignment = TextAlignment.Center;
                cell.HorizontalAlignment = HorizontalAlignment.Left;
                cell.Margin = new Thickness(CycleColumn / 2 - 11, 0, 11 - CycleColumn / 2, 0);
            }
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
            SiteCharacter siteCharacter = SiteCharacterOf(window);
            if (siteCharacter != null)
            {
                // Perso du compte lie : nom en vert, un clic dessus ouvre sa fiche sur le site.
                nameText.Foreground = Ui.Paint(Ui.ColAccent);
                nameText.Cursor = Cursors.Hand;
                Ui.SetTip(nameText, "Ouvrir sa fiche sur le site.");
                nameText.MouseEnter += delegate { nameText.TextDecorations = TextDecorations.Underline; };
                nameText.MouseLeave += delegate { nameText.TextDecorations = null; };
                nameText.MouseLeftButtonUp += delegate { OpenCharacterSheet(siteCharacter); };
            }
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
            cycle.HorizontalAlignment = HorizontalAlignment.Right;
            // La case garde 8 px a droite pour un libelle (absent ici) : on les reprend.
            cycle.Margin = new Thickness(0, 0, -8, 0);
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
    }
}
