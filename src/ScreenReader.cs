using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DofuxyaManager
{
    // Ce que l'outil a lu a l'ecran d'une fenetre Dofus (0 = non lu).
    internal class Reading
    {
        public int Level;
        public int Omega;
        public DateTime At;
        public string Note = "";
        public string Diagnostic = "";   // details de lecture, pour le journal seulement
        public readonly List<string> Events = new List<string>();
        // Lignes du chat visibles (texte apres l'heure), pour reperer les nouvelles d'une lecture a l'autre.
        public readonly List<string> ChatLines = new List<string>();
        // Succes debloques lus a l'ecran (« nom (catégorie) ») : notification, fenetre des recompenses.
        public readonly List<string> Unlocked = new List<string>();

        // Garde les valeurs d'un releve precedent que celui-ci n'a pas lues.
        public void KeepFrom(Reading previous)
        {
            if (previous == null) return;
            if (Level == 0 && Omega == 0)
            {
                Level = previous.Level;
                Omega = previous.Omega;
            }
            // Les evenements du chat deja vus restent dans l'infobulle (les 10 derniers).
            List<string> events = new List<string>(previous.Events);
            events.AddRange(Events);
            Events.Clear();
            Events.AddRange(events.GetRange(Math.Max(0, events.Count - 10), Math.Min(10, events.Count)));
        }

        public string Values()
        {
            return Level + "/" + Omega;
        }

        public string Summary()
        {
            string value = Omega > 0 ? "Niv. 200 · Ω " + Omega : Level > 0 ? "Niv. " + Level : "rien de lu";
            return value + " · relevé à " + At.ToString("HH:mm");
        }
    }

    // Lecture d'ecran (reservee a l'auteur) : capture la fenetre du jeu, la passe a la reconnaissance de
    // texte de Windows, et y cherche niveau et omega, succes debloques et quetes terminees, par leur
    // texte, ou qu'ils soient (les interfaces du jeu se deplacent). N'agit jamais sur le jeu ; l'image
    // n'est pas gardee.
    internal static class ScreenReader
    {
        private static readonly string[] Classes =
        {
            "feca", "osamodas", "enutrof", "sram", "xelor", "ecaflip", "eniripsa", "iop", "cra", "sadida",
            "sacrieur", "pandawa", "roublard", "zobal", "steamer", "eliotrope", "huppermage", "ouginak",
            "forgelance",
        };

        // Categories du panneau des succes (fenetre des recompenses : « Temple Céleste … Exploration »).
        private static readonly string[] Categories =
        {
            "general", "exploration", "donjons", "monstres", "quetes", "metiers", "elevage", "evenements",
            "kolizeum", "guilde", "compagnons",
        };

        private static OcrEngine engine;

        private class Word
        {
            public string Text;
            public int Line;
            public double X, Y, W, H;

            public double CenterY { get { return Y + H / 2; } }
            public double Right { get { return X + W; } }
        }

        // A appeler hors du fil de l'interface : capture et lecture prennent quelques centaines de ms.
        // "log" recoit le texte lu (positions comprises) pour regler la lecture ; aucune image.
        public static Reading Read(IntPtr hWnd, string name, StringBuilder log)
        {
            Reading reading = new Reading();
            reading.At = DateTime.Now;
            if (Native.IsIconic(hWnd))
            {
                reading.Note = "fenêtre réduite";
            }
            else
            {
                using (Bitmap image = Capture(hWnd))
                {
                    if (image == null) reading.Note = "capture impossible (image noire)";
                    else ReadImage(reading, image, name, log);
                }
            }
            if (log != null)
            {
                if (reading.Note.Length > 0 && reading.Level == 0) log.AppendLine("=== " + name);
                log.AppendLine("--> " + reading.Summary() + (reading.Note.Length > 0 ? " [" + reading.Note + "]" : ""));
                if (reading.Diagnostic.Length > 0) log.AppendLine("    " + reading.Diagnostic);
                log.AppendLine();
            }
            return reading;
        }

        // Lecture d'une image deja capturee (sert aussi aux essais sur des captures d'ecran).
        public static void ReadImage(Reading reading, Bitmap image, string name, StringBuilder log)
        {
            List<Word> words;
            List<string> lines;
            Recognize(image, out words, out lines);
            if (log != null)
            {
                log.AppendLine("=== " + name + " (" + image.Width + "x" + image.Height + ")");
                foreach (string line in lines) log.AppendLine(line);
            }
            Parse(reading, words, lines, image, name);
        }

        // ---------- Lecture continue (voir ScreenWatcher) ----------

        // Zone utile de la fenetre : chat, barre d'XP, fiche Caracteristiques, notification de succes.
        public class Zone
        {
            public string Kind;
            public Rectangle Rect;
        }

        // Lecture complete d'une image, avec les zones utiles reperees (coordonnees de l'image).
        public static void ReadFull(Reading reading, Bitmap image, string name, List<Zone> zones)
        {
            List<Word> words;
            List<string> lines;
            Recognize(image, out words, out lines);
            Parse(reading, words, lines, image, name);
            FindZones(words, lines, new Rectangle(0, 0, image.Width, image.Height), zones);
        }

        // Lecture d'une zone deja decoupee (image de la zone seule).
        public static void ReadZone(Reading reading, Bitmap zoneImage, string name)
        {
            List<Word> words;
            List<string> lines;
            Recognize(zoneImage, out words, out lines);
            Parse(reading, words, lines, zoneImage, name);
        }

        private static void FindZones(List<Word> words, List<string> lines, Rectangle bounds, List<Zone> zones)
        {
            // Chat : l'ensemble des lignes qui commencent par une heure.
            Rectangle chat = Rectangle.Empty;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!ChatLine.IsMatch(lines[i].Substring(12))) continue;
                foreach (Word word in words)
                {
                    if (word.Line != i) continue;
                    Rectangle r = new Rectangle((int)word.X, (int)word.Y, (int)word.W + 1, (int)word.H + 1);
                    chat = chat.IsEmpty ? r : Rectangle.Union(chat, r);
                }
            }
            if (!chat.IsEmpty)
            {
                chat = new Rectangle(chat.X - 40, chat.Y - 24, chat.Width + 400, chat.Height + 48);
                Add(zones, "chat", chat, bounds);
            }

            // Notification de succes : elle ne reste que quelques secondes, sa place est retenue (ScreenWatcher).
            foreach (Word word in words)
            {
                if (IsToastAnchor(words, word))
                {
                    // Au plus juste dans la notification (opaque) : le decor anime autour ne doit pas compter.
                    Add(zones, "notification", new Rectangle((int)word.X - 60, (int)word.Y - 10, 300, (int)word.H * 3 + 14), bounds);
                }
            }

            // Barre d'XP (« NIV. 152 ») et fiche Caracteristiques (« OMÉGA 152 | ENUTROF »).
            foreach (Word word in words)
            {
                if (word.Text == "NIV" || word.Text == "NIV.")
                {
                    Add(zones, "xp", new Rectangle((int)word.X - 20, (int)word.Y - 14, 200, (int)word.H + 28), bounds);
                }
                if ((word.Text == "OMEGA" || word.Text == "OMÉGA" || word.Text == "NIVEAU") && ClassOnRow(words, word))
                {
                    Add(zones, "fiche", new Rectangle((int)word.X - 20, (int)word.Y - 20, 420, (int)word.H + 40), bounds);
                }
            }
        }

        private static void Add(List<Zone> zones, string kind, Rectangle rect, Rectangle bounds)
        {
            rect.Intersect(bounds);
            if (rect.Width < 20 || rect.Height < 10) return;
            zones.Add(new Zone { Kind = kind, Rect = rect });
        }

        private static readonly Regex ChatLine =
            new Regex(@"^\S{0,2}\s*\[?\s*[CI]?\d{1,2}\s*[:.]?\s*\d{2}\s*[\]1l)]\s*(.+)$");

        // Essai hors jeu : « Dofuxya.exe --essai-lecture sortie.txt image1.png image2.png… ».
        public static void TestFiles(string[] args)
        {
            StringBuilder report = new StringBuilder();
            for (int i = 2; i < args.Length; i++)
            {
                Reading reading = new Reading();
                reading.At = DateTime.Now;
                using (Bitmap source = new Bitmap(args[i]))
                using (Bitmap image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(image)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    ReadImage(reading, image, System.IO.Path.GetFileNameWithoutExtension(args[i]), null);
                }
                report.AppendLine(System.IO.Path.GetFileName(args[i]) + " : " + reading.Summary()
                    + (reading.Note.Length > 0 ? " [" + reading.Note + "]" : ""));
                foreach (string unlocked in reading.Unlocked) report.AppendLine("  récompense de succès : " + unlocked);
                foreach (string line in reading.ChatLines)
                {
                    string achievement = UnlockedAchievement(line);
                    int value;
                    bool omega;
                    string quest = CompletedQuest(line);
                    if (achievement != null) report.AppendLine("  succès débloqué : " + achievement);
                    else if (quest != null) report.AppendLine("  quête terminée : " + quest);
                    else if (LevelUpOf(line, "Osmalie", out value, out omega))
                        report.AppendLine("  " + (omega ? "oméga " : "niveau ") + value + " (Osmalie)");
                }
            }
            System.IO.File.WriteAllText(args[1], report.ToString(), Encoding.UTF8);
        }

        // ---------- Capture ----------

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out Native.RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        private const uint PW_CLIENTONLY = 1;
        private const uint PW_RENDERFULLCONTENT = 2;

        // Zone client de la fenetre, meme cachee derriere une autre ; sinon, l'ecran a son
        // emplacement si elle est au premier plan. null si l'image reste noire.
        private static Bitmap Capture(IntPtr hWnd)
        {
            Native.RECT client;
            if (!GetClientRect(hWnd, out client)) return null;
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width < 100 || height < 100) return null;

            Bitmap image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    PrintWindow(hWnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
            if (!IsBlack(image)) return image;

            if (Native.GetForegroundWindow() == hWnd)
            {
                POINT origin = new POINT();
                ClientToScreen(hWnd, ref origin);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
                }
                if (!IsBlack(image)) return image;
            }
            image.Dispose();
            return null;
        }

        // Zone client entiere (area vide) ou partie de la fenetre au premier plan, copiee depuis
        // l'ecran (rapide) ; PrintWindow en secours pour la fenetre entiere.
        public static Bitmap CaptureArea(IntPtr hWnd, Rectangle area)
        {
            Native.RECT client;
            if (!GetClientRect(hWnd, out client)) return null;
            if (area.IsEmpty) area = new Rectangle(0, 0, client.Right - client.Left, client.Bottom - client.Top);
            if (area.Width < 20 || area.Height < 10) return null;
            if (Native.GetForegroundWindow() != hWnd) return area.X == 0 && area.Y == 0 ? Capture(hWnd) : null;
            POINT origin = new POINT();
            ClientToScreen(hWnd, ref origin);
            Bitmap image = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.CopyFromScreen(origin.X + area.X, origin.Y + area.Y, 0, 0, area.Size);
            }
            return image;
        }

        // Fenetre entiere, meme cachee derriere une autre (PrintWindow) ; null si l'image reste noire.
        public static Bitmap CaptureWindow(IntPtr hWnd)
        {
            return Native.IsIconic(hWnd) ? null : Capture(hWnd);
        }

        // Les quatre coins de la zone client (taille proportionnelle a la fenetre, 460 x 220 a 2560 px de large).
        public static Rectangle[] Corners(IntPtr hWnd)
        {
            Native.RECT client;
            if (!GetClientRect(hWnd, out client)) return new Rectangle[0];
            int width = client.Right - client.Left, height = client.Bottom - client.Top;
            double scale = Math.Max(0.5, width / 2560.0);
            int w = Math.Min(width / 2, (int)(460 * scale)), h = Math.Min(height / 2, (int)(220 * scale));
            return new[]
            {
                new Rectangle(0, 0, w, h), new Rectangle(width - w, 0, w, h),
                new Rectangle(0, height - h, w, h), new Rectangle(width - w, height - h, w, h),
            };
        }

        // Part de pixels jaune vif (fond de la notification de succes), un point sur trois.
        public static double YellowShare(Bitmap image)
        {
            Rectangle area = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] row = new byte[data.Stride];
            int yellow = 0, total = 0;
            for (int y = 0; y < image.Height; y += 3)
            {
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < image.Width; x += 3)
                {
                    int i = x * 4;
                    int b = row[i], g = row[i + 1], r = row[i + 2];
                    total++;
                    if (r > 215 && g > 150 && b < 110 && r - b > 130) yellow++;
                }
            }
            image.UnlockBits(data);
            return total == 0 ? 0 : (double)yellow / total;
        }

        // Empreinte grossiere d'une image (une luminosite sur 16 tous les 4 pixels) pour voir si
        // elle a change, sans reconnaissance de texte.
        public static byte[] Signature(Bitmap image)
        {
            Rectangle area = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] row = new byte[data.Stride];
            byte[] samples = new byte[((image.Height + 3) / 4) * ((image.Width + 3) / 4)];
            int n = 0;
            for (int y = 0; y < image.Height; y += 4)
            {
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < image.Width; x += 4)
                {
                    int i = x * 4;
                    samples[n++] = (byte)((row[i] + row[i + 1] * 2 + row[i + 2]) / 64);
                }
            }
            image.UnlockBits(data);
            return samples;
        }

        // Vrai si deux empreintes different sur plus de 1 % des points.
        public static bool Differs(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return true;
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) changed++;
            }
            return changed * 100 > a.Length;
        }

        private static bool IsBlack(Bitmap image)
        {
            for (int i = 1; i < 12; i++)
            {
                for (int j = 1; j < 12; j++)
                {
                    Color c = image.GetPixel(image.Width * i / 12, image.Height * j / 12);
                    if (c.R + c.G + c.B > 30) return false;
                }
            }
            return true;
        }

        // ---------- Reconnaissance de texte ----------

        private static T Wait<T>(Windows.Foundation.IAsyncOperation<T> operation)
        {
            while (operation.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(3);
            return operation.GetResults();
        }

        private static void Recognize(Bitmap image, out List<Word> words, out List<string> lines)
        {
            if (engine == null)
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language("fr-FR"))
                    ?? OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null) throw new InvalidOperationException(
                    "reconnaissance de texte indisponible (langue française absente de Windows)");
            }
            Rectangle area = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[data.Stride * image.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            image.UnlockBits(data);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            Windows.Storage.Streams.IBuffer buffer =
                Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
            SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
                image.Width, image.Height, BitmapAlphaMode.Premultiplied);
            OcrResult result = Wait(engine.RecognizeAsync(bitmap));

            words = new List<Word>();
            lines = new List<string>();
            int index = 0;
            foreach (OcrLine line in result.Lines)
            {
                Windows.Foundation.Rect first = line.Words[0].BoundingRect;
                lines.Add(string.Format("[{0,4},{1,4}] {2}", (int)first.X, (int)first.Y, line.Text));
                foreach (OcrWord word in line.Words)
                {
                    Windows.Foundation.Rect r = word.BoundingRect;
                    words.Add(new Word { Text = word.Text, Line = index, X = r.X, Y = r.Y, W = r.Width, H = r.Height });
                }
                index++;
            }
        }

        // ---------- Interpretation ----------

        private static void Parse(Reading reading, List<Word> words, List<string> lines, Bitmap image, string name)
        {
            // Fiche Caracteristiques : « OMÉGA 152 | ENUTROF » ou « NIVEAU 152 | SRAM » (en capitales,
            // ce qui l'ecarte du « Niveau 160 » de la zone).
            foreach (Word word in words)
            {
                bool omega = word.Text == "OMEGA" || word.Text == "OMÉGA";
                bool level = word.Text == "NIVEAU";
                if ((!omega && !level) || !ClassOnRow(words, word)) continue;
                int value = NumberRightOf(words, word, 160);
                if (value <= 0) continue;
                if (omega) reading.Omega = value;
                else if (value <= 200) reading.Level = value;
            }

            // Fenetre de passage de niveau : bandeau « NIVEAU 2 » au-dessus du bouton « CARACTÉRISTIQUES ».
            foreach (Word word in words)
            {
                bool omega = word.Text == "OMEGA" || word.Text == "OMÉGA";
                if (word.Text != "NIVEAU" && !omega) continue;
                bool popup = false;
                foreach (Word other in words)
                {
                    if ((other.Text == "CARACTÉRISTIQUES" || other.Text == "CARACTERISTIQUES" || other.Text == "AUGMENTATIONS")
                        && other.Y > word.Y && other.Y - word.Y < 320 && Math.Abs(other.X - word.X) < 300) popup = true;
                }
                if (!popup) continue;
                int value = NumberRightOf(words, word, 120);
                if (omega && value > 0 && value > reading.Omega) reading.Omega = value;
                else if (!omega && value > 0 && value <= 200 && value > reading.Level) reading.Level = value;
            }

            // Notification « SUCCÈS DÉVERROUILLÉ ! » avec le nom du succes juste dessous.
            foreach (Word anchor in words)
            {
                if (!IsToastAnchor(words, anchor)) continue;
                List<Word> below = new List<Word>();
                Word first = null;
                foreach (Word other in words)
                {
                    if (other.Y <= anchor.Y + anchor.H * 0.6 || other.Y > anchor.Y + anchor.H * 3) continue;
                    if (other.X < anchor.X - 30 || other.X > anchor.X + 420) continue;
                    if (first == null || other.Y < first.Y) first = other;
                    below.Add(other);
                }
                if (first == null) continue;
                List<Word> row = below.FindAll(delegate(Word w) { return SameRow(w, first); });
                row.Sort(delegate(Word a, Word b) { return a.X.CompareTo(b.X); });
                StringBuilder label = new StringBuilder();
                foreach (Word part in row) label.Append(label.Length > 0 ? " " : "").Append(part.Text);
                if (label.Length > 1) reading.Unlocked.Add(label.ToString());
            }

            // Fenetre des recompenses de succes : « Temple Céleste … Exploration », puis « RÉCUPÉRER » dessous.
            foreach (Word category in words)
            {
                string normalized = Normalize(category.Text);
                if (Array.IndexOf(Categories, normalized) < 0) continue;
                bool reward = false;
                foreach (Word other in words)
                {
                    string text = Normalize(other.Text);
                    if ((text == "recuperer" || text == "xp") && other.Y > category.Y && other.Y - category.Y < 90
                        && Math.Abs(other.X - category.X) < 200) reward = true;
                }
                if (!reward) continue;
                List<Word> title = new List<Word>();
                foreach (Word other in words)
                {
                    if (other != category && SameRow(other, category) && other.Right < category.X - 30
                        && category.X - other.X < 700) title.Add(other);
                }
                if (title.Count == 0) continue;
                title.Sort(delegate(Word a, Word b) { return a.X.CompareTo(b.X); });
                StringBuilder label = new StringBuilder();
                foreach (Word part in title) label.Append(label.Length > 0 ? " " : "").Append(part.Text);
                reading.Unlocked.Add(label + " (" + category.Text + ")");
            }

            // Barre d'XP : « NIV. 152 » (sans omega ; l'omega y est une icone, pas lue).
            if (reading.Level == 0 && reading.Omega == 0)
            {
                foreach (Word word in words)
                {
                    if (word.Text != "NIV" && word.Text != "NIV.") continue;
                    int value = NumberRightOf(words, word, 120);
                    if (value > 0 && value <= 200) reading.Level = value;
                }
            }

            // L'omega n'est pris que la ou il est ecrit en toutes lettres (fiche, fenetre de passage,
            // chat) : un nombre sur fond dore ou une icone mal lue donnaient des omegas inventes.
            if (reading.Omega > 0) reading.Level = 200;

            // Chat : lignes commencant par une heure (« [17:28] … », souvent mal lue : « [17:281 »).
            foreach (string line in lines)
            {
                Match chat = ChatLine.Match(line.Substring(12));
                if (chat.Success) reading.ChatLines.Add(chat.Groups[1].Value.Trim());
            }
        }

        // Messages du chat sur la progression du perso de la fenetre :
        // « Succès déverrouillé : [Bord de la forêt maléfique] » (ceux des autres joueurs s'ecrivent
        // « X a déverrouillé le succès [Y] » et ne correspondent pas) ;
        // « Osmalie vient de passer au niveau 92. » ou « Osmalie vient de passer Oméga 45. ».
        private static readonly Regex OwnAchievement = new Regex(
            @"succ\S{0,2}s\s+d\S{0,2}verrouill\S{0,2}\s*:?\s*\[([^\]]+)", RegexOptions.IgnoreCase);
        private static readonly Regex LevelUp = new Regex(
            @"(\S+)\s+vient\s+de\s+passer\s+(?:au\s+niveau\s+)?(om\S{0,2}ga\s+)?(\d{1,4})", RegexOptions.IgnoreCase);

        // Quete terminee (format pas encore vu en jeu, a confirmer dans evenements.txt) : une ligne qui
        // parle d'une quete terminee, validee, accomplie ou achevee. Le nom est pris entre crochets,
        // entre guillemets, ou apres les deux-points.
        private static readonly Regex QuestDone = new Regex(
            @"qu\S{0,2}te\b.*\b(termin|valid|accompli|achev)|\b(termin|valid|accompli|achev)\S*\s+(la\s+)?qu\S{0,2}te\b",
            RegexOptions.IgnoreCase);
        private static readonly Regex QuestName = new Regex(@"\[([^\]]{2,120})\]|«\s*([^»]{2,120}?)\s*»|:\s*(.{2,120}?)\s*\.?$");

        // Nom de la quete terminee par le perso de la fenetre, ou null.
        public static string CompletedQuest(string line)
        {
            if (!QuestDone.IsMatch(line)) return null;
            Match name = QuestName.Match(line);
            if (!name.Success) return null;
            for (int i = 1; i <= 3; i++)
            {
                if (name.Groups[i].Success) return name.Groups[i].Value.Trim();
            }
            return null;
        }

        // Nom du succes debloque par le perso de la fenetre, ou null.
        public static string UnlockedAchievement(string line)
        {
            Match match = OwnAchievement.Match(line);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        // Passage de niveau du perso nomme : renvoie true et la valeur (omega ou niveau).
        public static bool LevelUpOf(string line, string name, out int value, out bool omega)
        {
            value = 0;
            omega = false;
            Match match = LevelUp.Match(line);
            if (!match.Success || ChatKey(match.Groups[1].Value) != ChatKey(name)) return false;
            omega = match.Groups[2].Success;
            return int.TryParse(match.Groups[3].Value, out value) && value > 0;
        }

        // Ligne du chat qui parle d'un succes, d'une quete, d'un niveau ou de l'omega.
        public static bool IsProgressLine(string line)
        {
            string lower = line.ToLowerInvariant();
            return lower.Contains("succès") || lower.Contains("succes") || lower.Contains("niveau")
                || lower.Contains("oméga") || lower.Contains("omega") || lower.Contains("quête")
                || lower.Contains("quete");
        }

        // Forme comparable d'une ligne du chat (la lecture varie un peu d'une capture a l'autre).
        public static string ChatKey(string line)
        {
            StringBuilder key = new StringBuilder();
            foreach (char c in line.ToLowerInvariant())
            {
                if (char.IsLetter(c)) key.Append(c);
            }
            return key.ToString();
        }

        // « SUCCÈS » en capitales suivi de « DÉVERROUILLÉ » sur la meme ligne (notification du jeu).
        private static bool IsToastAnchor(List<Word> words, Word anchor)
        {
            if (anchor.Text != "SUCCÈS" && anchor.Text != "SUCCES") return false;
            foreach (Word other in words)
            {
                if (SameRow(other, anchor) && other.X > anchor.X && other.X - anchor.Right < 40
                    && Normalize(other.Text) == "deverrouille") return true;
            }
            return false;
        }

        private static bool ClassOnRow(List<Word> words, Word anchor)
        {
            foreach (Word word in words)
            {
                if (!SameRow(word, anchor) || word.X < anchor.X || word.X - anchor.Right > 400) continue;
                string normalized = Normalize(word.Text);
                foreach (string cls in Classes)
                {
                    // Le nom peut etre coupe par le panneau (« FORGELAN »).
                    if (normalized == cls || (normalized.Length >= 4 && cls.StartsWith(normalized))) return true;
                }
            }
            return false;
        }

        private static int NumberRightOf(List<Word> words, Word anchor, double maxGap)
        {
            Word best = null;
            foreach (Word word in words)
            {
                if (word == anchor || !SameRow(word, anchor) || word.X < anchor.Right - 2) continue;
                if (word.X - anchor.Right > maxGap) continue;
                if (!Regex.IsMatch(word.Text, @"^\d{1,4}$")) continue;
                if (best == null || word.X < best.X) best = word;
            }
            return best != null ? int.Parse(best.Text) : 0;
        }

        private static bool SameRow(Word a, Word b)
        {
            return Math.Abs(a.CenterY - b.CenterY) < Math.Max(a.H, b.H) * 0.6;
        }

        private static string Normalize(string text)
        {
            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder result = new StringBuilder();
            foreach (char c in decomposed)
            {
                if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }
    }
}
