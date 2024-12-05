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
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string cityName)
            {
                // Тільки ці міста доступні
                if (cityName == "Київ" || cityName == "Львів" || cityName == "Херсон")
                {
                    return false; // Місто доступне
                }
                return true; // Місто недоступне
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
