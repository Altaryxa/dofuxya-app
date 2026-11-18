using System;
using System.Collections.Generic;
using System.Drawing;

namespace DofuxyaManager
{
    // Lecture continue de la fenetre Dofus au premier plan (prototype, F-50), faite pour ne pas peser
    // sur le jeu :
    // - une lecture complete toutes les 20 s (ou au changement de fenetre) repere les zones utiles :
    //   panneau des succes, chat, barre d'XP, fiche Caracteristiques ;
    // - chaque seconde, seules ces zones sont copiees depuis l'ecran et comparees a grands traits ;
    // - une zone n'est relue que si elle a change ET qu'elle est stable depuis la seconde precedente
    //   (on attend la fin d'un defilement avant de lire).
    // Appele toujours depuis un seul fil a la fois.
    internal class ScreenWatcher
    {
        private static readonly TimeSpan FullEvery = TimeSpan.FromSeconds(20);

        private class Watched
        {
            public ScreenReader.Zone Zone;
            public byte[] Read;       // empreinte au moment de la derniere lecture
            public byte[] Previous;   // empreinte de la seconde precedente
        }

        private IntPtr window = IntPtr.Zero;
        private DateTime lastFull = DateTime.MinValue;
        private readonly List<Watched> zones = new List<Watched>();
        // Notification « Succès déverrouillé » : elle apparait quelques secondes dans un coin de la
        // fenetre, sur fond jaune vif. Chaque seconde, on mesure seulement la part de jaune de chaque
        // coin ; son texte n'est lu que quand cette part bondit (au plus une fois toutes les 3 s par coin).
        private static readonly TimeSpan CornerPause = TimeSpan.FromSeconds(3);
        private readonly double[] cornerYellow = new double[4];
        private readonly DateTime[] cornerRead = new DateTime[4];

        // Renvoie un releve quand quelque chose a ete lu, sinon null. panelLines recoit les textes du
        // panneau des succes lus a cette occasion.
        public Reading Tick(IntPtr hWnd, string name, List<string> panelLines)
        {
            if (hWnd != window || DateTime.UtcNow - lastFull > FullEvery) return Full(hWnd, name, panelLines);

            Reading reading = WatchCorners(hWnd, name);
            foreach (Watched watched in zones)
            {
                using (Bitmap image = ScreenReader.CaptureArea(hWnd, watched.Zone.Rect))
                {
                    if (image == null) continue;
                    byte[] signature = ScreenReader.Signature(image);
                    bool stable = !ScreenReader.Differs(signature, watched.Previous);
                    watched.Previous = signature;
                    if (!stable || !ScreenReader.Differs(signature, watched.Read)) continue;
                    watched.Read = signature;
                    if (reading == null) reading = new Reading { At = DateTime.Now };
                    ScreenReader.ReadZone(reading, image, watched.Zone.Kind, name, panelLines);
                }
            }
            return reading;
        }

        private Reading WatchCorners(IntPtr hWnd, string name)
        {
            Reading reading = null;
            Rectangle[] corners = ScreenReader.Corners(hWnd);
            for (int i = 0; i < corners.Length; i++)
            {
                using (Bitmap image = ScreenReader.CaptureArea(hWnd, corners[i]))
                {
                    if (image == null) continue;
                    double yellow = ScreenReader.YellowShare(image);
                    // La notification occupe environ 13 % d'un coin et y est jaune aux deux tiers.
                    bool appeared = yellow > 0.1 && yellow - cornerYellow[i] > 0.07;
                    cornerYellow[i] = yellow;
                    if (!appeared || DateTime.UtcNow - cornerRead[i] < CornerPause) continue;
                    cornerRead[i] = DateTime.UtcNow;
                    if (reading == null) reading = new Reading { At = DateTime.Now };
                    ScreenReader.ReadZone(reading, image, "notification", name, null);
                }
            }
            return reading;
        }

        private Reading Full(IntPtr hWnd, string name, List<string> panelLines)
        {
            window = hWnd;
            lastFull = DateTime.UtcNow;
            zones.Clear();
            using (Bitmap image = ScreenReader.CaptureArea(hWnd, Rectangle.Empty))
            {
                if (image == null) return null;
                Reading reading = new Reading { At = DateTime.Now };
                List<ScreenReader.Zone> found = new List<ScreenReader.Zone>();
                ScreenReader.ReadFull(reading, image, name, found, panelLines);
                // Les coins sont surveilles a part (WatchCorners).
                found.RemoveAll(delegate(ScreenReader.Zone z) { return z.Kind == "notification"; });
                foreach (ScreenReader.Zone zone in found)
                {
                    using (Bitmap part = image.Clone(zone.Rect, image.PixelFormat))
                    {
                        byte[] signature = ScreenReader.Signature(part);
                        zones.Add(new Watched { Zone = zone, Read = signature, Previous = signature });
                    }
                }
                return reading;
            }
        }
    }
}
