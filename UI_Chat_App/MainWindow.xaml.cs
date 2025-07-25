﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

using ChatApp.Services;

namespace UI_Chat_App
{
    public partial class MainWindow : Window
    {
        private readonly FirebaseAuthService _authService;

        public MainWindow()
        {

            InitializeComponent();
            InitializePlaceholders();
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
                (LoginForm.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, slide);
            };

            _authService = new FirebaseAuthService();
        }
        private void InitializePlaceholders()
        {
            UsernamePlaceholder.Text = UsernameTextBox.Tag.ToString();
            PasswordPlaceholder.Text = PasswordBox.Tag.ToString();

            UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;

            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        // Dùng để di chuyển cửa sổ khi kéo chuột
        

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
            Application.Current.Shutdown();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox != null)
            {
                UsernamePlaceholder.Visibility = Visibility.Collapsed;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox != null && string.IsNullOrEmpty(textBox.Text))
            {
                UsernamePlaceholder.Visibility = Visibility.Visible;
            }
        }

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            PasswordPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            if (passwordBox != null && string.IsNullOrEmpty(passwordBox.Password))
            {
                PasswordPlaceholder.Visibility = Visibility.Visible;
            }
        }

        //private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        //{
        //    PasswordBox passwordBox = sender as PasswordBox;
        //    if (passwordBox != null)
        //    {
        //        if (passwordBox.Name == "PasswordBox" && passwordBox.Password != "")
        //        {
        //            PasswordPlaceholder.Visibility = Visibility.Collapsed;
        //        }
        //    }
        //}

        private void SignUpTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            SignUp signup = new SignUp();
            signup.Show();
            this.Close();
        }

        private void TextBlock_MouseEnter(object sender, MouseEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            textBlock.TextDecorations = TextDecorations.Underline;
        }

        private void TextBlock_MouseLeave(object sender, MouseEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            textBlock.TextDecorations = null;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {

            string email = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ErrorMessageTextBlock.Text = "Please enter both email and password.";
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
                return;
            }
            LoadingBorder.Visibility = Visibility.Visible;
            try
            {
                var (idToken, refreshToken, uid) = await _authService.SignInWithEmailAndPasswordAsync(email, password);

                bool isEmailVerified = await _authService.IsEmailVerifiedAsync(idToken);
                if (!isEmailVerified)
                {
                    ErrorMessageTextBlock.Text = "Please verify your email before logging in.";
                    ErrorMessageTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                App.IdToken = idToken;
                var databaseService = new FirebaseDatabaseService();

                // Lấy thông tin người dùng từ Firestore
                var user = await databaseService.GetUserAsync(uid);
                if (user == null)
                {
                    // Nếu không tìm thấy user, tạo mới với avatar mặc định
                    string defaultAvatarUrl = "Icons/user.png";
                    user = new ChatApp.Models.UserData
                    {
                        Id = uid,
                        Email = email,
                        DisplayName = email.Split('@')[0],
                        Avatar = defaultAvatarUrl,
                        IsOnline = true // Thêm trạng thái online khi tạo mới
                    };
                    await databaseService.SaveUserAsync(idToken, user);
                    Console.WriteLine($"Created new user with ID: {user.Id}, Avatar: {user.Avatar}");
                }
                else
                {
                    // Nếu user tồn tại, kiểm tra và gán avatar mặc định nếu cần
                    if (string.IsNullOrEmpty(user.Avatar))
                    {
                        user.Avatar = "Icons/user.png";
                    }
                    user.IsOnline = true; // Cập nhật trạng thái online khi đăng nhập
                    await databaseService.SaveUserAsync(idToken, user);
                }

                App.CurrentUser = user;
                ChatWindow chatWindow = new ChatWindow();
                chatWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                LoadingBorder.Visibility = Visibility.Collapsed;
                ErrorMessageTextBlock.Text = ex.Message;
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
            }
        }


        private async void ForgotPasswordTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string email = UsernameTextBox.Text;

            if (string.IsNullOrEmpty(email))
            {
                ErrorMessageTextBlock.Text = "Please enter your email to reset password.";
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                await _authService.SendPasswordResetEmailAsync(email);
                MessageBox.Show("Password reset email sent. Please check your inbox.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ErrorMessageTextBlock.Text = ex.Message;
                ErrorMessageTextBlock.Visibility = Visibility.Visible;
            }
        }

        private bool isPasswordVisible = false;

        private void TogglePasswordVisibility(object sender, MouseButtonEventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;

            if (isPasswordVisible)
            {
                VisiblePasswordBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                VisiblePasswordBox.Visibility = Visibility.Visible;
                PasswordToggleIcon.Source = new BitmapImage(new Uri("Icons/see.png", UriKind.Relative));
                VisiblePasswordBox.Focus();
            }
            else
            {
                PasswordBox.Visibility = Visibility.Visible;
                VisiblePasswordBox.Visibility = Visibility.Collapsed;
                PasswordToggleIcon.Source = new BitmapImage(new Uri("Icons/hide.png", UriKind.Relative));
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }

        // Thêm xử lý cho VisiblePasswordBox
        private void VisiblePasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void VisiblePasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(VisiblePasswordBox.Text))
            {
                PasswordPlaceholder.Visibility = Visibility.Visible;
            }
        }
        private void VisiblePasswordBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isPasswordVisible)
            {
                PasswordBox.Password = VisiblePasswordBox.Text;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!isPasswordVisible)
            {
                PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Password) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Đồng bộ nếu đang ẩn chế độ xem
            if (!isPasswordVisible)
            {
                VisiblePasswordBox.Text = PasswordBox.Password;
            }
        }


    }
}