using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Legend2Tool.WPF.Converters
{
    public sealed class MonsterCountToRedBrushConverter : IMultiValueConverter
    {
        private static readonly Color LightRed = Color.FromRgb(229, 57, 53);
        private static readonly Color DarkRed = Color.FromRgb(127, 0, 0);

        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (
                values.Length < 2
                || values[0] is not int monsterCount
                || values[1] is not int maxMonstersPerMap
            )
            {
                return Brushes.Black;
            }

            double intensity = GetIntensity(monsterCount, maxMonstersPerMap);
            if (monsterCount <= maxMonstersPerMap)
            {
                return Brushes.Black;
            }

            Color color = Color.FromRgb(
                Interpolate(LightRed.R, DarkRed.R, intensity),
                Interpolate(LightRed.G, DarkRed.G, intensity),
                Interpolate(LightRed.B, DarkRed.B, intensity)
            );
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotSupportedException();

        private static double GetIntensity(int monsterCount, int maxMonstersPerMap)
        {
            if (maxMonstersPerMap <= 0)
            {
                return monsterCount > maxMonstersPerMap ? 1 : 0;
            }

            return Math.Clamp(
                (monsterCount - maxMonstersPerMap) / (double)maxMonstersPerMap,
                0,
                1
            );
        }

        private static byte Interpolate(byte start, byte end, double amount) =>
            (byte)Math.Round(start + (end - start) * amount);
    }
}
