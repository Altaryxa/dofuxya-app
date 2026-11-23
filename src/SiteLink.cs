using System;
using System.Collections;
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
    // Liaison avec le compte dofuxya.com (F-50) : liste des persos du compte, Almanax du jour et, pour la
    // lecture d'ecran, envoi des releves. La cle de l'appareil est rangee dans %AppData%\Dofuxya,
    // chiffree par Windows pour la session de l'utilisateur. L'outil n'envoie que des valeurs (nom du
    // perso, niveau, omega, succes et quetes termines).
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
                    Dictionary<string, object> answer = Send("POST", site, "/api/manager/claim", null,
                        new Dictionary<string, object> { { "code", code } });
                    string token = Text(answer, "token");
                    if (token.Length > 0)
                    {
                        SaveToken(token);
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

        // Oublie la cle de l'appareil (le membre peut aussi la revoquer depuis son profil sur le site).
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

        // ---------- Echanges ----------
        // A appeler hors du fil de l'interface.

        // Enregistre les releves sur le site. Renvoie le bilan : « updated » (persos mis a jour),
        // « achievements » (succes valides), « unknown » (succes non reconnus), « quests » (quetes
        // validees) et « unknownQuests ».
        public static Dictionary<string, object> Apply(string site, List<Dictionary<string, object>> readings)
        {
            return Send("POST", site, "/api/manager/apply", RequireToken(),
                new Dictionary<string, object> { { "readings", readings } });
        }

        // Persos du compte lie (Dofus et Retro).
        public static List<SiteCharacter> Characters(string site)
        {
            List<SiteCharacter> result = new List<SiteCharacter>();
            Dictionary<string, object> answer = Send("GET", site, "/api/manager/characters", RequireToken(), null);
            foreach (Dictionary<string, object> raw in Objects(answer, "characters"))
            {
                result.Add(new SiteCharacter
                {
                    Id = Text(raw, "id"),
                    Game = Text(raw, "game") == "retro" ? Scanner.Retro : Scanner.Dofus3,
                    Name = Text(raw, "name"),
                    Level = Number(raw, "level"),
                    Omega = Number(raw, "omega"),
                });
            }
            return result;
        }

        // Almanax du jour, ou null si le site n'en a pas.
        public static AlmanaxDay Almanax(string site)
        {
            object value;
            Dictionary<string, object> answer = Send("GET", site, "/api/manager/almanax", RequireToken(), null);
            Dictionary<string, object> raw = answer.TryGetValue("almanax", out value) ? value as Dictionary<string, object> : null;
            if (raw == null) return null;
            AlmanaxDay day = new AlmanaxDay
            {
                Date = Text(raw, "date"),
                Item = Text(raw, "item"),
                Quantity = Number(raw, "quantity"),
                Bonus = Text(raw, "bonus"),
            };
            day.Missing.AddRange(Texts(raw, "missing"));
            return day;
        }

        // ---------- Lecture des reponses ----------

        public static string Text(Dictionary<string, object> raw, string key)
        {
            object value;
            return raw != null && raw.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }

        public static int Number(Dictionary<string, object> raw, string key)
        {
            object value;
            if (raw == null || !raw.TryGetValue(key, out value) || value == null) return 0;
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static List<string> Texts(Dictionary<string, object> raw, string key)
        {
            List<string> result = new List<string>();
            object value;
            ArrayList list = raw != null && raw.TryGetValue(key, out value) ? value as ArrayList : null;
            if (list != null)
            {
                foreach (object item in list) result.Add(Convert.ToString(item));
            }
            return result;
        }

        private static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> raw, string key)
        {
            object value;
            ArrayList list = raw.TryGetValue(key, out value) ? value as ArrayList : null;
            if (list == null) yield break;
            foreach (object item in list)
            {
                Dictionary<string, object> entry = item as Dictionary<string, object>;
                if (entry != null) yield return entry;
            }
        }

        // ---------- Requetes ----------

        private static string RequireToken()
        {
            string token = LoadToken();
            if (token == null) throw new SiteError(401, "outil non lié au site");
            return token;
        }

        private static Dictionary<string, object> Send(string method, string site, string path, string token,
            Dictionary<string, object> body)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(site + path);
            request.Method = method;
            request.UserAgent = "Dofuxya/" + AppVersion.Number;
            request.Timeout = 15000;
            if (token != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            if (body != null)
            {
                request.ContentType = "application/json; charset=utf-8";
                byte[] payload = Encoding.UTF8.GetBytes(Json.Serialize(body));
                using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
            }
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
                    string text = Text(Read(response), "error");
                    if (text.Length == 0) text = response.StatusDescription;
                    // Cle revoquee depuis le site : l'outil n'est plus lie.
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

    // Un perso du compte lie, tel que le site le connait.
    internal class SiteCharacter
    {
        public string Id = "";
        public string Game = "";
        public string Name = "";
        public int Level;
        public int Omega;

        // « Niv. 200 · Ω 152 », « Niv. 152 », ou vide.
        public string LevelText()
        {
            if (Omega > 0) return "Niv. 200 · Ω " + Omega;
            return Level > 0 ? "Niv. " + Level : "";
        }
    }

    // Almanax du jour : offrande, bonus et persos Dofus du compte qui ne l'ont pas valide.
    internal class AlmanaxDay
    {
        public string Date = "";
        public string Item = "";
        public int Quantity;
        public string Bonus = "";
        public readonly List<string> Missing = new List<string>();
    }
}
