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
    // Fenetre principale. Le code est reparti par theme : MainWindow.cs (etat, detection, icones),
    // MainWindow.Layout.cs (interface), MainWindow.Shortcuts.cs (raccourcis, tour, ordre),
    // MainWindow.System.cs (barre des taches, demarrage, zone de notification, diagnostic),
    // MainWindow.Site.cs (liaison au site, Almanax) et MainWindow.Reading.cs (lecture d'ecran).
    internal partial class MainWindow : Window
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
        private Button wheelKeyButton;
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
        // Gardee tout le temps de l'outil : les fenetres signalees en plein ecran le restent tant qu'elle vit.
        private ITaskbarList2 taskbarList;
        private readonly HashSet<IntPtr> fullscreenMarked = new HashSet<IntPtr>();
        private readonly DispatcherTimer fullscreenTimer = new DispatcherTimer();

        private Button captureButton;
        private Action<Hotkey> captureApply;
        private bool captureHoldKey;

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
            fullscreenTimer.Interval = TimeSpan.FromSeconds(1);
            fullscreenTimer.Tick += delegate { UpdateFullscreenMarks(); };
            fullscreenTimer.Start();
            StartAutoReading();
            StartSite();
            RefreshAll(true);
        }
        // ---------- Détection ----------

        private void RefreshAll(bool force)
        {
            bool changed = false;
            try
            {
                changed = Scan();
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
                    if (settings.AutoMaximizeRetro) MaximizeIfRetro(window);
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

                if (rename && window.IsNamed && raw.Title != window.Name)
                {
                    if (Native.SetWindowText(window.Handle, window.Name))
                    {
                        window.AppliedTitle = window.Name;
                    }
                }
            }

            HashSet<IntPtr> open = new HashSet<IntPtr>();
            foreach (RawWindow raw in found) open.Add(raw.Handle);
            if (windows.RemoveAll(delegate(GameWindow window) { return !open.Contains(window.Handle); }) > 0)
            {
                changed = true;
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
                .OrderBy(delegate(GameWindow w) { return w.IsNamed ? 0 : 1; })
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
                if (!window.IsNamed)
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
