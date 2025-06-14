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

                    // Chuẩn hóa URL (chuyển về chữ hoa/thường nhất quán)
                    url = url.Replace("icons/", "Icons/");

                    // Kiểm tra nếu là đường dẫn cục bộ
                    if (url.StartsWith("Icons/"))
                    {
                        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        if (string.IsNullOrEmpty(baseDirectory))
                        {
                            Console.WriteLine("Base directory is null, falling back to default resource.");
                            bitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                        }
                        else
                        {
                            string fullPath = Path.Combine(baseDirectory, url);
                            Console.WriteLine($"Loading local image from path: {fullPath}");
                            if (File.Exists(fullPath))
                            {
                                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                            }
                            else
                            {
                                Console.WriteLine($"Local image file not found: {fullPath}, falling back to default resource.");
                                bitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                            }
                        }
                    }
                    else
                    {
                        // Xử lý URL từ S3
                        try
                        {
                            Console.WriteLine($"Attempting to load image from S3 URL: {url}");
                            using (var client = new System.Net.WebClient())
                            {
                                client.DownloadData(url);
                            }
                            bitmap.UriSource = new Uri(url, UriKind.Absolute);
                        }
                        catch (Exception s3Ex)
                        {
                            Console.WriteLine($"Failed to access S3 URL {url}: {s3Ex.Message}, falling back to default resource.");
                            bitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                        }
                    }

                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    Console.WriteLine($"Successfully loaded image from {url}");
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load image from {url}: {ex.Message}, StackTrace: {ex.StackTrace}");
                    try
                    {
                        var fallbackBitmap = new BitmapImage();
                        fallbackBitmap.BeginInit();
                        fallbackBitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                        fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        fallbackBitmap.EndInit();
                        Console.WriteLine("Successfully loaded fallback resource.");
                        return fallbackBitmap;
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"Failed to load fallback resource: {fallbackEx.Message}, StackTrace: {fallbackEx.StackTrace}");
                        return null;
                    }
                }
            }

            Console.WriteLine("URL is null or empty, using default avatar from resources.");
            try
            {
                var defaultBitmap = new BitmapImage();
                defaultBitmap.BeginInit();
                defaultBitmap.UriSource = new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute);
                defaultBitmap.CacheOption = BitmapCacheOption.OnLoad;
                defaultBitmap.EndInit();
                Console.WriteLine("Successfully loaded default resource.");
                return defaultBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load default resource: {ex.Message}, StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}