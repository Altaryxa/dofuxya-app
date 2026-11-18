using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DofuxyaManager
{
    // Mini-barre flottante : les personnages dans l'ordre du tour, toujours visibles.
    // Un clic sur un personnage amene sa fenetre au premier plan. Elle ne prend jamais
    // le focus, pour ne pas sortir du jeu.
    internal class MiniBar : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly StackPanel panel = new StackPanel();
        private readonly StackPanel content = new StackPanel();
        private readonly StackPanel grip = new StackPanel();
        private readonly Action<GameWindow> pick;
        private readonly Action moved;
        private readonly Action toggleOrientation;
        private bool vertical;

        public MiniBar(Action<GameWindow> pick, Action moved, Action toggleOrientation)
        {
            this.pick = pick;
            this.moved = moved;
            this.toggleOrientation = toggleOrientation;

            Title = "Dofuxya - mini-barre";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");

            Border frame = new Border();
            frame.Background = Ui.Paint(Ui.ColDeep);
            frame.BorderBrush = Ui.Paint(Ui.ColLine);
            frame.BorderThickness = new Thickness(1);
            frame.CornerRadius = new CornerRadius(12);
            frame.Padding = new Thickness(6);
            frame.Cursor = Cursors.SizeAll;
            frame.ToolTip = "Glisser pour déplacer · double-clic pour passer en vertical ou en horizontal.";
            frame.MouseLeftButtonDown += OnDrag;

            // Poignee de deplacement.
            for (int i = 0; i < 3; i++)
            {
                Border dot = new Border();
                dot.Width = 4;
                dot.Height = 4;
                dot.CornerRadius = new CornerRadius(2);
                dot.Background = Ui.Paint(Ui.ColMuted);
                dot.Margin = new Thickness(2);
                grip.Children.Add(dot);
            }
            grip.HorizontalAlignment = HorizontalAlignment.Center;
            grip.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(grip);
            content.Children.Add(panel);
            SetVertical(false);
            frame.Child = content;
            Content = frame;

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(this).Handle;
                    int style = Native.GetWindowLong(handle, GWL_EXSTYLE);
                    Native.SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                }
                catch (Exception)
                {
                }
            };
        }

        // Horizontale : personnages cote a cote. Verticale : personnages empiles.
        public void SetVertical(bool value)
        {
            vertical = value;
            content.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            grip.Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical;
            grip.Margin = vertical ? new Thickness(0, 4, 0, 6) : new Thickness(4, 0, 6, 0);
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (toggleOrientation != null) toggleOrientation();
                return;
            }
            try
            {
                DragMove();
            }
            catch (Exception)
            {
            }
            if (moved != null) moved();
        }

        // Redessine la barre : un bouton par personnage, celui de la fenetre active en vert.
        public void Update(List<GameWindow> ordered, IntPtr active, IconLibrary icons)
        {
            panel.Children.Clear();
            foreach (GameWindow window in ordered)
            {
                GameWindow target = window;
                bool isActive = window.Handle == active;

                Border cell = new Border();
                cell.CornerRadius = new CornerRadius(8);
                cell.BorderThickness = new Thickness(1);
                cell.BorderBrush = isActive ? Ui.Paint(Ui.ColAccent) : Brushes.Transparent;
                cell.Background = isActive ? Ui.Paint(Ui.ColRowActive) : Brushes.Transparent;
                cell.Margin = vertical ? new Thickness(0, 2, 0, 2) : new Thickness(2, 0, 2, 0);

                Button button = Ui.MakeFlat(null, delegate { if (pick != null) pick(target); });
                button.Width = 44;
                button.Height = 44;
                button.Padding = new Thickness(0);
                button.Focusable = false;
                button.ToolTip = window.Name;

                ImageSource preview = icons.GetPreview(window.IconKey);
                if (preview != null)
                {
                    Image image = new Image();
                    image.Source = preview;
                    image.Width = 34;
                    image.Height = 34;
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    button.Content = image;
                }
                else
                {
                    string initials = window.Name.Substring(0, Math.Min(2, window.Name.Length));
                    TextBlock text = Ui.Text(initials, 14, true, isActive ? Ui.ColAccent : Ui.ColMuted);
                    text.HorizontalAlignment = HorizontalAlignment.Center;
                    text.VerticalAlignment = VerticalAlignment.Center;
                    button.Content = text;
                }
                cell.Child = button;
                panel.Children.Add(cell);
            }
            if (ordered.Count == 0)
            {
                TextBlock empty = Ui.Text("Aucun personnage", 12, false, Ui.ColMuted);
                empty.VerticalAlignment = VerticalAlignment.Center;
                empty.Margin = new Thickness(4, 10, 10, 10);
                panel.Children.Add(empty);
            }
        }
    }
}
