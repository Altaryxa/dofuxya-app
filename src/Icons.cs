using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DofuxyaManager
{
    // Icones de classe : un fichier image par classe dans le dossier "icones" a cote de l'executable.
    // Le nom du fichier (sans extension) sert de nom de classe : pandawa.png, iop.ico...
    internal class IconLibrary
    {
        private const int BigSize = 48;
        private const int SmallSize = 24;

        private readonly Dictionary<string, string> paths = new Dictionary<string, string>();
        private readonly Dictionary<string, IntPtr> bigHandles = new Dictionary<string, IntPtr>();
        private readonly Dictionary<string, IntPtr> smallHandles = new Dictionary<string, IntPtr>();
        private readonly Dictionary<string, ImageSource> previews = new Dictionary<string, ImageSource>();
        private readonly List<object> keepAlive = new List<object>();

        public readonly List<string> Keys = new List<string>();
        public readonly string Folder;
        private readonly string builtInFolder;

        public IconLibrary()
        {
            Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icones");
            builtInFolder = Path.Combine(AppPaths.DataFolder, "icones-integrees");
            try
            {
                Directory.CreateDirectory(Folder);
            }
            catch (Exception)
            {
            }
            ExtractBuiltIn();
            Reload();
        }

        // Relit le dossier. Renvoie true si la liste des fichiers a change.
        // Copie les icones integrees a l'executable dans un dossier de travail, une fois.
        private void ExtractBuiltIn()
        {
            try
            {
                Directory.CreateDirectory(builtInFolder);
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string resource in assembly.GetManifestResourceNames())
                {
                    if (!resource.StartsWith("icones.", StringComparison.Ordinal)) continue;
                    string fileName = DisplayName(resource.Substring("icones.".Length));
                    string target = Path.Combine(builtInFolder, fileName);
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                    {
                        if (stream == null) continue;
                        if (File.Exists(target) && new FileInfo(target).Length == stream.Length) continue;
                        using (FileStream output = File.Create(target))
                        {
                            stream.CopyTo(output);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        // Les fichiers integres ont des noms sans accent : on leur rend le nom de la classe.
        private static string DisplayName(string fileName)
        {
            if (fileName.StartsWith("Feca.")) return "Féca." + fileName.Substring(5);
            if (fileName.StartsWith("Cra.")) return "Crâ." + fileName.Substring(4);
            if (fileName.StartsWith("Xelor.")) return "Xélor." + fileName.Substring(6);
            return fileName;
        }

        private static void Collect(string folder, Dictionary<string, string> found, List<string> taken)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".png" && extension != ".ico" && extension != ".jpg"
                        && extension != ".jpeg" && extension != ".bmp" && extension != ".gif") continue;
                    string key = Path.GetFileNameWithoutExtension(file);
                    string normalized = Normalize(key);
                    if (taken.Contains(normalized)) continue;
                    taken.Add(normalized);
                    found[key] = file;
                }
            }
            catch (Exception)
            {
            }
        }

        public bool Reload()
        {
            // Le dossier "icones" de l'utilisateur passe avant les icones integrees.
            Dictionary<string, string> found = new Dictionary<string, string>();
            List<string> taken = new List<string>();
            Collect(Folder, found, taken);
            Collect(builtInFolder, found, taken);

            bool same = found.Count == paths.Count;
            if (same)
            {
                foreach (KeyValuePair<string, string> pair in found)
                {
                    string existing;
                    if (!paths.TryGetValue(pair.Key, out existing) || existing != pair.Value)
                    {
                        same = false;
                        break;
                    }
                }
            }
            if (same) return false;

            paths.Clear();
            Keys.Clear();
            bigHandles.Clear();
            smallHandles.Clear();
            previews.Clear();
            foreach (KeyValuePair<string, string> pair in found)
            {
                paths[pair.Key] = pair.Value;
                Keys.Add(pair.Key);
            }
            Keys.Sort(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        // Trouve l'icone correspondant a un nom de classe ("Féca" -> fichier "feca.png").
        public string Match(string className)
        {
            string wanted = Normalize(className);
            if (wanted.Length == 0) return "";
            foreach (string key in Keys)
            {
                if (Normalize(key) == wanted) return key;
            }
            foreach (string key in Keys)
            {
                string normalized = Normalize(key);
                if (normalized.Length >= 3 && (normalized.Contains(wanted) || wanted.Contains(normalized)))
                {
                    return key;
                }
            }
            return "";
        }

        public bool Has(string key)
        {
            return key != null && paths.ContainsKey(key);
        }

        public IntPtr GetBig(string key)
        {
            return GetHandle(key, BigSize, bigHandles);
        }

        public IntPtr GetSmall(string key)
        {
            return GetHandle(key, SmallSize, smallHandles);
        }

        public ImageSource GetPreview(string key)
        {
            if (!Has(key)) return null;
            ImageSource cached;
            if (previews.TryGetValue(key, out cached)) return cached;
            ImageSource image = null;
            try
            {
                string file = paths[key];
                if (Path.GetExtension(file).ToLowerInvariant() == ".ico")
                {
                    // Un .ico contient plusieurs tailles : on prend la plus grande, la plus nette.
                    IconBitmapDecoder decoder = new IconBitmapDecoder(new Uri(file),
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapFrame best = null;
                    foreach (BitmapFrame frame in decoder.Frames)
                    {
                        if (best == null || frame.PixelWidth > best.PixelWidth
                            || (frame.PixelWidth == best.PixelWidth
                                && frame.Format.BitsPerPixel > best.Format.BitsPerPixel))
                        {
                            best = frame;
                        }
                    }
                    if (best != null)
                    {
                        if (best.CanFreeze) best.Freeze();
                        image = best;
                    }
                }
                else
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(file);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch (Exception)
            {
            }
            previews[key] = image;
            return image;
        }

        private IntPtr GetHandle(string key, int size, Dictionary<string, IntPtr> cache)
        {
            if (!Has(key)) return IntPtr.Zero;
            IntPtr handle;
            if (cache.TryGetValue(key, out handle)) return handle;
            handle = IntPtr.Zero;
            try
            {
                string file = paths[key];
                if (Path.GetExtension(file).ToLowerInvariant() == ".ico")
                {
                    System.Drawing.Icon icon = new System.Drawing.Icon(file, size, size);
                    keepAlive.Add(icon);
                    handle = icon.Handle;
                }
                else
                {
                    using (System.Drawing.Image source = System.Drawing.Image.FromFile(file))
                    {
                        System.Drawing.Bitmap scaled = new System.Drawing.Bitmap(size, size,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(scaled))
                        {
                            graphics.InterpolationMode =
                                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            graphics.Clear(System.Drawing.Color.Transparent);
                            graphics.DrawImage(source, 0, 0, size, size);
                        }
                        keepAlive.Add(scaled);
                        handle = scaled.GetHicon();
                    }
                }
            }
            catch (Exception)
            {
                handle = IntPtr.Zero;
            }
            cache[key] = handle;
            return handle;
        }

        // Minuscules, sans accents ni ponctuation.
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder result = new StringBuilder();
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) result.Append(c);
            }
            return result.ToString();
        }
    }
}
