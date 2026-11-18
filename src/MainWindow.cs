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
    internal static class Program
    {
        private static bool errorShown;

        // Vrai quand l'outil vient d'etre relance par une mise a jour automatique.
        public static bool JustUpdated;

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            Mutex mutex = new Mutex(true, "DofuxyaManager.InstanceUnique", out created);
            if (!created)
            {
                MessageBox.Show("Dofuxya est déjà lancé (regarde dans la zone de notification).",
                    "Dofuxya");
                return;
            }
            try
            {
                bool hidden = false;
                foreach (string arg in args)
                {
                    if (arg == "--reduit") hidden = true;
                    // Apres une mise a jour, la fenetre se rouvre normalement.
                    if (arg == "--apres-maj") JustUpdated = true;
                }

                // Mise a jour avant d'ouvrir la fenetre. Juste apres une mise a jour, on ne reverifie pas.
                if (!JustUpdated)
                {
                    string setup = UpdateSplash.Run();
                    if (setup.Length > 0 && Updater.Launch(setup))
                    {
                        // L'installateur attend la fin de ce processus, puis relance l'outil.
                        mutex.ReleaseMutex();
                        return;
                    }
                }
                else
                {
                    Updater.State = "mis à jour en version " + AppVersion.Number;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object sender,
                    DispatcherUnhandledExceptionEventArgs e)
                {
                    e.Handled = true;
                    Report(e.Exception);
                };

                MainWindow window = new MainWindow();
                new WindowInteropHelper(window).EnsureHandle();
                if (!hidden) window.Show();
                app.Run();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
            GC.KeepAlive(mutex);
        }

        // Ecrit l'erreur dans un journal et l'affiche une seule fois.
        private static void Report(Exception ex)
        {
            string logPath = Path.Combine(AppPaths.DataFolder, "erreurs.txt");
            try
            {
                File.AppendAllText(logPath, DateTime.Now + "\r\n" + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            if (errorShown) return;
            errorShown = true;
            MessageBox.Show("Une erreur est survenue :\n\n" + ex.Message
                + "\n\nLe détail est enregistré dans :\n" + logPath
                + "\n\nCopie le contenu de ce fichier dans le chat.", "Dofuxya");
        }
    }

    // Un raccourci attribue a une action.
    internal class Shortcut
    {
        public Hotkey Hotkey;
        public string Label;
        public Action Action;
    }

    // Lot 6 : suivi du tour par les notifications Windows, agrandissement automatique (Retro).
    internal class MainWindow : Window
    {
        private const string Version = AppVersion.Number;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "Dofuxya";
        private const string OldRunName = "DofuxyaManager";

        private List<GameWindow> windows = new List<GameWindow>();
        private readonly Store store = new Store();
        private readonly Settings settings = new Settings();
        private readonly IconLibrary icons = new IconLibrary();
        private readonly MouseHook mouseHook = new MouseHook();
        private readonly ForegroundWatcher foregroundWatcher = new ForegroundWatcher();

        private readonly StackPanel rowsPanel = new StackPanel();
        private readonly TextBlock statusText = new TextBlock();
        private readonly TextBlock countText = new TextBlock();
        private readonly Dictionary<GameWindow, Border> rowBorders = new Dictionary<GameWindow, Border>();
        private Button nextButton;
        private Button previousButton;
        private readonly TextBlock scopeText = new TextBlock();
        private readonly TextBlock previousChip = new TextBlock();
        private readonly TextBlock nextChip = new TextBlock();
        private readonly Dictionary<GameWindow, TextBlock> rowRanks = new Dictionary<GameWindow, TextBlock>();
        private readonly FrameworkElement[] tabPages = new FrameworkElement[3];
        private readonly Border[] tabUnderlines = new Border[3];
        private readonly Button[] tabButtons = new Button[3];
        private Border outerBorder;
        private MiniBar miniBar;
        private int iconTick;
        private TextBlock updateText;
        private readonly DispatcherTimer positionTimer = new DispatcherTimer();
        private readonly TurnWatcher turnWatcher = new TurnWatcher();
        private readonly List<string> recentNotifications = new List<string>();
        private int flashCount;
        private readonly DispatcherTimer scanTimer = new DispatcherTimer();
        private int shellMessage;
        private DateTime lastGoTo = DateTime.MinValue;
        private IntPtr lastGoToHandle = IntPtr.Zero;
        private GameWindow dragWindow;
        private Button manualButton;
        private Button leaderButton;
        private Button leaderHotkeyButton;
        private ToggleButton miniVerticalToggle;
        private readonly DispatcherTimer timer = new DispatcherTimer();

        private IntPtr handle = IntPtr.Zero;
        private List<Shortcut> shortcuts = new List<Shortcut>();
        private readonly Dictionary<int, Shortcut> registered = new Dictionary<int, Shortcut>();
        private bool keyboardActive;
        private string refusedText = "";
        private IntPtr lastActive = IntPtr.Zero;
        private string lastMessage = "";
        private string lastTaskbarOrder = "";

        private Button captureButton;
        private Action<Hotkey> captureApply;

        private System.Windows.Forms.NotifyIcon tray;
        private bool quitting;
        private bool trayHintShown;

        public MainWindow()
        {
            Title = "Dofuxya";
            // La fenetre prend exactement la taille de son contenu : rien de rogne, pas de defilement
            // (sauf si la liste depasse la hauteur de l'ecran).
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.CanMinimize;
            MaxHeight = SystemParameters.WorkArea.Height;
            ContentRendered += delegate { InvalidateMeasure(); };

            // La fenetre rouvre la ou elle a ete laissee.
            if (!double.IsNaN(settings.WindowX) && !double.IsNaN(settings.WindowY)
                && settings.WindowX >= SystemParameters.VirtualScreenLeft
                && settings.WindowY >= SystemParameters.VirtualScreenTop
                && settings.WindowX < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
                && settings.WindowY < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = settings.WindowX;
                Top = settings.WindowY;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            positionTimer.Interval = TimeSpan.FromMilliseconds(600);
            positionTimer.Tick += delegate
            {
                positionTimer.Stop();
                SaveWindowPosition();
            };
            LocationChanged += delegate
            {
                // On attend la fin du deplacement avant d'ecrire le reglage.
                positionTimer.Stop();
                positionTimer.Start();
            };
            Background = Ui.Paint(Ui.ColGround);
            Foreground = Ui.Paint(Ui.ColText);
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            LoadIcon();
            Content = BuildLayout();
            ApplyChrome();

            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            Deactivated += delegate { CancelCapture(); };

            CreateTray();
            if (Program.JustUpdated && tray != null)
            {
                try
                {
                    tray.ShowBalloonTip(4000, "Dofuxya",
                        "Mis à jour en version " + Version + ".", System.Windows.Forms.ToolTipIcon.Info);
                }
                catch (Exception)
                {
                }
            }
            // Apres une installation, le chemin de l'exe a pu changer : on remet a jour le demarrage auto
            // (et on reprend l'ancienne entree, nommee DofuxyaManager).
            if (MigrateOldAutoStart() || IsAutoStartEnabled()) SetAutoStart(true);

            // Les installateurs telecharges par les mises a jour passees ne servent plus.
            if (!Program.JustUpdated) Updater.CleanTemp();

            // La detection est declenchee par les evenements de Windows ; cette minuterie lente
            // n'est qu'un filet de securite.
            timer.Interval = TimeSpan.FromSeconds(10);
            scanTimer.Interval = TimeSpan.FromMilliseconds(150);
            scanTimer.Tick += delegate
            {
                scanTimer.Stop();
                RefreshAll(false);
            };
            timer.Tick += delegate { RefreshAll(false); };
            timer.Start();
            RefreshAll(true);
        }

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
            button.ToolTip = tip;
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
            manualButton.ToolTip = "L'ordre a été fixé à la main. Ce bouton rétablit le tri par initiative.";
            manualButton.Visibility = Visibility.Collapsed;

            Button groupButton = Ui.MakeButton("GROUPER L'ÉQUIPE", true, GroupTeam);
            groupButton.ToolTip = "Amène le personnage principal devant et copie les commandes /invite des autres "
                + "personnages détectés. Colle-les ensuite dans le chat (Ctrl + V, puis Entrée).";
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
            page.Children.Add(MakeSectionTitle("Navigation"));

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

            page.Children.Add(MakeHint(
                "Clique sur un champ puis appuie sur une touche, une combinaison, un bouton latéral de la souris "
                + "ou le clic molette. Échap annule, Retour arrière ou Suppr efface. "
                + "Le raccourci propre à chaque personnage se règle dans l'onglet Équipe."));

            page.Children.Add(MakeSectionTitle("Portée"));
            page.Children.Add(MakeOption("Raccourcis actifs seulement dans Dofus", settings.OnlyInDofus,
                delegate(bool value)
                {
                    settings.OnlyInDofus = value;
                    settings.Save();
                    SyncKeyboard(true);
                    UpdateStatus();
                }));
            page.Children.Add(MakeHint(
                "Coché, les touches restent libres dans tes autres logiciels. Décoché, elles marchent partout."));
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
            });
            page.Children.Add(miniVerticalToggle);
            page.Children.Add(MakeHint(
                "Un double-clic sur la poignée de la mini-barre change aussi son orientation."));

            page.Children.Add(MakeOption("Suivre le tour : amener devant le personnage qui doit jouer",
                settings.AutoFollow, delegate(bool value)
                {
                    settings.AutoFollow = value;
                    settings.Save();
                    if (value) turnWatcher.Start();
                    else turnWatcher.Stop();
                }));
            page.Children.Add(MakeHint(
                "Pour Dofus Retro, l'outil lit les notifications de Windows. Il faut : 1) dans le jeu, activer "
                + "les notifications en arrière-plan ; 2) dans Windows, laisser les notifications du jeu "
                + "activées ; 3) dans Paramètres > Confidentialité et sécurité > Notifications, autoriser les "
                + "applications à accéder aux notifications. Ne s'applique que quand tu es déjà dans Dofus."));

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

        private static TextBlock MakeSectionTitle(string text)
        {
            TextBlock block = Ui.Text(text, 16, true, Ui.ColText);
            block.Margin = new Thickness(0, 6, 0, 12);
            return block;
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
            text.FontWeight = FontWeights.SemiBold;
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            return row;
        }

        private static ToggleButton MakeOption(string label, bool value, Action<bool> changed)
        {
            ToggleButton toggle = Ui.MakeCheck(label, value, changed);
            toggle.Margin = new Thickness(0, 0, 0, 12);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
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
                if (window.Name.Length > 0) rank++;
                rowsPanel.Children.Add(BuildRow(window, window.Name.Length > 0 ? rank : 0));
            }
            if (icons.Keys.Count == 0)
            {
                TextBlock hint = Ui.Text(
                    "Aucune icône de classe disponible pour l'instant.",
                    12, false, Ui.ColMuted);
                hint.TextWrapping = TextWrapping.Wrap;
                hint.MaxWidth = 560;
                hint.HorizontalAlignment = HorizontalAlignment.Left;
                hint.Margin = new Thickness(4, 8, 4, 0);
                rowsPanel.Children.Add(hint);
            }
            UpdateHighlight();
        }

        private UIElement BuildRow(GameWindow window, int rank)
        {
            bool named = window.Name.Length > 0;

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
                rankBadge.ToolTip = "Glisser pour changer l'ordre à la main.";
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
            iconButton.ToolTip = "Changer l'icône. « (auto) » utilise la classe du personnage.";
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
            direct.ToolTip = "Raccourci qui amène directement cette fenêtre au premier plan.";
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
            cycle.ToolTip = "Inclure dans le cycle suivante / précédente.";
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
                List<GameWindow> ordered = new List<GameWindow>();
                foreach (GameWindow window in windows)
                {
                    if (window.Name.Length > 0) ordered.Add(window);
                }
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

        // ---------- Détection ----------

        private void RefreshAll(bool force)
        {
            bool changed = false;
            try
            {
                // Le dossier d'icones change rarement : on ne le relit qu'une fois sur six.
                iconTick++;
                bool reloadIcons = force || iconTick >= 6;
                if (reloadIcons) iconTick = 0;
                if (reloadIcons && icons.Reload())
                {
                    changed = true;
                    foreach (GameWindow window in windows) window.AppliedIconKey = null;
                }
                if (Scan()) changed = true;
            }
            catch (Exception ex)
            {
                lastMessage = "Erreur pendant la détection : " + ex.Message;
            }
            if (changed || force)
            {
                SortWindows();
                ApplyIcons(force);
                RebuildRows();
                RebuildShortcuts();
                SyncKeyboard(true);
                AutoReorderTaskbar();
            }
            else
            {
                SyncKeyboard(false);
            }
            UpdateStatus();
        }

        // Met la liste a jour d'apres les fenetres ouvertes. Renvoie true si elle a change.
        private bool Scan()
        {
            bool changed = false;
            bool rename = settings.Rename;
            List<RawWindow> found = Scanner.Enumerate(false);

            foreach (RawWindow raw in found)
            {
                GameWindow window = FindWindow(raw.Handle);

                if (window == null)
                {
                    window = new GameWindow();
                    window.Handle = raw.Handle;
                    window.ProcessName = raw.ProcessName;
                    window.RawTitle = raw.Title;
                    Scanner.Parse(window, raw.Title, raw.ProcessName);
                    store.Load(window);
                    store.SyncClass(window);
                    windows.Add(window);
                    if (settings.AutoMaximizeRetro && window.Game == "Dofus Retro"
                        && !Native.IsZoomed(window.Handle))
                    {
                        Native.ShowWindow(window.Handle, 3);
                    }
                    changed = true;
                }
                else if (raw.Title != window.RawTitle && raw.Title != window.AppliedTitle)
                {
                    // Le jeu (ou un autre outil) a reecrit le titre : nouveau personnage possible.
                    string previousKey = window.Key;
                    string previousClass = window.ClassName;
                    window.RawTitle = raw.Title;
                    window.AppliedTitle = null;
                    window.AppliedIconKey = null;
                    Scanner.Parse(window, raw.Title, raw.ProcessName);
                    if (window.ClassName.Length == 0 && window.Key == previousKey)
                    {
                        window.ClassName = previousClass;
                    }
                    if (window.Key != previousKey)
                    {
                        store.Load(window);
                    }
                    store.SyncClass(window);
                    changed = true;
                }

                if (rename && window.Name.Length > 0 && raw.Title != window.Name)
                {
                    if (Native.SetWindowText(window.Handle, window.Name))
                    {
                        window.AppliedTitle = window.Name;
                    }
                }
            }

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                bool stillOpen = false;
                foreach (RawWindow raw in found)
                {
                    if (raw.Handle == windows[i].Handle)
                    {
                        stillOpen = true;
                        break;
                    }
                }
                if (!stillOpen)
                {
                    windows.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        private GameWindow FindWindow(IntPtr hWnd)
        {
            foreach (GameWindow window in windows)
            {
                if (window.Handle == hWnd) return window;
            }
            return null;
        }

        // Ordre : personnages par initiative decroissante, fenetres sans personnage a la fin.
        private void SortWindows()
        {
            bool manual = IsManualOrder();
            windows = windows
                .OrderBy(delegate(GameWindow w) { return w.Name.Length > 0 ? 0 : 1; })
                .ThenBy(delegate(GameWindow w) { return manual ? w.ManualRank : 0; })
                .ThenByDescending(delegate(GameWindow w) { return w.Initiative; })
                .ThenBy(delegate(GameWindow w) { return w.Name; }, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<GameWindow> CycleList()
        {
            List<GameWindow> list = new List<GameWindow>();
            foreach (GameWindow window in windows)
            {
                // Les fenetres sans personnage (ecran de connexion, choix du personnage) comptent aussi.
                if (window.InCycle) list.Add(window);
            }
            return list;
        }

        // ---------- Icônes de classe ----------

        private void ApplyIcons(bool force)
        {
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length == 0)
                {
                    window.IconKey = "";
                    continue;
                }
                string key;
                if (window.IconOverride.Length > 0 && icons.Has(window.IconOverride))
                {
                    key = window.IconOverride;
                }
                else
                {
                    key = icons.Match(window.ClassName);
                }
                window.IconKey = key;
                if (key.Length == 0) continue;
                if (!force && window.AppliedIconKey == key) continue;
                Native.SetWindowIcon(window.Handle, icons.GetBig(key), icons.GetSmall(key));
                window.AppliedIconKey = key;
            }
        }

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
                if (window.Name.Length == 0) continue;
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
            if (window == null || window.Name.Length == 0) return;
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
                if (window.Name.Length == 0) continue;
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
            foreach (GameWindow window in windows)
            {
                if (window.Game == "Dofus Retro" && !Native.IsZoomed(window.Handle))
                {
                    Native.ShowWindow(window.Handle, 3);
                }
            }
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
                if (window.Name.Length == 0) continue;
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
            GameWindow leader = null;
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length > 0 && window.Key == settings.LeaderKey)
                {
                    leader = window;
                    break;
                }
            }
            if (leader == null)
            {
                foreach (GameWindow window in windows)
                {
                    if (window.Name.Length > 0)
                    {
                        leader = window;
                        break;
                    }
                }
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
                if (window.Name.Length == 0 || window == leader) continue;
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

        private void GoToLeader()
        {
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length > 0 && window.Key == settings.LeaderKey)
                {
                    GoTo(window);
                    return;
                }
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
                if (window.Name.Length > 0) list.Add(window);
            }
            return list;
        }

        // L'ordre manuel s'applique quand tous les personnages ouverts ont un rang fixe.
        private bool IsManualOrder()
        {
            int named = 0;
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length == 0) continue;
                named++;
                if (window.ManualRank <= 0) return false;
            }
            return named > 0;
        }

        private void ClearManualOrder()
        {
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length > 0 && window.ManualRank != 0)
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
            if (window != null && window.Name.Length > 0 && lastActive != foreground)
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

        // ---------- Barre des tâches ----------

        private void AutoReorderTaskbar()
        {
            if (!settings.AutoTaskbar) return;
            StringBuilder order = new StringBuilder();
            int named = 0;
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length == 0) continue;
                order.Append(window.Handle.ToInt64()).Append(';');
                named++;
            }
            string signature = order.ToString();
            if (signature == lastTaskbarOrder) return;
            lastTaskbarOrder = signature;
            if (named > 1) ReorderTaskbar();
        }

        private void ReorderTaskbar()
        {
            List<GameWindow> ordered = new List<GameWindow>();
            foreach (GameWindow window in windows)
            {
                if (window.Name.Length > 0) ordered.Add(window);
            }
            if (ordered.Count == 0) return;
            try
            {
                ITaskbarList taskbar = (ITaskbarList)new TaskbarListClass();
                taskbar.HrInit();
                foreach (GameWindow window in ordered)
                {
                    taskbar.DeleteTab(window.Handle);
                }
                Thread.Sleep(120);
                foreach (GameWindow window in ordered)
                {
                    taskbar.AddTab(window.Handle);
                    Thread.Sleep(40);
                }
            }
            catch (Exception ex)
            {
                lastMessage = "Rangement de la barre des tâches impossible : " + ex.Message;
            }
        }

        // ---------- Lancement avec Windows ----------

        // Supprime l'entree de demarrage des anciennes versions. Renvoie vrai si elle existait.
        private static bool MigrateOldAutoStart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null || key.GetValue(OldRunName) == null) return false;
                    key.DeleteValue(OldRunName, false);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key != null && key.GetValue(RunName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exe = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunName, "\"" + exe + "\" --reduit");
                        lastMessage = "Dofuxya se lancera avec Windows, réduit dans la zone de notification.";
                    }
                    else
                    {
                        key.DeleteValue(RunName, false);
                        lastMessage = "Dofuxya ne se lancera plus avec Windows.";
                    }
                }
            }
            catch (Exception ex)
            {
                lastMessage = "Réglage du lancement avec Windows impossible : " + ex.Message;
            }
        }

        // ---------- Zone de notification ----------

        private void CreateTray()
        {
            try
            {
                tray = new System.Windows.Forms.NotifyIcon();
                tray.Text = "Dofuxya";
                System.Drawing.Icon icon = null;
                try
                {
                    icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                }
                catch (Exception)
                {
                }
                tray.Icon = icon != null ? icon : System.Drawing.SystemIcons.Application;

                System.Windows.Forms.ContextMenu menu = new System.Windows.Forms.ContextMenu();
                menu.MenuItems.Add("À propos…", delegate { ShowAbout(); });
                menu.MenuItems.Add("Rafraîchir / trier", delegate { RefreshAll(true); });
                menu.MenuItems.Add("Configurer", delegate { ShowMain(); });
                menu.MenuItems.Add("-");
                menu.MenuItems.Add("Quitter", delegate { Quit(); });
                tray.ContextMenu = menu;
                tray.DoubleClick += delegate { ShowMain(); };
                tray.Visible = true;
            }
            catch (Exception)
            {
                tray = null;
            }
        }

        private void ShowMain()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        // A propos. Le diagnostic (utile pour signaler un probleme) y est propose, plutot qu'en bouton.
        private void ShowAbout()
        {
            MessageBoxResult answer = MessageBox.Show(
                "Dofuxya " + Version + "\n\n"
                + "Organise les fenêtres Dofus 3 et Dofus Retro : ordre d'initiative, titres, icônes, raccourcis.\n"
                + "L'outil n'agit que sur les fenêtres : il n'envoie aucune touche ni aucun clic au jeu.\n\n"
                + "Outil de fan, non affilié à Ankama. Dofus est une marque d'Ankama.\n\n"
                + "Un problème ? « Oui » copie un diagnostic à joindre à ton signalement.",
                "À propos de Dofuxya", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) CopyDiagnostic();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (quitting || tray == null)
            {
                Shutdown();
                return;
            }
            // La croix masque la fenetre : l'outil reste actif dans la zone de notification.
            e.Cancel = true;
            CancelCapture();
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                try
                {
                    tray.ShowBalloonTip(4000, "Dofuxya",
                        "Toujours actif ici. Clic droit sur l'icône pour configurer ou quitter.",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                catch (Exception)
                {
                }
            }
        }

        private void Quit()
        {
            quitting = true;
            Close();
        }

        private void Shutdown()
        {
            timer.Stop();
            positionTimer.Stop();
            SaveWindowPosition();
            mouseHook.Uninstall();
            foregroundWatcher.Uninstall();
            turnWatcher.StateChanged = null;
            turnWatcher.Stop();
            scanTimer.Stop();
            if (handle != IntPtr.Zero)
            {
                try
                {
                    Native.DeregisterShellHookWindow(handle);
                }
                catch (Exception)
                {
                }
            }
            if (miniBar != null)
            {
                try
                {
                    miniBar.Close();
                }
                catch (Exception)
                {
                }
                miniBar = null;
            }
            if (handle != IntPtr.Zero)
            {
                foreach (int id in registered.Keys)
                {
                    Native.UnregisterHotKey(handle, id);
                }
            }
            registered.Clear();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            Application.Current.Shutdown();
        }

        // ---------- Diagnostic ----------

        private void CopyDiagnostic()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Dofuxya " + Version + " - diagnostic");
            text.AppendLine("Windows : " + Environment.OSVersion.VersionString);
            text.AppendLine("Mise à jour : " + Updater.State
                + " · fichier : " + Updater.ManifestUrl);
            text.AppendLine("Suivi du tour : " + (settings.AutoFollow ? "activé" : "désactivé") + " · état : "
                + turnWatcher.State + " · lecture des notifications compilée : "
                + (TurnWatcher.Available ? "oui" : "non") + " · clignotements reçus : " + flashCount);
            text.AppendLine("Dernières notifications Dofus :");
            foreach (string line in recentNotifications) text.AppendLine("  " + line);
            text.AppendLine("Portée : " + (settings.OnlyInDofus ? "Dofus seulement" : "partout")
                + " · clavier actif : " + (keyboardActive ? "oui" : "non") + " · " + refusedText);
            foreach (Shortcut shortcut in shortcuts)
            {
                text.AppendLine("  raccourci " + shortcut.Hotkey + " -> " + shortcut.Label);
            }
            text.AppendLine("Icônes disponibles : " + string.Join(", ", icons.Keys.ToArray()));
            text.AppendLine();
            text.AppendLine("Fenêtres dont le processus ou le titre contient « dofus » :");
            try
            {
                foreach (RawWindow raw in Scanner.Enumerate(true))
                {
                    text.AppendLine("- processus=[" + raw.ProcessName + "] classe=[" + raw.ClassName
                        + "] titre=[" + raw.Title + "] retenue=" + (raw.IsDofusProcess ? "oui" : "non"));
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("Erreur : " + ex.Message);
            }
            text.AppendLine();
            text.AppendLine("Interprétation :");
            foreach (GameWindow window in windows)
            {
                text.AppendLine("- nom=[" + window.Name + "] classe=[" + window.ClassName + "] jeu=["
                    + window.Game + "] icône=[" + window.IconKey + "] initiative=" + window.Initiative
                    + " cycle=" + (window.InCycle ? "oui" : "non") + " direct=[" + window.Direct
                    + "] titre d'origine=[" + window.RawTitle + "]");
            }
            try
            {
                Clipboard.SetText(text.ToString());
                lastMessage = "Diagnostic copié : colle-le dans le chat (Ctrl + V).";
            }
            catch (Exception ex)
            {
                lastMessage = "Copie impossible : " + ex.Message;
            }
            UpdateStatus();
        }

        // ---------- Aides ----------

        private static ImageSource LoadLogo()
        {
            try
            {
                Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("DofuxyaManager.logo.png");
                if (stream == null) return null;
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void LoadIcon()
        {
            ImageSource logo = LoadLogo();
            if (logo != null) Icon = logo;
        }
    }
}
