using System;
using System.Linq;
using System.Globalization;
using System.Windows.Data;
using UI_Chat_App;

namespace UI_Chat_App.Converters // Đảm bảo namespace này khớp với khai báo trong XAML
{
    public class HasPendingRequestConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string userId && parameter is ChatWindow chatWindow)
            {
                return chatWindow.SentFriendRequests.Any(r => r.FriendRequest.ToUserId == userId && r.FriendRequest.Status == "pending");
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}