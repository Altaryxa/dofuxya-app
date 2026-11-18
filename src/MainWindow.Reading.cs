using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace DofuxyaManager
{
    // Prototype de lecture d'ecran : bouton « Relever » de l'onglet Equipe (visible seulement avec
    // « lecture_ecran=1 » dans reglages.txt). Rien n'est envoye ; le texte lu est ecrit dans
    // %AppData%\Dofuxya\releve-texte.txt pour regler la lecture (aucune image).
    internal partial class MainWindow
    {
        private Button readButton;
        private bool reading;

        private void ReadScreens()
        {
            if (reading) return;
            List<GameWindow> targets = new List<GameWindow>();
            foreach (GameWindow window in NamedWindows())
            {
                if (!window.IsRetro) targets.Add(window);
            }
            if (targets.Count == 0)
            {
                lastMessage = "Aucune fenêtre Dofus 3 à relever.";
                UpdateStatus();
                return;
            }
            reading = true;
            if (readButton != null)
            {
                readButton.IsEnabled = false;
                readButton.Content = "Relevé…";
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder log = new StringBuilder();
                Dictionary<GameWindow, Reading> results = new Dictionary<GameWindow, Reading>();
                DateTime start = DateTime.Now;
                string error = null;
                foreach (GameWindow window in targets)
                {
                    try
                    {
                        results[window] = ScreenReader.Read(window.Handle, window.Name, log);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        break;
                    }
                }
                double seconds = (DateTime.Now - start).TotalSeconds;
                try
                {
                    File.WriteAllText(Path.Combine(AppPaths.DataFolder, "releve-texte.txt"), log.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    foreach (KeyValuePair<GameWindow, Reading> pair in results) pair.Key.LastReading = pair.Value;
                    reading = false;
                    if (readButton != null)
                    {
                        readButton.IsEnabled = true;
                        readButton.Content = "Relever";
                    }
                    lastMessage = error != null
                        ? "Relevé impossible : " + error
                        : results.Count + " fenêtre(s) relevée(s) en " + seconds.ToString("0.0") + " s.";
                    RebuildRows();
                    UpdateStatus();
                }));
            });
        }
    }
}
