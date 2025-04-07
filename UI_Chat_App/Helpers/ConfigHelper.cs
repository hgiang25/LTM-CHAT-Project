using System;
using DotNetEnv;

namespace ChatApp.Helpers
{
    public static class ConfigHelper
    {
        public static string GetFirebaseApiKey()
        {
            try
            {
                string apiKey = DotNetEnv.Env.GetString("FIREBASE_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new Exception("FirebaseApiKey is missing or empty in environment variables.");
                }
                return apiKey;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to read Firebase API key from config: {ex.Message}", "Configuration Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                throw;
            }
        }

        public static string GetFirebaseDatabaseUrl()
        {
            try
            {
                string databaseUrl = DotNetEnv.Env.GetString("FIREBASE_DATABASE_URL");
                if (string.IsNullOrEmpty(databaseUrl))
                {
                    throw new Exception("FirebaseDatabaseURL is missing or empty in environment variables.");
                }
                return databaseUrl;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to read Firebase Database URL from config: {ex.Message}", "Configuration Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                throw;
            }
        }
    }
}