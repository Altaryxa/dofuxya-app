using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DofuxyaManager
{
    // Habillage : couleurs et controles sombres, construits en code.
    internal static class Ui
    {
        public const string ColGround = "#2a2d33";
        public const string ColDeep = "#1d2025";
        public const string ColRow = "#33373e";
        public const string ColRowHover = "#3d424a";
        public const string ColRowActive = "#353f29";
        public const string ColLine = "#454a53";
        public const string ColField = "#1d2025";
        public const string ColFieldHover = "#272b31";
        public const string ColText = "#f0f2ee";
        public const string ColMuted = "#b4b9c0";
        public const string ColAccent = "#b6e23c";
        public const string ColAccentHover = "#c8ee5c";
        public const string ColAccentInk = "#17210a";
        public const string ColWheelLine = "#16181b";

        private static ControlTemplate normalTemplate;
        private static ControlTemplate primaryTemplate;
        private static ControlTemplate fieldTemplate;
        private static ControlTemplate checkTemplate;
        private static ControlTemplate flatTemplate;

        // Pinceaux figes, crees une fois par couleur : l'interface les redemande a chaque mise a jour.
        private static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();

        public static SolidColorBrush Paint(string hex)
        {
            SolidColorBrush brush;
            if (brushes.TryGetValue(hex, out brush)) return brush;
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            brushes[hex] = brush;
            return brush;
        }

        public static TextBlock Text(string text, double size, bool bold, string color)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontSize = size;
            block.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            block.Foreground = Paint(color);
            return block;
        }

        // Bouton d'action. "primary" = bouton vert principal.
        public static Button MakeButton(string text, bool primary, Action onClick)
        {
            if (normalTemplate == null)
            {
                normalTemplate = ButtonTemplate(ColRow, ColRowHover, ColDeep, ColLine, ColLine);
                primaryTemplate = ButtonTemplate(ColAccent, ColAccentHover, ColAccent, ColAccent, ColAccentHover);
            }
            Button button = new Button();
            button.Content = text;
            button.Template = primary ? primaryTemplate : normalTemplate;
            button.Foreground = Paint(primary ? ColAccentInk : ColText);
            button.FontSize = 13;
            button.FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold;
            button.Padding = new Thickness(14, 8, 14, 8);
            button.Cursor = Cursors.Hand;
            button.FocusVisualStyle = null;
            button.VerticalAlignment = VerticalAlignment.Center;
            if (onClick != null) button.Click += delegate { onClick(); };
            return button;
        }

        // Champ cliquable sombre (raccourci, choix d'icone).
        public static Button MakeField(string text, Action onClick)
        {
            if (fieldTemplate == null)
            {
                fieldTemplate = ButtonTemplate(ColField, ColFieldHover, ColField, ColLine, ColAccent);
            }
            Button button = new Button();
            button.Content = text;
            button.Template = fieldTemplate;
            button.Foreground = Paint(ColText);
            button.FontSize = 13;
            button.FontWeight = FontWeights.SemiBold;
            button.Padding = new Thickness(8, 8, 8, 8);
            button.Cursor = Cursors.Hand;
            button.FocusVisualStyle = null;
            button.VerticalAlignment = VerticalAlignment.Center;
            if (onClick != null) button.Click += delegate { onClick(); };
            return button;
        }

        // Bouton sans cadre (onglets, boutons de fenetre, icone de classe).
        public static Button MakeFlat(string text, Action onClick)
        {
            if (flatTemplate == null)
            {
                flatTemplate = ButtonTemplate("#00000000", ColFieldHover, ColDeep, "#00000000", "#00000000");
            }
            Button button = new Button();
            if (text != null) button.Content = text;
            button.Template = flatTemplate;
            button.Foreground = Paint(ColText);
            button.FontSize = 13;
            button.FontWeight = FontWeights.SemiBold;
            button.Padding = new Thickness(8, 6, 8, 6);
            button.Cursor = Cursors.Hand;
            button.FocusVisualStyle = null;
            button.VerticalAlignment = VerticalAlignment.Center;
            if (onClick != null) button.Click += delegate { onClick(); };
            return button;
        }

        // Case a cocher sombre, avec libelle facultatif.
        public static ToggleButton MakeCheck(string label, bool value, Action<bool> changed)
        {
            if (checkTemplate == null) checkTemplate = CheckTemplate();
            ToggleButton toggle = new ToggleButton();
            toggle.Template = checkTemplate;
            toggle.Content = label;
            toggle.IsChecked = value;
            toggle.Foreground = Paint(ColText);
            toggle.FontSize = 13;
            toggle.Cursor = Cursors.Hand;
            toggle.FocusVisualStyle = null;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            if (changed != null)
            {
                toggle.Click += delegate { changed(toggle.IsChecked == true); };
            }
            return toggle;
        }

        private static ControlTemplate ButtonTemplate(string normal, string hover, string pressed,
            string border, string hoverBorder)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));

            FrameworkElementFactory frame = new FrameworkElementFactory(typeof(Border), "frame");
            frame.SetValue(Border.BackgroundProperty, Paint(normal));
            frame.SetValue(Border.BorderBrushProperty, Paint(border));
            frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            frame.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(content);
            template.VisualTree = frame;

            Trigger over = new Trigger();
            over.Property = UIElement.IsMouseOverProperty;
            over.Value = true;
            over.Setters.Add(new Setter(Border.BackgroundProperty, Paint(hover), "frame"));
            over.Setters.Add(new Setter(Border.BorderBrushProperty, Paint(hoverBorder), "frame"));
            template.Triggers.Add(over);

            Trigger down = new Trigger();
            down.Property = ButtonBase.IsPressedProperty;
            down.Value = true;
            down.Setters.Add(new Setter(Border.BackgroundProperty, Paint(pressed), "frame"));
            template.Triggers.Add(down);

            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "frame"));
            template.Triggers.Add(disabled);

            return template;
        }

        private static ControlTemplate CheckTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));

            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel), "panel");
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Border), "box");
            box.SetValue(FrameworkElement.WidthProperty, 22.0);
            box.SetValue(FrameworkElement.HeightProperty, 22.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            box.SetValue(Border.BackgroundProperty, Paint(ColField));
            box.SetValue(Border.BorderBrushProperty, Paint("#8b919a"));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "check");
            check.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 3.5,9 L 7.5,13 L 14.5,5"));
            check.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Paint(ColAccentInk));
            check.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.6);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(check);
            panel.AppendChild(box);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(content);
            template.VisualTree = panel;

            Trigger isChecked = new Trigger();
            isChecked.Property = ToggleButton.IsCheckedProperty;
            isChecked.Value = true;
            isChecked.Setters.Add(new Setter(Border.BackgroundProperty, Paint(ColAccent), "box"));
            isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, Paint(ColAccent), "box"));
            isChecked.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "check"));
            template.Triggers.Add(isChecked);

            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "panel"));
            template.Triggers.Add(disabled);

            return template;
        }
    }
}
