using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DofuxyaManager
{
    // Icones de classe, integrees a l'executable (assets\icones, liste dans assets\icones.rsp).
    // Elles sont lues en memoire a la demande : rien n'est ecrit sur le disque.
    internal class IconLibrary
    {
        private const string Prefix = "icones.";
        private const int BigSize = 48;
        private const int SmallSize = 24;

        // Les fichiers integres ont des noms sans accent : on leur rend le nom de la classe.
        private static readonly Dictionary<string, string> Accented = new Dictionary<string, string>
        {
            { "Feca", "Féca" },
            { "Cra", "Crâ" },
            { "Xelor", "Xélor" },
        };

        private readonly Assembly assembly = Assembly.GetExecutingAssembly();
        private readonly Dictionary<string, string> resources = new Dictionary<string, string>();
        private readonly Dictionary<string, IntPtr> bigHandles = new Dictionary<string, IntPtr>();
        private readonly Dictionary<string, IntPtr> smallHandles = new Dictionary<string, IntPtr>();
        private readonly Dictionary<string, ImageSource> previews = new Dictionary<string, ImageSource>();
        // Les icones posees sur les fenetres doivent rester en vie tant que l'outil tourne.
        private readonly List<System.Drawing.Icon> keepAlive = new List<System.Drawing.Icon>();

        public readonly List<string> Keys = new List<string>();

        public IconLibrary()
        {
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(Prefix, StringComparison.Ordinal)
                    || !resource.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) continue;
                string key = Path.GetFileNameWithoutExtension(resource.Substring(Prefix.Length));
                string accented;
                if (Accented.TryGetValue(key, out accented)) key = accented;
                resources[key] = resource;
                Keys.Add(key);
            }
            Keys.Sort(StringComparer.OrdinalIgnoreCase);
            RemoveOldCopies();
        }

        // Jusqu'a la version 1.2.3, les icones etaient copiees dans %AppData%\Dofuxya\icones-integrees.
        private static void RemoveOldCopies()
        {
            try
            {
                string old = Path.Combine(AppPaths.DataFolder, "icones-integrees");
                if (Directory.Exists(old)) Directory.Delete(old, true);
            }
            catch (Exception)
            {
            }
        }

        // Trouve l'icone correspondant a un nom de classe ("Féca", "feca" ou "Feca" -> "Féca").
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
            return key != null && resources.ContainsKey(key);
        }

        public IntPtr GetBig(string key)
        {
            return GetHandle(key, BigSize, bigHandles);
        }

        public IntPtr GetSmall(string key)
        {
            return GetHandle(key, SmallSize, smallHandles);
        }

        // Image pour l'interface : la plus grande taille du .ico, la plus nette.
        public ImageSource GetPreview(string key)
        {
            if (!Has(key)) return null;
            ImageSource image;
            if (previews.TryGetValue(key, out image)) return image;
            image = null;
            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resources[key]))
                {
                    IconBitmapDecoder decoder = new IconBitmapDecoder(stream,
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
                using (Stream stream = assembly.GetManifestResourceStream(resources[key]))
                {
                    System.Drawing.Icon icon = new System.Drawing.Icon(stream, size, size);
                    keepAlive.Add(icon);
                    handle = icon.Handle;
                }
            }
            catch (Exception)
            {
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
