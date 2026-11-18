using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DofuxyaManager
{
    // Liaison avec le compte dofuxya.com et envoi des releves (prototype, F-50).
    // La cle de l'appareil est rangee dans %AppData%\Dofuxya, chiffree par Windows pour la session
    // de l'utilisateur. L'outil n'envoie que des valeurs (nom du perso, niveau, omega, succes).
    internal static class SiteLink
    {
        private static readonly string KeyPath = Path.Combine(AppPaths.DataFolder, "site-cle.bin");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public class SiteError : Exception
        {
            public readonly int Status;

            public SiteError(int status, string message) : base(message)
            {
                Status = status;
            }
        }

        public static bool IsLinked
        {
            get { return LoadToken() != null; }
        }

        // ---------- Liaison ----------

        // Ouvre la page d'autorisation dans le navigateur, puis attend la cle (10 minutes au plus).
        // A appeler hors du fil de l'interface. Renvoie true si l'appareil est lie.
        public static bool Link(string site, Func<bool> cancelled)
        {
            byte[] random = new byte[32];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create()) generator.GetBytes(random);
            string code = Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Process.Start(new ProcessStartInfo(site + "/lier-manager?code=" + code) { UseShellExecute = true });

            DateTime limit = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < limit && (cancelled == null || !cancelled()))
            {
                Thread.Sleep(2000);
                try
                {
                    Dictionary<string, object> answer = Post(site, "/api/manager/claim", null,
                        new Dictionary<string, object> { { "code", code } });
                    object token;
                    if (answer.TryGetValue("token", out token) && token is string)
                    {
                        SaveToken((string)token);
                        return true;
                    }
                }
                catch (SiteError error)
                {
                    if (error.Status != 404) throw;
                }
            }
            return false;
        }

        public static void Unlink()
        {
            try
            {
                if (File.Exists(KeyPath)) File.Delete(KeyPath);
            }
            catch (Exception)
            {
            }
        }

        // ---------- Envoi ----------

        // Valeurs actuelles sur le site des persos releves (cle « name », « found », « level »…).
        public static List<Dictionary<string, object>> Preview(string site, List<Dictionary<string, object>> readings)
        {
            Dictionary<string, object> answer = Post(site, "/api/manager/preview", RequireToken(),
                new Dictionary<string, object> { { "readings", readings } });
            List<Dictionary<string, object>> characters = new List<Dictionary<string, object>>();
            object list;
            if (answer.TryGetValue("characters", out list) && list is System.Collections.ArrayList)
            {
                foreach (object item in (System.Collections.ArrayList)list)
                {
                    Dictionary<string, object> character = item as Dictionary<string, object>;
                    if (character != null) characters.Add(character);
                }
            }
            return characters;
        }

        // Enregistre les releves sur le site. Renvoie le nombre de persos mis a jour.
        public static int Apply(string site, List<Dictionary<string, object>> readings)
        {
            Dictionary<string, object> answer = Post(site, "/api/manager/apply", RequireToken(),
                new Dictionary<string, object> { { "readings", readings } });
            object updated;
            return answer.TryGetValue("updated", out updated) ? Convert.ToInt32(updated) : 0;
        }

        // ---------- Echanges ----------

        private static string RequireToken()
        {
            string token = LoadToken();
            if (token == null) throw new SiteError(401, "outil non lié au site");
            return token;
        }

        private static Dictionary<string, object> Post(string site, string path, string token,
            Dictionary<string, object> body)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(site + path);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.UserAgent = "Dofuxya/" + AppVersion.Number;
            request.Timeout = 15000;
            if (token != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            byte[] payload = Encoding.UTF8.GetBytes(Json.Serialize(body));
            using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return Read(response);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response == null) throw new SiteError(0, "site injoignable (" + ex.Message + ")");
                using (response)
                {
                    Dictionary<string, object> error = Read(response);
                    object message;
                    string text = error.TryGetValue("error", out message) ? Convert.ToString(message) : response.StatusDescription;
                    if ((int)response.StatusCode == 401) Unlink();
                    throw new SiteError((int)response.StatusCode, text);
                }
            }
        }

        private static Dictionary<string, object> Read(HttpWebResponse response)
        {
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                try
                {
                    return Json.Deserialize<Dictionary<string, object>>(text) ?? new Dictionary<string, object>();
                }
                catch (Exception)
                {
                    return new Dictionary<string, object>();
                }
            }
        }

        // ---------- Cle de l'appareil ----------

        private static void SaveToken(string token)
        {
            byte[] secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KeyPath, secret);
        }

        private static string LoadToken()
        {
            try
            {
                if (!File.Exists(KeyPath)) return null;
                byte[] secret = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(secret);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
