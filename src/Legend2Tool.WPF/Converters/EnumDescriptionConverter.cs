using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Legend2Tool.WPF.Converters
{
    public class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return string.Empty; // Return an empty string instead of null to avoid CS8603.

            // Ensure value.ToString() is not null before calling GetField
            string? fieldName = value.ToString();
            if (string.IsNullOrEmpty(fieldName)) return string.Empty; // Handle potential null or empty case for CS8603.

            FieldInfo? field = value.GetType().GetField(fieldName);
            if (field is null) return string.Empty; // Handle potential null case for CS8603.

            var attr = field.GetCustomAttributes<DescriptionAttribute>();
            return attr?.FirstOrDefault()?.Description ?? string.Empty; // Ensure no null reference is returned.
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
