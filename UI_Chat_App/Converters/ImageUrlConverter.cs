using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.IO;

namespace UI_Chat_App.Converters
{
    public class ImageUrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();

                    url = url.Replace("icons/", "Icons/");

                    if (url.StartsWith("Icons/"))
                    {
                        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        if (string.IsNullOrEmpty(baseDirectory))
                        {
                            bitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                        }
                        else
                        {
                            string fullPath = Path.Combine(baseDirectory, url);
                            if (File.Exists(fullPath))
                            {
                                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                            }
                            else
                            {
                                bitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                            }
                        }
                    }
                    else
                    {
                        bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    }

                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load image from {url}: {ex.Message}");
                    var fallbackBitmap = new BitmapImage();
                    fallbackBitmap.BeginInit();
                    fallbackBitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                    fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    fallbackBitmap.EndInit();
                    return fallbackBitmap;
                }
            }

            var defaultBitmap = new BitmapImage();
            defaultBitmap.BeginInit();
            defaultBitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
            defaultBitmap.CacheOption = BitmapCacheOption.OnLoad;
            defaultBitmap.EndInit();
            return defaultBitmap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}