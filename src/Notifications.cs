using System;
using System.Collections.Generic;
using System.Windows.Threading;
#if NOTIF
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
#endif

namespace DofuxyaManager
{
    // Suivi du tour par les notifications Windows : Dofus Retro envoie une notification quand un
    // personnage doit agir. L'outil lit la liste des notifications de Windows (pas le jeu) et
    // signale chaque nouvelle notification. Compile seulement si le poste fournit les
    // composants Windows necessaires (voir build.bat).
    internal class TurnWatcher
    {
        // Recoit le nom de l'application emettrice et les textes de la notification.
        public Action<string, List<string>> Received;
        public Action StateChanged;
        public string State = "arrêté";

#if NOTIF
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private UserNotificationListener listener;
        private Windows.Foundation.IAsyncOperation<UserNotificationListenerAccessStatus> accessOperation;
        private Windows.Foundation.IAsyncOperation<IReadOnlyList<UserNotification>> listOperation;
        private HashSet<uint> seen = new HashSet<uint>();
        private bool primed;
        private bool running;

        public static bool Available
        {
            get { return true; }
        }

        public TurnWatcher()
        {
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += delegate { Poll(); };
        }

        public void Start()
        {
            if (running) return;
            running = true;
            SetState("demande d'accès aux notifications…");
            try
            {
                listener = UserNotificationListener.Current;
                listOperation = null;
                accessOperation = listener.RequestAccessAsync();
                timer.Start();
            }
            catch (Exception ex)
            {
                running = false;
                SetState("indisponible sur ce poste : " + ex.Message);
            }
        }

        public void Stop()
        {
            running = false;
            timer.Stop();
            accessOperation = null;
            listOperation = null;
            SetState("arrêté");
        }

        // Les demandes a Windows sont asynchrones : on regarde a chaque passage si elles ont abouti.
        private void Poll()
        {
            if (!running || listener == null) return;
            try
            {
                if (accessOperation != null)
                {
                    Windows.Foundation.AsyncStatus accessStatus = accessOperation.Status;
                    if (accessStatus == Windows.Foundation.AsyncStatus.Started) return;
                    Windows.Foundation.IAsyncOperation<UserNotificationListenerAccessStatus> finished = accessOperation;
                    accessOperation = null;
                    if (accessStatus != Windows.Foundation.AsyncStatus.Completed
                        || finished.GetResults() != UserNotificationListenerAccessStatus.Allowed)
                    {
                        running = false;
                        timer.Stop();
                        SetState("accès aux notifications refusé par Windows (Paramètres > Confidentialité et "
                            + "sécurité > Notifications : autoriser les applications à y accéder)");
                        return;
                    }
                    primed = false;
                    seen = new HashSet<uint>();
                    SetState("actif");
                    return;
                }

                if (listOperation == null)
                {
                    listOperation = listener.GetNotificationsAsync(NotificationKinds.Toast);
                    return;
                }
                Windows.Foundation.AsyncStatus listStatus = listOperation.Status;
                if (listStatus == Windows.Foundation.AsyncStatus.Started) return;
                Windows.Foundation.IAsyncOperation<IReadOnlyList<UserNotification>> done = listOperation;
                listOperation = null;
                if (listStatus != Windows.Foundation.AsyncStatus.Completed) return;

                IReadOnlyList<UserNotification> list = done.GetResults();
                HashSet<uint> current = new HashSet<uint>();
                foreach (UserNotification notification in list)
                {
                    current.Add(notification.Id);
                    // Les notifications deja presentes au demarrage ne comptent pas.
                    if (!primed || seen.Contains(notification.Id)) continue;

                    string app = "";
                    try
                    {
                        app = notification.AppInfo.DisplayInfo.DisplayName;
                    }
                    catch (Exception)
                    {
                    }
                    List<string> texts = new List<string>();
                    try
                    {
                        NotificationBinding binding =
                            notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                        if (binding != null)
                        {
                            foreach (AdaptiveNotificationText text in binding.GetTextElements())
                            {
                                texts.Add(text.Text);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    Action<string, List<string>> received = Received;
                    if (received != null) received(app, texts);
                }
                seen = current;
                primed = true;
            }
            catch (Exception ex)
            {
                listOperation = null;
                SetState("erreur de lecture des notifications : " + ex.Message);
            }
        }
#else
        public static bool Available
        {
            get { return false; }
        }

        public void Start()
        {
            SetState("non disponible : cette version a été compilée sans la lecture des notifications "
                + "(voir build-suivi.log à côté de build.bat)");
        }

        public void Stop()
        {
            SetState("arrêté");
        }
#endif

        private void SetState(string state)
        {
            State = state;
            Action changed = StateChanged;
            if (changed != null) changed();
        }
    }
}
