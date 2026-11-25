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
    // Interface : cadre de fenetre, en-tete, onglets et barre d'etat. Chaque onglet a son fichier
    // (MainWindow.TeamPage, .ShortcutsPage, .OptionsPage), la mini-barre aussi (.MiniBarSync).
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
            // Pastilles a droite : portee des raccourcis, puis etat de la liaison au site (si l'outil est lie).
            StackPanel indicators = new StackPanel();
            indicators.VerticalAlignment = VerticalAlignment.Center;
            indicators.Children.Add(MakeIndicator(new Border { Background = Ui.Paint(Ui.ColAccent) }, scopeText));
            linkIndicator = MakeIndicator(linkDot, linkStateText);
            linkIndicator.Margin = new Thickness(0, 4, 0, 0);
            indicators.Children.Add(linkIndicator);
            DockPanel.SetDock(indicators, Dock.Right);
            footerLine.Children.Add(indicators);

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

        // Une pastille et son texte, sur une ligne.
        private static StackPanel MakeIndicator(Border dot, TextBlock text)
        {
            StackPanel line = new StackPanel();
            line.Orientation = Orientation.Horizontal;
            dot.Width = 8;
            dot.Height = 8;
            dot.CornerRadius = new CornerRadius(4);
            dot.Margin = new Thickness(0, 0, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(dot);
            text.FontSize = 13;
            text.Foreground = Ui.Paint(Ui.ColMuted);
            text.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(text);
            return line;
        }

        private void UpdateStatus()
        {
            countText.Text = windows.Count + " fenêtre(s) ouverte(s) · " + CycleList().Count + " dans le cycle";
            previousChip.Text = settings.Previous.ToString();
            nextChip.Text = settings.Next.ToString();
            scopeText.Text = settings.OnlyInDofus ? "Raccourcis actifs dans Dofus" : "Raccourcis actifs partout";
            bool linked = SiteLink.IsLinkedTo(settings.SiteUrl);
            bool refused = linked && SiteLink.Refused;
            if (linkIndicator != null) linkIndicator.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            bool unreachable = linked && !refused && siteUnreachable;
            linkDot.Background = Ui.Paint(refused || unreachable ? Ui.ColWarning : Ui.ColAccent);
            linkStateText.Text = refused ? "Liaison refusée" : unreachable ? "Site injoignable" : "Liaison active";

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
    }
}
