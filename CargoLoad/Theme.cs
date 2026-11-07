using System.Windows;
using System.Windows.Media;

namespace CargoLoad
{
    /// <summary>
    /// Управление темой интерфейса. Все цвета хранятся в ресурсах приложения,
    /// XAML и код берут их через DynamicResource / GetBrush, поэтому смена темы
    /// применяется без перезапуска.
    /// </summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; } = true;

        private static readonly (string Key, string Dark, string Light)[] Palette =
        {
            ("Bg",        "#121419", "#F4F6F9"),
            ("Panel",     "#1B1E25", "#FFFFFF"),
            ("PanelAlt",  "#20242D", "#F7F9FC"),
            ("Header",    "#161920", "#EDF1F7"),
            ("Border",    "#2A2F3A", "#D6DCE6"),
            ("Text",      "#E9ECF2", "#1B1F27"),
            ("Muted",     "#8D96A6", "#5E6775"),
            ("InputBg",   "#12151B", "#FFFFFF"),
            ("InputFg",   "#E9ECF2", "#1B1F27"),
            ("Accent",    "#3B6EF6", "#2C5CE0"),
            ("AccentText","#FFFFFF", "#FFFFFF"),
            ("Danger",    "#E8A0A0", "#B42318"),
            ("Canvas",    "#0E1014", "#E9EDF3"),
            ("CanvasBox", "#14171C", "#FFFFFF"),
            ("Truck",     "#8D96A6", "#5E6775"),
        };

        public static void Apply(bool dark)
        {
            IsDark = dark;
            var res = Application.Current.Resources;
            foreach (var (key, darkHex, lightHex) in Palette)
                res[key] = MakeBrush(dark ? darkHex : lightHex);

            // Обводка/заливка ящиков второго яруса
            res["TierInk"] = MakeBrush(dark ? "#0E1014" : "#FFFFFF");
        }

        public static Brush GetBrush(string key)
        {
            return Application.Current.Resources[key] as Brush
                   ?? new SolidColorBrush(Colors.Gray);
        }

        public static Color GetColor(string key)
        {
            return GetBrush(key) is SolidColorBrush sb ? sb.Color : Colors.Gray;
        }

        private static SolidColorBrush MakeBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}