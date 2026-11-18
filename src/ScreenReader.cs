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
        public int Success;
        public bool SuccessFromAccount;
        public DateTime At;
        public string Note = "";
        public string Diagnostic = "";   // details de lecture, pour le journal seulement
        public readonly List<string> Events = new List<string>();
        // Lignes du chat visibles (texte apres l'heure), pour reperer les nouvelles d'une lecture a l'autre.
        public readonly List<string> ChatLines = new List<string>();
        // Succes debloques lus a l'ecran (« nom (catégorie) ») : fenetre des recompenses a recuperer.
        public readonly List<string> Unlocked = new List<string>();

        // Garde les valeurs d'un releve precedent que celui-ci n'a pas lues (total de succes quand le
        // panneau est ferme, par exemple).
        public void KeepFrom(Reading previous)
        {
            if (previous == null) return;
            if (Level == 0 && Omega == 0)
            {
                Level = previous.Level;
                Omega = previous.Omega;
            }
            if (Success == 0)
            {
                Success = previous.Success;
                SuccessFromAccount = previous.SuccessFromAccount;
            }
            // Les evenements du chat deja vus restent dans l'infobulle (les 10 derniers).
            List<string> events = new List<string>(previous.Events);
            events.AddRange(Events);
            Events.Clear();
            Events.AddRange(events.GetRange(Math.Max(0, events.Count - 10), Math.Min(10, events.Count)));
        }

        public string Values()
        {
            return Level + "/" + Omega + "/" + Success + "/" + SuccessFromAccount;
        }

        public string Summary()
        {
            List<string> parts = new List<string>();
            if (Omega > 0) parts.Add("Niv. 200 · Ω " + Omega);
            else if (Level > 0) parts.Add("Niv. " + Level);
            if (Success > 0)
            {
                parts.Add(Success.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))
                    + (SuccessFromAccount ? " succès (compte)" : " succès"));
            }
            if (parts.Count == 0) parts.Add("rien de lu");
            return string.Join(" · ", parts.ToArray()) + " · relevé à " + At.ToString("HH:mm");
        }
    }

    // Lecture d'ecran (prototype) : capture la fenetre du jeu, la passe a la reconnaissance de texte
    // de Windows, et y cherche niveau, omega et total des succes par leur texte, ou qu'ils soient
    // (les interfaces du jeu se deplacent). N'agit jamais sur le jeu ; l'image n'est pas gardee.
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
                if (reading.Note.Length > 0 && reading.Level == 0 && reading.Success == 0) log.AppendLine("=== " + name);
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

        // Zone utile de la fenetre : panneau des succes, chat, barre d'XP, fiche Caracteristiques.
        public class Zone
        {
            public string Kind;
            public Rectangle Rect;
        }

        // Lecture complete d'une image, avec les zones utiles reperees (coordonnees de l'image).
        public static void ReadFull(Reading reading, Bitmap image, string name, List<Zone> zones, List<string> panelLines)
        {
            List<Word> words;
            List<string> lines;
            Recognize(image, out words, out lines);
            Parse(reading, words, lines, image, name);
            FindZones(words, lines, new Rectangle(0, 0, image.Width, image.Height), zones);
            foreach (Zone zone in zones)
            {
                if (zone.Kind != "succes") continue;
                foreach (Word word in words)
                {
                    if (zone.Rect.Contains((int)word.X, (int)word.Y)) { CollectPanel(lines, words, zone.Rect, panelLines); break; }
                }
            }
        }

        // Lecture d'une zone deja decoupee (image de la zone seule).
        public static void ReadZone(Reading reading, Bitmap zoneImage, string kind, string name, List<string> panelLines)
        {
            List<Word> words;
            List<string> lines;
            Recognize(zoneImage, out words, out lines);
            Parse(reading, words, lines, zoneImage, name);
            if (kind == "succes") CollectPanel(lines, words, new Rectangle(0, 0, zoneImage.Width, zoneImage.Height), panelLines);
        }

        // Textes du panneau des succes (pour constituer le catalogue), sans les positions.
        private static void CollectPanel(List<string> lines, List<Word> words, Rectangle rect, List<string> panelLines)
        {
            if (panelLines == null) return;
            HashSet<int> inside = new HashSet<int>();
            foreach (Word word in words)
            {
                if (rect.Contains((int)word.X, (int)word.Y)) inside.Add(word.Line);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Substring(12).Trim();
                // Une heure (« [19:13] », « [19:131 ») trahit une ligne du chat.
                if (inside.Contains(i) && !Regex.IsMatch(text, @"\d{1,2}\s*[:.]\s*\d{2}\s*[\]1l]")) panelLines.Add(text);
            }
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

            // Panneau des succes, a ses vrais bords : de la colonne des categories (« Compte »,
            // « PROGRESSION GÉNÉRALE ») au bord droit, sans deborder sur le chat ni sur la fiche
            // voisine (sinon chaque message du chat ferait relire tout le panneau).
            Word title = null;
            foreach (Word word in words)
            {
                if ((word.Text == "Succès" || word.Text == "Succes") && (title == null || word.H > title.H)) title = word;
            }
            if (title != null)
            {
                int left = (int)title.X - 580;
                foreach (Word word in words)
                {
                    bool column = word.Text == "Compte" || word.Text == "PROGRESSION" || word.Text == "Rechercher...";
                    if (column && Math.Abs(word.Y - title.Y) < 200 && word.X < title.X) left = Math.Max(left, (int)word.X - 50);
                }
                int bottom = (int)title.Y + 850;
                if (!chat.IsEmpty && chat.Right > left && chat.Top > title.Y) bottom = Math.Min(bottom, chat.Top - 4);
                int right = (int)title.X + 640;
                Add(zones, "succes", Rectangle.FromLTRB(left, (int)title.Y - 24, right, bottom), bounds);
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

            // Barre d'XP (« NIV. 152 » ou icone omega) et fiche Caracteristiques (« OMÉGA 152 | ENUTROF »).
            foreach (Word word in words)
            {
                if (word.Text == "NIV" || word.Text == "NIV." || word.Text == "n")
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
                    if (achievement != null) report.AppendLine("  succès débloqué : " + achievement);
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

            // Barre d'XP : « NIV. 152 » sans omega, sinon le niveau omega seul sur une pastille doree.
            if (reading.Level == 0 && reading.Omega == 0)
            {
                foreach (Word word in words)
                {
                    if (word.Text != "NIV" && word.Text != "NIV.") continue;
                    int value = NumberRightOf(words, word, 120);
                    if (value > 0 && value <= 200) reading.Level = value;
                }
            }

            Word successNumber = ReadSuccess(reading, words, image);

            if (reading.Level == 0 && reading.Omega == 0)
            {
                foreach (Word word in words)
                {
                    if (word == successNumber || !Regex.IsMatch(word.Text, @"^\d{1,4}$")) continue;
                    if (GoldShare(image, word) < 0.25) continue;
                    reading.Omega = int.Parse(word.Text);
                }
            }
            if (reading.Level == 0 && reading.Omega == 0) reading.Omega = ZoomOmega(reading, words, image);
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
        // « Osmalie vient de passer au niveau 92. » (« niveau Oméga N » suppose, pas encore vu).
        private static readonly Regex OwnAchievement = new Regex(
            @"succ\S{0,2}s\s+d\S{0,2}verrouill\S{0,2}\s*:?\s*\[([^\]]+)", RegexOptions.IgnoreCase);
        private static readonly Regex LevelUp = new Regex(
            @"(\S+)\s+vient\s+de\s+passer\s+au\s+niveau\s+(om\S{0,2}ga\s+)?(\d{1,4})", RegexOptions.IgnoreCase);

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

        // Ligne du chat qui parle d'un succes, d'un niveau ou de l'omega.
        public static bool IsProgressLine(string line)
        {
            string lower = line.ToLowerInvariant();
            return lower.Contains("succès") || lower.Contains("succes") || lower.Contains("niveau")
                || lower.Contains("oméga") || lower.Contains("omega");
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

        // L'icone omega est lue « n » ; la pastille doree a sa droite porte le nombre. On relit cette
        // zone agrandie trois fois quand la premiere lecture ne l'a pas donne.
        private static int ZoomOmega(Reading reading, List<Word> words, Bitmap image)
        {
            foreach (Word icon in words)
            {
                if (icon.Text != "n" && icon.Text != "∩" && icon.Text != "Ω") continue;
                Rectangle zone = Rectangle.Intersect(new Rectangle((int)icon.Right + 2, (int)icon.Y - 8, 110, (int)icon.H + 16),
                    new Rectangle(0, 0, image.Width, image.Height));
                if (zone.Width < 20) continue;
                Color average = AverageColor(image, zone);
                reading.Diagnostic += string.Format("zoom à droite de « {0} » ({1},{2}) : doré {3:0} %, couleur moyenne {4},{5},{6}",
                    icon.Text, (int)icon.X, (int)icon.Y, GoldShare(image, zone) * 100, average.R, average.G, average.B);
                using (Bitmap zoom = new Bitmap(zone.Width * 3, zone.Height * 3, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(zoom))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, new Rectangle(0, 0, zoom.Width, zoom.Height), zone, GraphicsUnit.Pixel);
                    }
                    List<Word> found;
                    List<string> ignored;
                    Recognize(zoom, out found, out ignored);
                    reading.Diagnostic += ", lu « " + string.Join(" ", ignored.ToArray()) + " » ; ";
                    foreach (Word word in found)
                    {
                        string digits = Regex.Replace(word.Text, @"\D", "");
                        if (digits.Length >= 1 && digits.Length <= 4) return int.Parse(digits);
                    }
                }
            }
            return 0;
        }

        // Panneau des succes : le total, juste sous le titre « Succès », si « Personnage » est actif.
        private static Word ReadSuccess(Reading reading, List<Word> words, Bitmap image)
        {
            // Le titre du panneau est le « Succès » ecrit le plus grand (le mot revient dans les cartes).
            Word title = null;
            foreach (Word word in words)
            {
                if (word.Text != "Succès" && word.Text != "Succes") continue;
                if (title == null || word.H > title.H) title = word;
            }
            if (title == null) return null;

            // Le nombre peut etre coupe en plusieurs mots (« 14 751e ») : on regroupe ceux d'une meme ligne.
            Word best = null;
            int bestValue = 0;
            double bestDistance = double.MaxValue;
            Dictionary<int, List<Word>> byLine = new Dictionary<int, List<Word>>();
            foreach (Word word in words)
            {
                if (word.Y <= title.Y + title.H || word.Y > title.Y + 140) continue;
                if (Math.Abs(word.X - title.X) > 400) continue;
                List<Word> group;
                if (!byLine.TryGetValue(word.Line, out group)) byLine[word.Line] = group = new List<Word>();
                group.Add(word);
            }
            foreach (List<Word> group in byLine.Values)
            {
                StringBuilder text = new StringBuilder();
                foreach (Word word in group) text.Append(word.Text).Append(' ');
                Match match = Regex.Match(text.ToString().Trim(), @"^(\d{1,3}(?:\s\d{3})*)\D{1,3}$");
                if (!match.Success) continue;
                double distance = group[0].Y - title.Y;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestValue = int.Parse(Regex.Replace(match.Groups[1].Value, @"\s", ""));
                best = group[0];
            }
            if (best == null)
            {
                // Secours : la carte « Obtenir N points de Succès » montre « 14751/16000 ».
                foreach (Word word in words)
                {
                    Match progress = Regex.Match(word.Text, @"^(\d{1,6})/(\d{3,6})$");
                    if (!progress.Success) continue;
                    int done = int.Parse(progress.Groups[1].Value);
                    if (done >= int.Parse(progress.Groups[2].Value)) continue;
                    foreach (Word other in words)
                    {
                        if (other.Text != "points" || other.Y >= word.Y || other.Y < word.Y - 90) continue;
                        if (Math.Abs(other.X - word.X) > 300) continue;
                        best = word;
                        bestValue = done;
                    }
                }
                if (best == null) return null;
            }

            reading.Success = bestValue;
            Word person = null;
            Word account = null;
            foreach (Word word in words)
            {
                if (Math.Abs(word.CenterY - best.CenterY) > 200 && Math.Abs(word.CenterY - title.CenterY) > 200) continue;
                if (word.Text == "Personnage") person = word;
                if (word.Text == "Compte") account = word;
            }
            if (person != null && IsGreen(image, person)) return best;
            if (account != null && IsGreen(image, account)) reading.SuccessFromAccount = true;
            else reading.Note = "onglet Compte / Personnage non reconnu";
            return best;
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

        // Part de pixels dores autour d'un mot (pastille omega).
        private static double GoldShare(Bitmap image, Word word)
        {
            return GoldShare(image, new Rectangle((int)word.X - 6, (int)word.Y - 4, (int)word.W + 12, (int)word.H + 8));
        }

        private static double GoldShare(Bitmap image, Rectangle zone)
        {
            int gold = 0, total = 0;
            int left = Math.Max(0, zone.Left), right = Math.Min(image.Width - 1, zone.Right);
            int top = Math.Max(0, zone.Top), bottom = Math.Min(image.Height - 1, zone.Bottom);
            for (int x = left; x <= right; x += 2)
            {
                for (int y = top; y <= bottom; y += 2)
                {
                    Color c = image.GetPixel(x, y);
                    total++;
                    if (c.R > 170 && c.G > 100 && c.G < 215 && c.B < 100 && c.R > c.G) gold++;
                }
            }
            return total == 0 ? 0 : (double)gold / total;
        }

        private static Color AverageColor(Bitmap image, Rectangle zone)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;
            for (int x = zone.Left; x < zone.Right; x += 2)
            {
                for (int y = zone.Top; y < zone.Bottom; y += 2)
                {
                    Color c = image.GetPixel(x, y);
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    count++;
                }
            }
            if (count == 0) return Color.Black;
            return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
        }

        // Texte vert (onglet actif) : moyenne des pixels clairs du mot.
        private static bool IsGreen(Bitmap image, Word word)
        {
            long r = 0, g = 0, b = 0;
            int count = 0;
            for (int x = (int)word.X; x < (int)word.Right && x < image.Width; x++)
            {
                for (int y = (int)word.Y; y < (int)(word.Y + word.H) && y < image.Height; y++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.R + c.G + c.B < 300) continue;
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    count++;
                }
            }
            if (count == 0) return false;
            return g / count > r / count + 25 && g / count > b / count + 25;
        }
    }
}
