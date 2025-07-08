using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UI_Chat_App.Converters
{
    public class PriorityToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Nếu Tag là int priority và > 0 thì hiện thumbtack
            if (value is int priority && priority > 0)
                return Visibility.Visible;

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
