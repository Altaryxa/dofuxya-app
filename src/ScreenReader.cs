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
        public readonly List<string> Events = new List<string>();

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
                foreach (string line in reading.Events) report.AppendLine("  chat : " + line);
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
            if (reading.Level == 0 && reading.Omega == 0) reading.Omega = ZoomOmega(words, image);
            if (reading.Omega > 0) reading.Level = 200;

            // Chat : lignes qui citent le perso (ou « Vous ») avec succès, niveau ou oméga.
            foreach (string line in lines)
            {
                string lower = line.ToLowerInvariant();
                bool aboutMe = lower.Contains(name.ToLowerInvariant()) || lower.Contains("vous ");
                bool topic = lower.Contains("succès") || lower.Contains("niveau") || lower.Contains("oméga")
                    || lower.Contains("omega");
                if (aboutMe && topic && lower.Contains("[")) reading.Events.Add(line.Substring(12).Trim());
            }
        }

        // L'icone omega est lue « n » ; la pastille doree a sa droite porte le nombre. On relit cette
        // zone agrandie trois fois quand la premiere lecture ne l'a pas donne.
        private static int ZoomOmega(List<Word> words, Bitmap image)
        {
            foreach (Word icon in words)
            {
                if (icon.Text != "n" && icon.Text != "∩" && icon.Text != "Ω") continue;
                Rectangle zone = Rectangle.Intersect(new Rectangle((int)icon.Right + 2, (int)icon.Y - 8, 90, (int)icon.H + 16),
                    new Rectangle(0, 0, image.Width, image.Height));
                if (zone.Width < 20 || GoldShare(image, zone) < 0.25) continue;
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

        private static bool ClassOnRow(List<Word> words, Word anchor)
        {
            foreach (Word word in words)
            {
                if (!SameRow(word, anchor) || word.X < anchor.X || word.X - anchor.Right > 400) continue;
                string normalized = Normalize(word.Text);
                foreach (string cls in Classes)
                {
                    if (normalized == cls) return true;
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
