using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using ChatApp.Services;

namespace UI_Chat_App
{
    public partial class SignUp : Window
    {
        private readonly FirebaseAuthService _authService;

        public SignUp()
        {
            InitializeComponent();
            this.Opacity = 0;
            this.Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5));
                this.BeginAnimation(Window.OpacityProperty, fadeIn);

                var slide = new DoubleAnimation
                {
                    From = 50,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.5),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                (SignUpForm.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, slide);
            };


            _authService = new FirebaseAuthService();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox != null)
            {
                if (textBox.Name == "SignUpUsernameTextBox" && textBox.Text == "")
                {
                    SignUpUsernamePlaceholder.Visibility = Visibility.Collapsed;
                }
                else if (textBox.Name == "EmailTextBox" && textBox.Text == "")
                {
                    EmailPlaceholder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox != null)
            {
                if (textBox.Name == "SignUpUsernameTextBox" && textBox.Text == "")
                {
                    SignUpUsernamePlaceholder.Visibility = Visibility.Visible;
                }
                else if (textBox.Name == "EmailTextBox" && textBox.Text == "")
                {
                    EmailPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            if (passwordBox != null && passwordBox.Password == "")
            {
                if (passwordBox.Name == "NewPasswordBox")
                {
                    NewPasswordPlaceholder.Visibility = Visibility.Collapsed;
                }
                else if (passwordBox.Name == "ConfirmPasswordBox")
                {
                    ConfirmPasswordPlaceholder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            if (passwordBox != null && passwordBox.Password == "")
            {
                if (passwordBox.Name == "NewPasswordBox")
                {
                    NewPasswordPlaceholder.Visibility = Visibility.Visible;
                }
                else if (passwordBox.Name == "ConfirmPasswordBox")
                {
                    ConfirmPasswordPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            if (passwordBox != null)
            {
                if (passwordBox.Name == "NewPasswordBox" && passwordBox.Password != "")
                {
                    NewPasswordPlaceholder.Visibility = Visibility.Collapsed;
                }
                else if (passwordBox.Name == "ConfirmPasswordBox" && passwordBox.Password != "")
                {
                    ConfirmPasswordPlaceholder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void SignUpButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTextBox.Text;
            string username = SignUpUsernameTextBox.Text;
            string password = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                SignUpErrorMessageTextBlock.Text = "Please fill in all fields.";
                SignUpErrorMessageTextBlock.Visibility = Visibility.Visible;
                return;
            }

            if (password != confirmPassword)
            {
                SignUpErrorMessageTextBlock.Text = "Passwords do not match.";
                SignUpErrorMessageTextBlock.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var (idToken, refreshToken, uid) = await _authService.SignUpWithEmailAndPasswordAsync(email, password);
                App.IdToken = idToken;
                var databaseService = new FirebaseDatabaseService();

                // Gán avatar mặc định
                string defaultAvatarUrl = "Icons/user.png"; // Đảm bảo file này tồn tại trong thư mục ứng dụng

                // Tạo đối tượng người dùng
                App.CurrentUser = new ChatApp.Models.UserData
                {
                    Id = uid,
                    Email = email,
                    DisplayName = username,
                    Avatar = defaultAvatarUrl
                };

                // Lưu vào Firestore
                await databaseService.SaveUserAsync(idToken, App.CurrentUser);
                Console.WriteLine($"User saved with avatar: {App.CurrentUser.Avatar}");

                // Gửi email xác nhận
                await _authService.SendEmailVerificationAsync(idToken);

                MessageBox.Show("Registration successful! Please verify your email to log in.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                SignUpErrorMessageTextBlock.Text = ex.Message;
                SignUpErrorMessageTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void LoginTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void TextBlock_MouseEnter(object sender, MouseEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            if (textBlock != null)
            {
                textBlock.TextDecorations = TextDecorations.Underline;
            }
        }

        private void TextBlock_MouseLeave(object sender, MouseEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            if (textBlock != null)
            {
                textBlock.TextDecorations = null;
            }
        }

    }
}