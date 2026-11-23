using System.IO;
using System.Net;
using System.Text;

namespace DofuxyaManager
{
    // Requetes reseau de l'outil (mises a jour, liaison au site) : un seul endroit pour TLS 1.2,
    // l'en-tete User-Agent et les delais maximaux (sans reseau, l'outil ne reste jamais bloque).
    internal static class Http
    {
        static Http()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }

        // Requete prete a envoyer, avec le meme delai pour la connexion et pour la lecture.
        public static HttpWebRequest Create(string url, string method, int timeoutMs)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.UserAgent = "Dofuxya/" + AppVersion.Number;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            return request;
        }

        // Texte d'une adresse, sans cache intermediaire (fichier de version).
        public static string GetString(string url, int timeoutMs)
        {
            HttpWebRequest request = Create(url, "GET", timeoutMs);
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // Telecharge un fichier (remplace celui qui existe deja a cet endroit).
        public static void DownloadFile(string url, string path, int timeoutMs)
        {
            HttpWebRequest request = Create(url, "GET", timeoutMs);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                input.CopyTo(output);
            }
        }
    }
}
