using System.Windows;
using ChatApp.Models;
using DotNetEnv;
using System.IO;
using System;

namespace UI_Chat_App
{
    public partial class App : Application
    {
        public static UserData CurrentUser { get; set; }
        public static string IdToken { get; set; }

        public App()
        {
            // Tải tệp .env khi ứng dụng khởi động
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath))
            {
                MessageBox.Show($"Tệp .env không tồn tại tại: {envPath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            DotNetEnv.Env.Load(envPath);

            // Kiểm tra xem FIREBASE_API_KEY có được tải không
            string apiKey = DotNetEnv.Env.GetString("FIREBASE_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Không tìm thấy FIREBASE_API_KEY trong tệp .env", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            CurrentUser = new UserData
            {
                Avatar = "Icons/user.png"
            };
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }
}