using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace DofuxyaManager
{
    // Roue des persos : touche maintenue (Alt par defaut) + clic droit dans une fenetre Dofus l'ouvre ;
    // un clic gauche sur une part passe sur la fenetre du perso.
    internal partial class MainWindow
    {
        private PersoWheel wheel;

        // Les deux fonctions suivantes sont appelees par l'ecoute de la souris a chaque clic : elles
        // doivent repondre tout de suite. Elles renvoient true quand le clic sert a la roue (il
        // n'arrive alors pas au jeu).

        // Roue ouverte : un clic gauche dans la roue lui revient (choix d'un perso) ; ailleurs, il la ferme.
        private bool OnLeftButton(int x, int y)
        {
            if (wheel == null || !wheel.IsOpen) return false;
            if (wheel.Contains(x, y)) return false;
            Dispatcher.BeginInvoke(new Action(CloseWheel));
            return true;
        }

        // Touche maintenue + clic droit dans Dofus : ouvre la roue (ou la ferme si elle est ouverte).
        private bool OnRightButton(int x, int y)
        {
            if (wheel != null && wheel.IsOpen)
            {
                Dispatcher.BeginInvoke(new Action(CloseWheel));
                return true;
            }
            if (settings.WheelKey.IsEmpty || captureButton != null) return false;
            if (!MouseHook.IsKeyDown(settings.WheelKey.Vk)) return false;
            if (FindWindow(Native.GetForegroundWindow()) == null) return false;
            if (NamedWindows().Count < 2) return false;
            Dispatcher.BeginInvoke(new Action(delegate { OpenWheel(x, y); }));
            return true;
        }

        private void OpenWheel(int x, int y)
        {
            List<GameWindow> persos = NamedWindows();
            if (persos.Count < 2) return;
            try
            {
                if (wheel == null) wheel = new PersoWheel(GoTo, WheelStillValid, LoadLogo());
                wheel.Open(persos, icons, x, y, DpiScale(), settings.WheelKey.Vk);
            }
            catch (Exception ex)
            {
                lastMessage = "Roue des persos : " + ex.Message;
                UpdateStatus();
            }
        }

        private void CloseWheel()
        {
            if (wheel != null && wheel.IsOpen) wheel.CloseWheel();
        }

        // La roue reste ouverte tant qu'une fenetre Dofus est au premier plan.
        private bool WheelStillValid()
        {
            return FindWindow(Native.GetForegroundWindow()) != null;
        }

        // Pixels de l'ecran par unite WPF (1,25 quand Windows agrandit l'affichage a 125 %).
        private double DpiScale()
        {
            HwndSource source = handle != IntPtr.Zero ? HwndSource.FromHwnd(handle) : null;
            if (source == null || source.CompositionTarget == null) return 1;
            return source.CompositionTarget.TransformToDevice.M11;
        }
    }
}
