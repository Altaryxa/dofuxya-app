using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace DofuxyaManager
{
    // Un raccourci clavier global : modificateurs + touche.
    internal class Hotkey
    {
        public uint Mods;
        public uint Vk;

        public bool IsEmpty
        {
            get { return Vk == 0; }
        }

        public string Serialize()
        {
            return Mods + ":" + Vk;
        }

        public static Hotkey Parse(string text)
        {
            Hotkey hotkey = new Hotkey();
            if (string.IsNullOrEmpty(text)) return hotkey;
            string[] fields = text.Split(':');
            if (fields.Length != 2) return hotkey;
            uint mods;
            uint vk;
            if (uint.TryParse(fields[0], out mods) && uint.TryParse(fields[1], out vk))
            {
                hotkey.Mods = mods;
                hotkey.Vk = vk;
            }
            return hotkey;
        }

        public bool SameAs(Hotkey other)
        {
            return other != null && other.Mods == Mods && other.Vk == Vk;
        }

        public override string ToString()
        {
            if (IsEmpty) return "Aucun";
            string text = "";
            if ((Mods & Native.MOD_CONTROL) != 0) text += "Ctrl + ";
            if ((Mods & Native.MOD_ALT) != 0) text += "Alt + ";
            if ((Mods & Native.MOD_SHIFT) != 0) text += "Maj + ";
            if ((Mods & Native.MOD_WIN) != 0) text += "Win + ";
            return text + KeyName();
        }

        public bool IsMouse
        {
            get { return Vk == 4 || Vk == 5 || Vk == 6; }
        }

        private string KeyName()
        {
            if (Vk == 4) return "Clic molette";
            if (Vk == 5) return "Souris latéral 1";
            if (Vk == 6) return "Souris latéral 2";
            if (Vk == 0x10) return "Maj";
            if (Vk == 0x11) return "Ctrl";
            if (Vk == 0x12) return "Alt";
            string name;
            try
            {
                name = KeyInterop.KeyFromVirtualKey((int)Vk).ToString();
            }
            catch (Exception)
            {
                name = "Touche " + Vk;
            }
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name.Substring(1);
            if (name.StartsWith("NumPad")) return "Pavé " + name.Substring(6);
            if (name == "Next" || name == "PageDown") return "Page suiv.";
            if (name == "Prior" || name == "PageUp") return "Page préc.";
            if (name == "Space") return "Espace";
            if (name == "Return") return "Entrée";
            if (name == "Oem3") return "²";
            return name;
        }
    }

    // Une fenetre vue par Windows, avant interpretation.
    internal class RawWindow
    {
        public IntPtr Handle;
        public string ProcessName;
        public string Title;
        public string ClassName;
        public bool IsDofusProcess;
    }

    // Une fenetre Dofus suivie par l'outil.
    internal class GameWindow
    {
        public IntPtr Handle;
        public string ProcessName = "";
        public string RawTitle = "";      // dernier titre ecrit par le jeu
        public string AppliedTitle;       // titre pose par l'outil (null si aucun)
        public string Name = "";          // vide = fenetre sans personnage
        public string ClassName = "";     // classe lue dans le titre (Dofus 3)
        public string Game = "";
        public int Initiative;
        public bool InCycle = true;
        public string IconOverride = "";  // icone choisie a la main (vide = automatique)
        public string IconKey = "";       // icone effectivement utilisee
        public string AppliedIconKey;     // icone deja posee sur la fenetre
        public Hotkey Direct = new Hotkey();
        public int ManualRank;            // rang fixe a la main (0 = ordre d'initiative)
        public Reading LastReading;       // dernier releve de l'ecran (prototype), null si aucun
        public readonly List<string> Unlocked = new List<string>(); // succes vus debloques (session)
        public readonly List<string> Quests = new List<string>();   // quetes vues terminees (session)

        public string Key
        {
            get { return Game + "\t" + Name; }
        }

        // Retient un succes debloque (« nom » ou « nom (catégorie) ») s'il n'est pas deja connu, meme
        // vu par un autre moyen (chat, notification, fenetre des recompenses). Renvoie true si nouveau.
        public bool AddUnlocked(string achievement)
        {
            string key = ScreenReader.ChatKey(BaseName(achievement));
            foreach (string known in Unlocked)
            {
                if (ScreenReader.ChatKey(BaseName(known)) != key) continue;
                // On garde la forme la plus complete (avec la catégorie).
                if (achievement.Length > known.Length) Unlocked[Unlocked.IndexOf(known)] = achievement;
                return false;
            }
            Unlocked.Add(achievement);
            return true;
        }

        // Retient une quete terminee si elle n'est pas deja connue. Renvoie true si nouvelle.
        public bool AddQuest(string quest)
        {
            string key = ScreenReader.ChatKey(quest);
            foreach (string known in Quests)
            {
                if (ScreenReader.ChatKey(known) == key) return false;
            }
            Quests.Add(quest);
            return true;
        }

        // Nom d'un succes sans sa categorie (« nom (catégorie) » -> « nom »).
        public static string BaseName(string achievement)
        {
            int paren = achievement.LastIndexOf(" (");
            return paren > 0 ? achievement.Substring(0, paren) : achievement;
        }

        // Faux tant que la fenetre n'affiche pas de personnage (connexion, choix du personnage).
        public bool IsNamed
        {
            get { return Name.Length > 0; }
        }

        public bool IsRetro
        {
            get { return Game == Scanner.Retro; }
        }
    }

    internal static class Scanner
    {
        public const string Retro = "Dofus Retro";
        public const string Dofus3 = "Dofus 3";

        private static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        // Nom de processus par numero, garde d'un passage a l'autre : le relire a chaque fois est couteux.
        private static readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();

        private static string ProcessName(uint pid)
        {
            string name;
            if (processNames.TryGetValue(pid, out name)) return name;
            name = "";
            try
            {
                name = Process.GetProcessById((int)pid).ProcessName;
            }
            catch (Exception)
            {
            }
            processNames[pid] = name;
            return name;
        }

        private static bool ContainsDofus(string text)
        {
            return text.IndexOf("dofus", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Processus du jeu : « Dofus » (Dofus.exe) ou « Dofus Retro » (Dofus Retro.exe), nom exact.
        // Les autres programmes dont le nom contient « dofus » (Dofus Guide…) sont ignores.
        private static bool IsGameProcess(string processName)
        {
            return string.Equals(processName, "Dofus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "Dofus Retro", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsRetro(string text)
        {
            return text.IndexOf("retro", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Liste les fenetres principales visibles. Sans "broad", seules celles dont le
        // processus est celui du jeu sont gardees ; avec, celles dont le titre le contient aussi.
        public static List<RawWindow> Enumerate(bool broad)
        {
            List<RawWindow> result = new List<RawWindow>();
            HashSet<uint> seen = new HashSet<uint>();

            Native.EnumWindowsProc callback = delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!Native.IsWindowVisible(hWnd)) return true;
                if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                int length = Native.GetWindowTextLength(hWnd);
                if (length <= 0) return true;

                uint pid;
                Native.GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0 || pid == ownPid) return true;

                seen.Add(pid);
                string processName = ProcessName(pid);

                StringBuilder title = new StringBuilder(length + 2);
                Native.GetWindowText(hWnd, title, title.Capacity);
                string titleText = title.ToString();

                bool isDofusProcess = IsGameProcess(processName);
                bool titleMatches = ContainsDofus(titleText);
                if (!isDofusProcess && !(broad && titleMatches)) return true;

                StringBuilder className = new StringBuilder(256);
                Native.GetClassName(hWnd, className, className.Capacity);

                RawWindow raw = new RawWindow();
                raw.Handle = hWnd;
                raw.ProcessName = processName;
                raw.Title = titleText;
                raw.ClassName = className.ToString();
                raw.IsDofusProcess = isDofusProcess;
                result.Add(raw);
                return true;
            };

            Native.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            // On oublie les processus disparus, pour ne pas confondre avec un nouveau de meme numero.
            List<uint> gone = new List<uint>();
            foreach (uint known in processNames.Keys)
            {
                if (!seen.Contains(known)) gone.Add(known);
            }
            foreach (uint oldPid in gone) processNames.Remove(oldPid);
            return result;
        }

        // Lit nom, classe et jeu dans un titre de fenetre.
        // Formats attendus : "Nom - Classe - version - Release" (Dofus 3),
        // "Nom - Dofus Retro vX.Y.Z" (Retro), ou "Nom" seul si la fenetre a deja ete renommee.
        public static void Parse(GameWindow window, string title, string processName)
        {
            string game = ContainsRetro(processName) ? Retro : Dofus3;
            string name = "";
            string className = "";

            string[] parts = title.Split(new string[] { " - " }, StringSplitOptions.None);
            if (parts.Length == 1)
            {
                string single = title.Trim();
                if (single.Length > 0 && !StartsWithDofus(single))
                {
                    name = single;
                }
            }
            else
            {
                name = parts[0].Trim();
                string second = parts[1].Trim();
                if (StartsWithDofus(second))
                {
                    if (ContainsRetro(second)) game = Retro;
                }
                else if (parts.Length >= 3)
                {
                    className = second;
                }
                if (StartsWithDofus(name)) name = "";
            }

            window.Name = name;
            window.ClassName = className;
            window.Game = game;
        }

        // Vrai si la fenetre appartient au processus du jeu (meme pas encore listee).
        public static bool IsDofusProcessWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hWnd, out pid);
            return pid != 0 && pid != ownPid && IsGameProcess(ProcessName(pid));
        }

        private static bool StartsWithDofus(string text)
        {
            return text.StartsWith("Dofus", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class AppPaths
    {
        private static string dataFolder;

        // Dossier des reglages et journaux, prepare au premier appel seulement.
        public static string DataFolder
        {
            get
            {
                if (dataFolder != null) return dataFolder;
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(root, "Dofuxya");
                string old = Path.Combine(root, "DofuxyaManager");
                try
                {
                    // Reprise des reglages des versions nommees Dofuxya Manager.
                    if (!Directory.Exists(folder) && Directory.Exists(old)) Directory.Move(old, folder);
                    Directory.CreateDirectory(folder);
                }
                catch (Exception)
                {
                }
                dataFolder = folder;
                return folder;
            }
        }
    }

    // Journaux texte de %AppData%\Dofuxya (aucune image) : une ecriture qui echoue ne doit jamais
    // gener l'outil.
    internal static class DataLog
    {
        public static void Append(string file, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.DataFolder, file), text, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        public static void Write(string file, string text)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.DataFolder, file), text, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        // « 2026-11-23 18:04:12 [Keax] [quête] texte »
        public static string Line(string name, string kind, string text)
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + name + "] [" + kind + "] " + text + Environment.NewLine;
        }
    }

    // Memoire par personnage : initiative, cycle, icone choisie, raccourci direct.
    internal class Store
    {
        private class Entry
        {
            public int Initiative;
            public bool InCycle = true;
            public string Icon = "";
            public string Direct = "";
            public string Class = "";
            public int Rank;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly string path;

        public Store()
        {
            path = Path.Combine(AppPaths.DataFolder, "personnages.txt");
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string[] fields = line.Split('\t');
                        if (fields.Length < 4) continue;
                        Entry entry = new Entry();
                        int.TryParse(fields[2], out entry.Initiative);
                        entry.InCycle = fields[3] != "0";
                        if (fields.Length > 4) entry.Icon = fields[4];
                        if (fields.Length > 5) entry.Direct = fields[5];
                        if (fields.Length > 6) entry.Class = fields[6];
                        if (fields.Length > 7) int.TryParse(fields[7], out entry.Rank);
                        entries[fields[0] + "\t" + fields[1]] = entry;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public void Load(GameWindow window)
        {
            Entry entry;
            if (window.IsNamed && entries.TryGetValue(window.Key, out entry))
            {
                window.Initiative = entry.Initiative;
                window.InCycle = entry.InCycle;
                window.IconOverride = entry.Icon;
                window.Direct = Hotkey.Parse(entry.Direct);
                window.ManualRank = entry.Rank;
            }
            else
            {
                window.ManualRank = 0;
                window.Initiative = 0;
                window.InCycle = true;
                window.IconOverride = "";
                window.Direct = new Hotkey();
            }
        }

        // La classe n'est lisible dans le titre que tant que la fenetre n'a pas ete renommee.
        // On la retient donc par personnage, et on la relit quand le titre ne la donne plus.
        public void SyncClass(GameWindow window)
        {
            if (!window.IsNamed) return;
            Entry entry;
            bool known = entries.TryGetValue(window.Key, out entry);
            if (window.ClassName.Length == 0)
            {
                if (known && entry.Class.Length > 0) window.ClassName = entry.Class;
            }
            else if (!known || entry.Class != window.ClassName)
            {
                Save(window);
            }
        }

        public void Save(GameWindow window)
        {
            if (!window.IsNamed) return;
            Entry entry = new Entry();
            entry.Initiative = window.Initiative;
            entry.InCycle = window.InCycle;
            entry.Icon = window.IconOverride;
            entry.Direct = window.Direct.IsEmpty ? "" : window.Direct.Serialize();
            entry.Class = window.ClassName;
            entry.Rank = window.ManualRank;
            Entry previous;
            if (entry.Class.Length == 0 && entries.TryGetValue(window.Key, out previous))
            {
                entry.Class = previous.Class;
            }
            entries[window.Key] = entry;
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, Entry> pair in entries)
                {
                    Entry value = pair.Value;
                    lines.Add(pair.Key + "\t" + value.Initiative + "\t" + (value.InCycle ? "1" : "0")
                        + "\t" + value.Icon + "\t" + value.Direct + "\t" + value.Class + "\t" + value.Rank);
                }
                File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }

    // Reglages generaux.
    internal class Settings
    {
        public Hotkey Next = new Hotkey();
        public Hotkey Previous = new Hotkey();
        public bool Rename = true;
        public bool AutoTaskbar = true;
        public bool OnlyInDofus = true;
        public bool MiniBar;
        public bool MiniVertical;
        public bool AutoFollow;
        public bool AutoMaximizeRetro;
        // Lecture d'ecran (reservee a l'auteur pour l'instant) : activee seulement par « lecture_ecran=1 »
        // dans reglages.txt.
        public bool ScreenReading;
        public bool AutoReading;
        // Adresse du site (« site=http://localhost:3000 » pour les essais).
        public string SiteUrl = "https://dofuxya.com";
        // Rappel de l'Almanax du jour (outil lie au site) et dernier jour rappele (aaaa-mm-jj).
        public bool AlmanaxReminder;
        public string AlmanaxSeen = "";
        public Hotkey Leader = new Hotkey();
        // Touche a maintenir pendant le clic qui ouvre la roue des persos (aucune = roue desactivee).
        public Hotkey WheelKey = new Hotkey();
        public string LeaderKey = "";
        public double MiniX = double.NaN;
        public double MiniY = double.NaN;
        public double WindowX = double.NaN;
        public double WindowY = double.NaN;

        private readonly string path;

        public Settings()
        {
            path = Path.Combine(AppPaths.DataFolder, "reglages.txt");
            Next.Vk = Native.VK_F3;
            Previous.Vk = Native.VK_F3;
            Previous.Mods = Native.MOD_SHIFT;
            WheelKey.Vk = MouseHook.VK_SHIFT;
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0) continue;
                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        if (key == "suivante") Next = Hotkey.Parse(value);
                        else if (key == "precedente") Previous = Hotkey.Parse(value);
                        else if (key == "renommer") Rename = value != "0";
                        else if (key == "barre_auto") AutoTaskbar = value != "0";
                        else if (key == "seulement_dofus") OnlyInDofus = value != "0";
                        else if (key == "mini_barre") MiniBar = value == "1";
                        else if (key == "mini_verticale") MiniVertical = value == "1";
                        else if (key == "suivi_tour") AutoFollow = value == "1";
                        else if (key == "agrandir_retro") AutoMaximizeRetro = value == "1";
                        else if (key == "lecture_ecran") ScreenReading = value == "1";
                        else if (key == "releve_auto") AutoReading = value == "1";
                        else if (key == "site" && value.StartsWith("http")) SiteUrl = value.TrimEnd('/');
                        else if (key == "rappel_almanax") AlmanaxReminder = value == "1";
                        else if (key == "almanax_vu") AlmanaxSeen = value;
                        else if (key == "principal_touche") Leader = Hotkey.Parse(value);
                        else if (key == "roue_touche") WheelKey = Hotkey.Parse(value);
                        else if (key == "principal") LeaderKey = line.Substring(separator + 1);
                        else if (key == "mini_x") MiniX = ParseDouble(value);
                        else if (key == "mini_y") MiniY = ParseDouble(value);
                        else if (key == "fenetre_x") WindowX = ParseDouble(value);
                        else if (key == "fenetre_y") WindowY = ParseDouble(value);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static double ParseDouble(string text)
        {
            double value;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return double.NaN;
        }

        public void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("suivante=" + Next.Serialize());
                lines.Add("precedente=" + Previous.Serialize());
                lines.Add("renommer=" + (Rename ? "1" : "0"));
                lines.Add("barre_auto=" + (AutoTaskbar ? "1" : "0"));
                lines.Add("seulement_dofus=" + (OnlyInDofus ? "1" : "0"));
                lines.Add("mini_barre=" + (MiniBar ? "1" : "0"));
                lines.Add("mini_verticale=" + (MiniVertical ? "1" : "0"));
                if (!double.IsNaN(WindowX) && !double.IsNaN(WindowY))
                {
                    lines.Add("fenetre_x=" + WindowX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    lines.Add("fenetre_y=" + WindowY.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                lines.Add("suivi_tour=" + (AutoFollow ? "1" : "0"));
                lines.Add("agrandir_retro=" + (AutoMaximizeRetro ? "1" : "0"));
                if (ScreenReading) lines.Add("lecture_ecran=1");
                if (AutoReading) lines.Add("releve_auto=1");
                if (SiteUrl != "https://dofuxya.com") lines.Add("site=" + SiteUrl);
                lines.Add("rappel_almanax=" + (AlmanaxReminder ? "1" : "0"));
                if (AlmanaxSeen.Length > 0) lines.Add("almanax_vu=" + AlmanaxSeen);
                lines.Add("principal_touche=" + Leader.Serialize());
                lines.Add("roue_touche=" + WheelKey.Serialize());
                lines.Add("principal=" + LeaderKey);
                if (!double.IsNaN(MiniX) && !double.IsNaN(MiniY))
                {
                    lines.Add("mini_x=" + MiniX.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    lines.Add("mini_y=" + MiniY.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }
}
