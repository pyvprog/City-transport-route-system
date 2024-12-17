using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Kursova.Converters
{
    public class AvailabilityConverter : IValueConverter
    {
        private static readonly HashSet<string> AvailableCities = new() { "Київ", "Львів", "Херсон" };

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string cityName)
            {
                return !AvailableCities.Contains(cityName);
            }
            return true;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
