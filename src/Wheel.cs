using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DofuxyaManager
{
    // Roue des persos : s'ouvre au curseur (touche maintenue, Maj par defaut, + clic droit dans Dofus). Une part par perso,
    // dans l'ordre du tour ; un clic sur une part amene sa fenetre au premier plan.
    // La roue ne prend jamais le focus : le jeu reste la fenetre active jusqu'au choix.
    internal class PersoWheel : Window
    {
        public const double Size = 340;
        private const double Center = Size / 2;
        private const double Outer = 169;
        private const double Inner = 32;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly Canvas canvas = new Canvas();
        private readonly List<Path> parts = new List<Path>();
        private readonly List<TextBlock> names = new List<TextBlock>();
        private readonly DispatcherTimer watch = new DispatcherTimer();
        private readonly Action<GameWindow> pick;
        private readonly Func<bool> stillValid;
        private readonly ImageSource logo;
        private List<GameWindow> persos = new List<GameWindow>();
        private int hover = -1;
        private uint holdKey;
        // Centre et rayon en pixels de l'ecran, pour savoir si un clic tombe dans la roue.
        private double centerX;
        private double centerY;
        private double radiusPx;

        public bool IsOpen { get; private set; }

        public PersoWheel(Action<GameWindow> pick, Func<bool> stillValid, ImageSource logo)
        {
            this.pick = pick;
            this.stillValid = stillValid;
            this.logo = logo;

            Title = "Dofuxya - roue des persos";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            ResizeMode = ResizeMode.NoResize;
            Width = Size;
            Height = Size;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");

            canvas.Width = Size;
            canvas.Height = Size;
            canvas.Background = Brushes.Transparent;
            canvas.MouseMove += delegate(object sender, MouseEventArgs e) { SetHover(PartAt(e.GetPosition(canvas))); };
            canvas.MouseLeave += delegate { SetHover(-1); };
            canvas.MouseLeftButtonDown += OnClick;
            Content = canvas;

            // Fermeture : touche relachee, Echap, ou le jeu n'est plus au premier plan.
            watch.Interval = TimeSpan.FromMilliseconds(40);
            watch.Tick += delegate
            {
                if (!MouseHook.IsKeyDown(holdKey) || MouseHook.IsKeyDown(MouseHook.VK_ESCAPE)
                    || (stillValid != null && !stillValid()))
                {
                    CloseWheel();
                }
            };

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

        // Ouvre la roue centree sur un point de l'ecran (pixels), avec "scale" pixels par unite WPF.
        // "key" est la touche maintenue : la relacher ferme la roue.
        public void Open(List<GameWindow> list, IconLibrary icons, int x, int y, double scale, uint key)
        {
            persos = list;
            holdKey = key;
            Build(icons);

            radiusPx = Center * scale;
            Native.RECT screen = Native.MonitorBoundsAt(x, y);
            centerX = Math.Max(screen.Left + radiusPx, Math.Min(screen.Right - radiusPx, x));
            centerY = Math.Max(screen.Top + radiusPx, Math.Min(screen.Bottom - radiusPx, y));
            Left = centerX / scale - Center;
            Top = centerY / scale - Center;

            if (!IsVisible) Show();
            IsOpen = true;
            watch.Start();
        }

        public void CloseWheel()
        {
            watch.Stop();
            IsOpen = false;
            SetHover(-1);
            Hide();
        }

        // Vrai si le point de l'ecran (pixels) est dans le disque de la roue.
        public bool Contains(int x, int y)
        {
            double dx = x - centerX;
            double dy = y - centerY;
            return dx * dx + dy * dy <= radiusPx * radiusPx;
        }

        private void Build(IconLibrary icons)
        {
            canvas.Children.Clear();
            parts.Clear();
            names.Clear();
            hover = -1;
            int count = persos.Count;
            double span = 360.0 / count;

            for (int i = 0; i < count; i++)
            {
                double from = -90 - span / 2 + i * span;
                Path part = new Path();
                part.Data = Slice(from, from + span);
                part.Fill = Ui.Paint(Ui.ColRow);
                part.Stroke = Ui.Paint(Ui.ColWheelLine);
                part.StrokeThickness = 2;
                part.StrokeLineJoin = PenLineJoin.Round;
                part.IsHitTestVisible = false;
                canvas.Children.Add(part);
                parts.Add(part);
            }

            Ellipse ring = new Ellipse();
            ring.Width = Size - 2;
            ring.Height = Size - 2;
            ring.Stroke = Ui.Paint(Ui.ColWheelLine);
            ring.StrokeThickness = 3;
            ring.IsHitTestVisible = false;
            Canvas.SetLeft(ring, 1);
            Canvas.SetTop(ring, 1);
            canvas.Children.Add(ring);

            for (int i = 0; i < count; i++)
            {
                GameWindow window = persos[i];
                double angle = (-90 + i * span) * Math.PI / 180;
                double radius = Inner + (Outer - Inner) * 0.56;
                double lx = Center + radius * Math.Cos(angle);
                double ly = Center + radius * Math.Sin(angle);

                StackPanel label = new StackPanel();
                label.Width = 88;
                label.Height = 54;
                label.IsHitTestVisible = false;
                label.VerticalAlignment = VerticalAlignment.Center;

                ImageSource icon = icons.GetPreview(window.IconKey);
                if (icon != null)
                {
                    Image image = new Image();
                    image.Source = icon;
                    image.Width = 30;
                    image.Height = 30;
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    label.Children.Add(image);
                }
                else
                {
                    TextBlock initials = Ui.Text(window.Name.Substring(0, Math.Min(2, window.Name.Length)),
                        15, true, Ui.ColMuted);
                    initials.Height = 30;
                    initials.HorizontalAlignment = HorizontalAlignment.Center;
                    label.Children.Add(initials);
                }
                TextBlock name = Ui.Text(window.Name, 12, true, Ui.ColText);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.MaxWidth = 88;
                name.Margin = new Thickness(0, 3, 0, 0);
                label.Children.Add(name);
                names.Add(name);

                Canvas.SetLeft(label, lx - 44);
                Canvas.SetTop(label, ly - 27);
                canvas.Children.Add(label);
            }

            Ellipse hub = new Ellipse();
            hub.Width = Inner * 2 + 4;
            hub.Height = Inner * 2 + 4;
            hub.Fill = Ui.Paint(Ui.ColDeep);
            hub.Stroke = Ui.Paint(Ui.ColWheelLine);
            hub.StrokeThickness = 3;
            hub.IsHitTestVisible = false;
            Canvas.SetLeft(hub, Center - Inner - 2);
            Canvas.SetTop(hub, Center - Inner - 2);
            canvas.Children.Add(hub);

            if (logo != null)
            {
                Image mark = new Image();
                mark.Source = logo;
                mark.Width = 36;
                mark.Height = 36;
                mark.IsHitTestVisible = false;
                RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
                Canvas.SetLeft(mark, Center - 18);
                Canvas.SetTop(mark, Center - 18);
                canvas.Children.Add(mark);
            }
        }

        // Part d'anneau entre deux angles (degres, 0 = a droite, sens des aiguilles d'une montre).
        private static Geometry Slice(double from, double to)
        {
            bool large = to - from > 180;
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(PointAt(Outer, from), true, true);
                context.ArcTo(PointAt(Outer, to), new System.Windows.Size(Outer, Outer), 0, large,
                    SweepDirection.Clockwise, true, true);
                context.LineTo(PointAt(Inner, to), true, true);
                context.ArcTo(PointAt(Inner, from), new System.Windows.Size(Inner, Inner), 0, large,
                    SweepDirection.Counterclockwise, true, true);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Point PointAt(double radius, double degrees)
        {
            double angle = degrees * Math.PI / 180;
            return new Point(Center + radius * Math.Cos(angle), Center + radius * Math.Sin(angle));
        }

        // Numero de la part sous un point de la roue, ou -1 (centre, hors du disque).
        private int PartAt(Point point)
        {
            double dx = point.X - Center;
            double dy = point.Y - Center;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < Inner || distance > Outer || persos.Count == 0) return -1;
            double span = 360.0 / persos.Count;
            double degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
            double fromTop = ((degrees + 90 + span / 2) % 360 + 360) % 360;
            return Math.Min(persos.Count - 1, (int)(fromTop / span));
        }

        private void SetHover(int index)
        {
            if (index == hover) return;
            if (hover >= 0 && hover < parts.Count)
            {
                parts[hover].Fill = Ui.Paint(Ui.ColRow);
                names[hover].Foreground = Ui.Paint(Ui.ColText);
            }
            hover = index;
            if (hover >= 0 && hover < parts.Count)
            {
                parts[hover].Fill = Ui.Paint(Ui.ColAccent);
                names[hover].Foreground = Ui.Paint(Ui.ColAccentInk);
            }
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            int index = PartAt(e.GetPosition(canvas));
            GameWindow target = index >= 0 && index < persos.Count ? persos[index] : null;
            CloseWheel();
            if (target != null && pick != null) pick(target);
        }
    }
}
