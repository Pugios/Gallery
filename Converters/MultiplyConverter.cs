using System.Globalization;

namespace app.Converters
{
    internal class MultiplyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d && parameter is string s &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double factor))
                return d * factor;
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
