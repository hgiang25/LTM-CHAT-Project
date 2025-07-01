using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows;

namespace UI_Chat_App.Converters
{
    public class ImageUrlConverter : IValueConverter
    {
        // Cache để tránh tải lại ảnh nhiều lần
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _imageCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    // Chuẩn hóa URL
                    url = NormalizeUrl(url);

                    // Kiểm tra cache trước
                    if (_imageCache.TryGetValue(url, out var cachedImage))
                    {
                        Console.WriteLine($"Using cached image for: {url}");
                        return cachedImage;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();

                    // Xử lý URL local
                    if (IsLocalPath(url))
                    {
                        LoadLocalImage(url, bitmap);
                    }
                    // Xử lý URL remote
                    else
                    {
                        LoadRemoteImage(url, bitmap);
                    }

                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();

                    // Freeze để sử dụng đa luồng
                    if (bitmap.CanFreeze)
                    {
                        bitmap.Freeze();
                    }

                    // Lưu vào cache
                    _imageCache[url] = bitmap;

                    Console.WriteLine($"Successfully loaded and cached image: {url}");
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load image from {url}: {ex.Message}");
                    return GetFallbackImage();
                }
            }

            Console.WriteLine("URL is null or empty, using fallback image");
            return GetFallbackImage();
        }

        private string NormalizeUrl(string url)
        {
            // Chuẩn hóa đường dẫn local
            url = url.Replace("icons/", "Icons/");
            url = url.Replace("\\", "/");

            return url;
        }

        private bool IsLocalPath(string url)
        {
            return url.StartsWith("Icons/", StringComparison.OrdinalIgnoreCase) ||
                   Path.IsPathRooted(url);
        }

        private void LoadLocalImage(string url, BitmapImage bitmap)
        {
            if (url.StartsWith("Icons/", StringComparison.OrdinalIgnoreCase))
            {
                // Xử lý resource embedded
                var resourcePath = $"pack://application:,,,/{url}";
                Console.WriteLine($"Loading embedded resource: {resourcePath}");
                bitmap.UriSource = new Uri(resourcePath, UriKind.Absolute);
            }
            else if (File.Exists(url))
            {
                // Xử lý file local
                Console.WriteLine($"Loading local file: {url}");
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
            }
            else
            {
                throw new FileNotFoundException($"Local image file not found: {url}");
            }
        }

        private void LoadRemoteImage(string url, BitmapImage bitmap)
        {
            Console.WriteLine($"Loading remote image: {url}");
            bitmap.UriSource = new Uri(url, UriKind.Absolute);

            // Thiết lập timeout và retry
            bitmap.DownloadCompleted += (s, e) =>
                Console.WriteLine($"Download completed: {url}");

            bitmap.DownloadFailed += (s, e) =>
                Console.WriteLine($"Download failed: {url}, Error: {e.ErrorException.Message}");
        }

        private BitmapImage GetFallbackImage()
        {
            const string fallback = "pack://application:,,,/Icons/user.png";

            if (_imageCache.TryGetValue(fallback, out var cached))
                return cached;

            try
            {
                var fallbackBitmap = new BitmapImage();
                fallbackBitmap.BeginInit();
                fallbackBitmap.UriSource = new Uri(fallback, UriKind.Absolute);
                fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                fallbackBitmap.EndInit();
                fallbackBitmap.Freeze();

                _imageCache[fallback] = fallbackBitmap;

                Console.WriteLine("Successfully loaded fallback image");
                return fallbackBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load fallback image: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        // Hàm clear cache 
        public static void ClearCache()
        {
            _imageCache.Clear();
            Console.WriteLine("Image cache cleared");
        }
    }
}