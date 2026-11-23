using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DofuxyaManager
{
    // Mises a jour automatiques, silencieuses et obligatoires, faites au lancement, avant que la
    // fenetre de l'outil ne s'ouvre. L'outil lit sur le site de l'auteur un petit fichier texte
    // (version, adresse de l'installateur, empreinte SHA-256). Si une version plus recente existe,
    // l'installateur est telecharge, verifie, puis lance en silence ; il relance l'outil a la fin.
    // Rien d'autre n'est envoye que la requete elle-meme.
    internal static class Updater
    {
        // Resultat de la verification du lancement, affiche dans l'onglet Options.
        public static string State = "pas encore vérifié";

        public static bool Configured
        {
            get
            {
                return AppVersion.UpdateBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string ManifestUrl
        {
            get
            {
                string baseUrl = AppVersion.UpdateBase;
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                return baseUrl + "version.txt";
            }
        }

        // Verifie et telecharge. Renvoie le chemin de l'installateur pret a lancer, ou "" s'il n'y a
        // rien a installer. "progress" recoit des messages pour la fenetre d'attente.
        public static string Check(Action<string> progress)
        {
            if (!Configured)
            {
                State = "adresse de mise à jour non configurée (fichier mise-a-jour.txt)";
                return "";
            }
            try
            {
                return CheckCore(progress);
            }
            catch (Exception ex)
            {
                State = "vérification impossible : " + ex.Message;
                return "";
            }
        }

        private static string CheckCore(Action<string> progress)
        {
            // Sans reseau, l'outil ne doit pas rester bloque au lancement : 6 secondes au plus.
            Dictionary<string, string> manifest = new Dictionary<string, string>();
            string text = Http.GetString(ManifestUrl + "?t=" + DateTime.UtcNow.Ticks, 6000);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                manifest[line.Substring(0, separator).Trim().ToLowerInvariant()] =
                    line.Substring(separator + 1).Trim();
            }

            string version;
            string url;
            string hash;
            if (!manifest.TryGetValue("version", out version) || !manifest.TryGetValue("url", out url)
                || !manifest.TryGetValue("sha256", out hash))
            {
                State = "fichier de version incomplet sur le site";
                return "";
            }
            Version remote;
            Version local;
            if (!Version.TryParse(version, out remote) || !Version.TryParse(AppVersion.Number, out local))
            {
                State = "numéro de version illisible : " + version;
                return "";
            }
            if (remote <= local)
            {
                State = "à jour (vérifié à " + DateTime.Now.ToString("HH:mm") + ")";
                return "";
            }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                State = "version " + version + " refusée : son adresse n'est pas en https";
                return "";
            }

            string target = Path.Combine(Path.GetTempPath(), "Dofuxya-Setup-" + version + ".exe");
            hash = hash.Replace(" ", "").ToUpperInvariant();
            if (!(File.Exists(target) && Sha256(target) == hash))
            {
                if (progress != null) progress("Téléchargement de la version " + version + "…");
                string partial = target + ".part";
                Http.DownloadFile(url, partial, 60000);
                if (Sha256(partial) != hash)
                {
                    TryDelete(partial);
                    State = "version " + version + " refusée : le fichier téléchargé ne correspond pas à "
                        + "l'empreinte annoncée";
                    return "";
                }
                TryDelete(target);
                File.Move(partial, target);
            }
            if (progress != null) progress("Installation de la version " + version + "…");
            State = "installation de la version " + version;
            return target;
        }

        // Lance l'installateur en silence ; il relance l'outil une fois l'installation terminee.
        public static bool Launch(string setup)
        {
            if (string.IsNullOrEmpty(setup) || !File.Exists(setup)) return false;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(setup);
                info.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                State = "installation impossible : " + ex.Message;
                return false;
            }
        }

        // Supprime les installateurs laisses dans le dossier temporaire par les mises a jour passees.
        public static void CleanTemp()
        {
            try
            {
                foreach (string file in Directory.GetFiles(Path.GetTempPath(), "Dofuxya*-Setup-*"))
                {
                    TryDelete(file);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder();
                foreach (byte b in digest) text.Append(b.ToString("X2"));
                return text.ToString();
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception)
            {
            }
        }
    }

    // Petite fenetre d'attente pendant la verification du lancement. Elle n'apparait que si la
    // verification dure plus d'un instant, pour ne pas clignoter a chaque ouverture.
    internal class UpdateSplash : System.Windows.Forms.Form
    {
        private readonly System.Windows.Forms.Label label = new System.Windows.Forms.Label();
        private readonly System.Windows.Forms.Timer reveal = new System.Windows.Forms.Timer();
        private string result = "";

        public UpdateSplash()
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            Width = 360;
            Height = 84;
            BackColor = System.Drawing.Color.FromArgb(0x1d, 0x20, 0x25);
            Opacity = 0;

            label.Dock = System.Windows.Forms.DockStyle.Fill;
            label.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            label.ForeColor = System.Drawing.Color.FromArgb(0xf0, 0xf2, 0xee);
            label.Font = new System.Drawing.Font("Segoe UI", 11f);
            label.Text = "Dofuxya · recherche de mise à jour…";
            Controls.Add(label);

            reveal.Interval = 400;
            reveal.Tick += delegate
            {
                reveal.Stop();
                Opacity = 1;
            };
            Shown += delegate
            {
                reveal.Start();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string setup = Updater.Check(delegate(string message)
                    {
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                label.Text = "Dofuxya · " + message;
                                Opacity = 1;
                            }));
                        }
                        catch (Exception)
                        {
                        }
                    });
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            result = setup;
                            reveal.Stop();
                            Close();
                        }));
                    }
                    catch (Exception)
                    {
                    }
                });
            };
        }

        // Affiche la fenetre le temps de la verification. Renvoie l'installateur a lancer, ou "".
        public static string Run()
        {
            using (UpdateSplash splash = new UpdateSplash())
            {
                System.Windows.Forms.Application.Run(splash);
                return splash.result;
            }
        }
    }
}
