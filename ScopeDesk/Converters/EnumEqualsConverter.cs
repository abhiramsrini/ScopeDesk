using System;
using System.Globalization;
using System.Windows.Data;

namespace ScopeDesk.Converters
{
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return false;
            }

            var enumString = parameter.ToString();
            if (enumString == null)
            {
                return false;
            }

            return string.Equals(value.ToString(), enumString, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null)
            {
                return Binding.DoNothing;
            }

            var enumString = parameter.ToString();
            if (enumString == null)
            {
                return Binding.DoNothing;
            }

            return Enum.Parse(targetType, enumString);
        }
    }
}
