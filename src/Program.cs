using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
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
            if (args.Length >= 3 && args[0] == "--essai-lecture")
            {
                ScreenReader.TestFiles(args);
                return;
            }
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
                + "\n\nJoins ce fichier si tu signales le problème.", "Dofuxya");
        }
    }
}
