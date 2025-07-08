using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UI_Chat_App.Converters
{
    public class BlockToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool blocked && blocked) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

