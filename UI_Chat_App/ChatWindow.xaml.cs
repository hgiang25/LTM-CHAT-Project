using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatApp.Models;
using ChatApp.Services;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Data;
using NAudio.Wave;
using System.Windows.Threading;
using UI_Chat_App.Converters;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace UI_Chat_App
{
    public partial class ChatWindow : Window
    {
        private readonly FirebaseDatabaseService _databaseService;
        private readonly FirebaseAuthService _authService;
        private ObservableCollection<UserData> _users; // Danh sách bạn bè
        private ObservableCollection<UserData> _allUsers; // Danh sách tất cả người dùng
        private ObservableCollection<FriendRequestWithUserInfo> _friendRequests; // Danh sách lời mời kết bạn
        private ObservableCollection<FriendRequestWithUserInfo> _sentFriendRequests; // Danh sách lời mời đã gửi
        private ObservableCollection<MessageData> _messages; // Danh sách tin nhắn
        private UserData _selectedUser;
        private string _currentChatRoomId;
        private string _idToken;
        private DispatcherTimer _refreshTimer; // Timer duy nhất để polling

        // Thuộc tính công khai để truy cập _sentFriendRequests từ HasPendingRequestConverter
        public ObservableCollection<FriendRequestWithUserInfo> SentFriendRequests => _sentFriendRequests;

        public ChatWindow()
        {
            InitializeComponent();
            _databaseService = new FirebaseDatabaseService();
            _authService = new FirebaseAuthService();
            _users = new ObservableCollection<UserData>();
            _allUsers = new ObservableCollection<UserData>();
            _friendRequests = new ObservableCollection<FriendRequestWithUserInfo>(); 
            _sentFriendRequests = new ObservableCollection<FriendRequestWithUserInfo>();
            _messages = new ObservableCollection<MessageData>(); // Khởi tạo danh sách tin nhắn
            Loaded += ChatWindow_Loaded;
            Closing += Window_Closing;

            // Khởi tạo timer duy nhất
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15) // Cập nhật mỗi 5 giây
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeChatAsync();
            _refreshTimer.Start(); // Bắt đầu timer
        }

        private async Task InitializeChatAsync()
        {
            try
            {
                _idToken = App.IdToken;

                if (string.IsNullOrEmpty(_idToken))
                {
                    throw new Exception("Authentication token is missing. Please log in again.");
                }
                Console.WriteLine($"idToken: {_idToken}");

                if (App.CurrentUser == null || string.IsNullOrEmpty(App.CurrentUser.Id))
                {
                    throw new Exception("Current user is null or invalid.");
                }
                Console.WriteLine($"Current user: {App.CurrentUser.Id}, {App.CurrentUser.DisplayName}, {App.CurrentUser.Email}");

                // Lưu thông tin hiện tại để tránh bị ghi đè
                string originalDisplayName = App.CurrentUser.DisplayName;
                string originalEmail = App.CurrentUser.Email;

                // Lấy thông tin người dùng từ Firestore
                var userFromFirestore = await _databaseService.GetUserAsync(App.CurrentUser.Id);
                if (userFromFirestore != null)
                {
                    App.CurrentUser.Avatar = userFromFirestore.Avatar;
                    App.CurrentUser.Id = userFromFirestore.Id;
                    App.CurrentUser.DisplayName = originalDisplayName;
                    App.CurrentUser.Email = originalEmail;
                    Console.WriteLine($"User loaded from Firestore: {App.CurrentUser.Id}, Avatar: {App.CurrentUser.Avatar}");
                }

                // Đặt IsOnline = true khi đăng nhập
                App.CurrentUser.IsOnline = true;
                if (string.IsNullOrEmpty(App.CurrentUser.Avatar) || App.CurrentUser.Avatar.Equals("icons/user.png", StringComparison.OrdinalIgnoreCase))
                {
                    App.CurrentUser.Avatar = "Icons/user.png";
                }
                await _databaseService.SaveUserAsync(_idToken, App.CurrentUser);
                Console.WriteLine($"Set IsOnline = true and default avatar for user {App.CurrentUser.Id}");

                // Làm mới giao diện
                await Dispatcher.InvokeAsync(() =>
                {
                    UsernameTextBlock.Text = App.CurrentUser.DisplayName;
                    ProfileUsername.Text = $"Username: {App.CurrentUser.DisplayName}";
                    ProfileEmail.Text = $"Email: {App.CurrentUser.Email}";
                    ProfileStatus.Text = "Status: Online";
                    App.CurrentUser.RaisePropertyChanged(nameof(App.CurrentUser.Avatar));
                });

                // Gán ItemsSource cho các ListBox
                UserListBox.ItemsSource = _users;
                FriendRequestsListBox.ItemsSource = _friendRequests;
                AllUsersListBox.ItemsSource = _allUsers;

                // Tải dữ liệu ban đầu
                await RefreshFriendsAndRequestsAsync();
                await LoadAllUsersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize chat: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Làm mới danh sách bạn bè, lời mời, và tin nhắn
            await RefreshFriendsAndRequestsAsync();
            if (_selectedUser != null && !string.IsNullOrEmpty(_currentChatRoomId))
            {
                await RefreshMessagesAsync();
            }
        }

        private async Task RefreshFriendsAndRequestsAsync()
        {
            try
            {
                // Lấy danh sách bạn bè
                var friends = await _databaseService.GetFriendsAsync(App.CurrentUser.Id);
                if (friends != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var previouslySelectedUserId = _selectedUser?.Id;
                        _users.Clear();
                        foreach (var friend in friends)
                        {
                            if (string.IsNullOrEmpty(friend.Avatar))
                            {
                                friend.Avatar = "Icons/user.png";
                                _databaseService.SaveUserAsync(_idToken, friend).ContinueWith(t =>
                                {
                                    if (t.IsFaulted)
                                        Console.WriteLine($"Failed to save default avatar for friend {friend.Id}: {t.Exception.Message}");
                                });
                            }
                            _users.Add(friend);
                        }

                        // Cập nhật người dùng đang chọn
                        if (previouslySelectedUserId != null)
                        {
                            var userToSelect = _users.FirstOrDefault(u => u.Id == previouslySelectedUserId);
                            if (userToSelect != null)
                            {
                                UserListBox.SelectedItem = userToSelect;
                                _selectedUser = userToSelect;
                                ProfileStatus.Text = $"Status: {(_selectedUser.IsOnline ? "Online" : "Offline")}";
                                ProfileAvatar.Source = (ImageSource)new ImageUrlConverter().Convert(_selectedUser.Avatar, typeof(ImageSource), null, null);
                            }
                        }
                    });
                }

                // Lấy danh sách lời mời kết bạn (nhận được)
                var receivedRequests = await _databaseService.GetFriendRequestsAsync(App.CurrentUser.Id);
                if (receivedRequests != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _friendRequests.Clear();
                        foreach (var request in receivedRequests)
                        {
                            _friendRequests.Add(request);
                        }
                    });
                }

                // Lấy danh sách lời mời đã gửi
                var sentRequests = await _databaseService.GetSentFriendRequestsAsync(App.CurrentUser.Id);
                if (sentRequests != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _sentFriendRequests.Clear();
                        foreach (var request in sentRequests)
                        {
                            _sentFriendRequests.Add(request);
                        }
                        Console.WriteLine($"Updated friend requests with {_friendRequests.Count} pending requests and {_sentFriendRequests.Count} sent requests.");
                    });
                }

                // Làm mới danh sách tất cả người dùng (chỉ hiển thị người chưa là bạn bè)
                var usersDict = await _databaseService.GetAllUsersAsync(_idToken);
                var allUsers = usersDict.Values.Where(u => u.Id != App.CurrentUser.Id).ToList();

                // Lấy danh sách ID của bạn bè
                var friendIds = friends?.Select(f => f.Id).ToList() ?? new List<string>();

                // Lọc danh sách người dùng để chỉ giữ lại những người chưa là bạn bè
                var nonFriends = allUsers.Where(u => !friendIds.Contains(u.Id)).ToList();

                await Dispatcher.InvokeAsync(async () =>
                {
                    _allUsers.Clear();
                    foreach (var user in nonFriends)
                    {
                        if (string.IsNullOrEmpty(user.Avatar))
                        {
                            user.Avatar = "Icons/user.png";
                            await _databaseService.SaveUserAsync(_idToken, user);
                        }
                        _allUsers.Add(user);
                    }
                    AllUsersListBox.ItemsSource = null;
                    AllUsersListBox.ItemsSource = _allUsers; // Làm mới giao diện
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh friends and requests: {ex.Message}");
            }
        }

        private void ViewSentFriendRequests_Click(object sender, RoutedEventArgs e)
        {
            // Đảm bảo danh sách đã được làm mới
            SentFriendRequestsListBox.ItemsSource = _sentFriendRequests;
        }

        private async Task RefreshMessagesAsync()
        {
            try
            {
                var messages = await _databaseService.GetMessagesAsync(_currentChatRoomId);
                if (messages != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _messages.Clear();
                        foreach (var message in messages)
                        {
                            // Đánh dấu tin nhắn là "đã xem" nếu người nhận là người dùng hiện tại
                            if (!message.IsSeen && message.ReceiverId == App.CurrentUser.Id)
                            {
                                message.IsSeen = true;
                                _databaseService.MarkMessageAsSeenAsync(_currentChatRoomId, message.MessageId).ContinueWith(t =>
                                {
                                    if (t.IsFaulted)
                                        Console.WriteLine($"Failed to mark message as seen: {t.Exception.Message}");
                                    else
                                        Console.WriteLine($"Marked message as seen: {message.Timestamp}");
                                });
                            }
                            _messages.Add(message);
                        }

                        // Cập nhật giao diện tin nhắn
                        MessagesStackPanel.Children.Clear();
                        foreach (var message in _messages)
                        {
                            var stackPanel = new StackPanel
                            {
                                HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                Margin = new Thickness(5, 5, 5, 0)
                            };

                            if (message.MessageType == "Voice")
                            {
                                if (string.IsNullOrEmpty(message.FileUrl))
                                {
                                    var errorText = new TextBlock
                                    {
                                        Text = "Lỗi: Tin nhắn thoại không khả dụng",
                                        Foreground = Brushes.Red,
                                        Margin = new Thickness(5, 0, 5, 0),
                                        HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left
                                    };
                                    stackPanel.Children.Add(errorText);
                                    continue;
                                }

                                try
                                {
                                    string tempFilePath = Path.Combine(Path.GetTempPath(), $"voice_{DateTime.Now.Ticks}.wav");
                                    using (var client = new System.Net.WebClient())
                                    {
                                        client.DownloadFile(message.FileUrl, tempFilePath);
                                    }

                                    var playButton = new Button
                                    {
                                        Content = "Phát tin nhắn thoại",
                                        Tag = tempFilePath,
                                        Margin = new Thickness(5, 0, 5, 0),
                                        HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left
                                    };
                                    playButton.Click += (s, e) =>
                                    {
                                        try
                                        {
                                            var filePath = (string)((Button)s).Tag;
                                            using (var audioFile = new AudioFileReader(filePath))
                                            using (var outputDevice = new WaveOutEvent())
                                            {
                                                outputDevice.Init(audioFile);
                                                outputDevice.Play();
                                                while (outputDevice.PlaybackState == PlaybackState.Playing)
                                                {
                                                    System.Threading.Thread.Sleep(100);
                                                }
                                            }
                                        }
                                        catch (Exception playEx)
                                        {
                                            MessageBox.Show($"Không thể phát tin nhắn thoại: {playEx.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                                        }
                                    };

                                    stackPanel.Children.Add(playButton);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Không thể tải tin nhắn thoại từ {message.FileUrl}: {ex.Message}");
                                    var errorText = new TextBlock
                                    {
                                        Text = "Lỗi: Tin nhắn thoại không khả dụng",
                                        Foreground = Brushes.Red,
                                        Margin = new Thickness(5, 0, 5, 0),
                                        HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left
                                    };
                                    stackPanel.Children.Add(errorText);
                                }
                            }
                            else if (message.MessageType == "Image")
                            {
                                var image = new Image
                                {
                                    Width = 200,
                                    Height = 200,
                                    HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left
                                };
                                var binding = new Binding("FileUrl")
                                {
                                    Source = message,
                                    Converter = (IValueConverter)FindResource("ImageUrlConverter"),
                                    FallbackValue = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute))
                                };
                                image.SetBinding(Image.SourceProperty, binding);
                                stackPanel.Children.Add(image);
                            }
                            else if (message.MessageType == "File")
                            {
                                var hyperlink = new Hyperlink
                                {
                                    NavigateUri = new Uri(message.FileUrl, UriKind.Absolute),
                                    Inlines = { new Run(message.Content) }
                                };
                                hyperlink.RequestNavigate += (sender, args) =>
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = message.FileUrl,
                                        UseShellExecute = true
                                    });
                                };

                                var textBlock = new TextBlock
                                {
                                    Inlines = { hyperlink },
                                    Style = message.SenderId == App.CurrentUser.Id
                                        ? (Style)FindResource("RightAlignedMessageStyle")
                                        : new Style(typeof(TextBlock))
                                        {
                                            Setters =
                                            {
                                                new Setter(TextBlock.FontSizeProperty, 16.0),
                                                new Setter(TextBlock.ForegroundProperty, Brushes.Black),
                                                new Setter(TextBlock.BackgroundProperty, Brushes.LightGray),
                                                new Setter(TextBlock.MarginProperty, new Thickness(0)),
                                                new Setter(TextBlock.PaddingProperty, new Thickness(10)),
                                                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left),
                                                new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left),
                                                new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)
                                            }
                                        }
                                };
                                stackPanel.Children.Add(textBlock);
                            }
                            else
                            {
                                var textBlock = new TextBlock
                                {
                                    Text = message.Content,
                                    Style = message.SenderId == App.CurrentUser.Id
                                        ? (Style)FindResource("RightAlignedMessageStyle")
                                        : new Style(typeof(TextBlock))
                                        {
                                            Setters =
                                            {
                                                new Setter(TextBlock.FontSizeProperty, IsEmoji(message.Content) ? 24.0 : 16.0),
                                                new Setter(TextBlock.ForegroundProperty, IsEmoji(message.Content) ? Brushes.Blue : Brushes.Black),
                                                new Setter(TextBlock.BackgroundProperty, Brushes.LightGray),
                                                new Setter(TextBlock.MarginProperty, new Thickness(0)),
                                                new Setter(TextBlock.PaddingProperty, new Thickness(10)),
                                                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left),
                                                new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left),
                                                new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)
                                            }
                                        }
                                };
                                stackPanel.Children.Add(textBlock);
                            }

                            if (message.SenderId == App.CurrentUser.Id && message.IsSeen)
                            {
                                var seenText = new TextBlock
                                {
                                    Text = "✔ Seen",
                                    FontSize = 12,
                                    Foreground = Brushes.Green,
                                    HorizontalAlignment = message.SenderId == App.CurrentUser.Id ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                    Margin = new Thickness(0, 2, 0, 0)
                                };
                                stackPanel.Children.Add(seenText);
                            }

                            MessagesStackPanel.Children.Add(stackPanel);
                        }

                        MessagesScrollViewer.ScrollToEnd();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh messages: {ex.Message}");
            }
        }

        private async Task LoadAllUsersAsync()
        {
            try
            {
                var usersDict = await _databaseService.GetAllUsersAsync(_idToken);
                var allUsers = usersDict.Values.Where(u => u.Id != App.CurrentUser.Id).ToList();

                // Lấy danh sách bạn bè hiện tại
                var friends = await _databaseService.GetFriendsAsync(App.CurrentUser.Id);
                var friendIds = friends?.Select(f => f.Id).ToList() ?? new List<string>();

                // Lọc danh sách người dùng để chỉ giữ lại những người chưa là bạn bè
                var nonFriends = allUsers.Where(u => !friendIds.Contains(u.Id)).ToList();


                await Dispatcher.InvokeAsync(async () =>
                {
                    _allUsers.Clear();
                    foreach (var user in nonFriends) // Sửa: Sử dụng nonFriends thay vì allUsers
                    {
                        if (string.IsNullOrEmpty(user.Avatar))
                        {
                            user.Avatar = "Icons/user.png";
                            await _databaseService.SaveUserAsync(_idToken, user);
                        }
                        _allUsers.Add(user);
                    }
                    AllUsersListBox.ItemsSource = _allUsers;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load all users: {ex.Message}");
            }
        }

        private async void UserListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UserListBox.SelectedItem as UserData;
            if (_selectedUser != null)
            {
                bool areFriends = await _databaseService.AreFriendsAsync(App.CurrentUser.Id, _selectedUser.Id);
                if (!areFriends)
                {
                    MessageBox.Show("You can only chat with friends. Please add this user as a friend first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    UserListBox.SelectedItem = null;
                    _selectedUser = null;
                    MessagesStackPanel.Children.Clear();
                    ChatWithTextBlock.Text = "Chat with [User]";
                    ProfileAvatar.Source = null;
                    ProfileUsername.Text = "Username: [Username]";
                    ProfileEmail.Text = "Email: user@example.com";
                    ProfileStatus.Text = "Status: Offline";
                    return;
                }

                ChatWithTextBlock.Text = $"Chat with {_selectedUser.DisplayName}";
                ProfileUsername.Text = $"Username: {_selectedUser.DisplayName}";
                ProfileEmail.Text = $"Email: {_selectedUser.Email}";
                ProfileStatus.Text = $"Status: {(_selectedUser.IsOnline ? "Online" : "Offline")}";

                if (!string.IsNullOrEmpty(_selectedUser.Avatar))
                {
                    try
                    {
                        ProfileAvatar.Source = new BitmapImage(new Uri(_selectedUser.Avatar, UriKind.Absolute));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load avatar for user {_selectedUser.DisplayName}: {ex.Message}");
                        ProfileAvatar.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute));
                    }
                }
                else
                {
                    ProfileAvatar.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute));
                }

                _currentChatRoomId = _databaseService.GenerateChatRoomId(App.CurrentUser.Id, _selectedUser.Id);
                await RefreshMessagesAsync();
            }
            else
            {
                MessagesStackPanel.Children.Clear();
                ChatWithTextBlock.Text = "Chat with [User]";
                ProfileAvatar.Source = null;
                ProfileUsername.Text = "Username: [Username]";
                ProfileEmail.Text = "Email: user@example.com";
                ProfileStatus.Text = "Status: Offline";
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Please select a user to chat with.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string messageContent = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(messageContent)) return;

            try
            {
                var message = new MessageData
                {
                    SenderId = App.CurrentUser.Id,
                    ReceiverId = _selectedUser.Id,
                    Content = messageContent,
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    MessageType = "Text"
                };

                await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                await Dispatcher.InvokeAsync(() => MessageTextBox.Text = string.Empty);
                await RefreshMessagesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _refreshTimer.Stop();
                Console.WriteLine("Refresh timer stopped on window closing.");

                if (App.CurrentUser != null)
                {
                    if (string.IsNullOrEmpty(App.IdToken))
                    {
                        throw new Exception("Authentication token is missing. Cannot update online status.");
                    }

                    App.CurrentUser.IsOnline = false;
                    await _databaseService.SaveUserAsync(App.IdToken, App.CurrentUser);
                    Console.WriteLine($"Set IsOnline = false for user {App.CurrentUser.Id} on window closing.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save user on window closing: {ex.Message}");
                MessageBox.Show($"Failed to update online status: {ex.Message}\nYou may still appear online.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
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

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.CurrentUser.IsOnline = false;
                await _databaseService.SaveUserAsync(App.IdToken, App.CurrentUser);
                Console.WriteLine($"Set IsOnline = false for user {App.CurrentUser.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update IsOnline on close: {ex.Message}");
            }

            Application.Current.Shutdown();
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Đặt trạng thái IsOnline = false trước khi đăng xuất
                if (App.CurrentUser != null)
                {
                    if (string.IsNullOrEmpty(App.IdToken))
                    {
                        throw new Exception("Authentication token is missing. Please log in again.");
                    }

                    App.CurrentUser.IsOnline = false;
                    await _databaseService.SaveUserAsync(App.IdToken, App.CurrentUser);
                    Console.WriteLine($"Set IsOnline = false for user {App.CurrentUser.Id} on logout.");
                }

                // Đóng cửa sổ hiện tại và mở lại cửa sổ đăng nhập
                var mainWindow = new MainWindow(); // Đã sửa từ LoginWindow thành MainWindow
                mainWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                // Ghi log lỗi và thông báo cho người dùng
                Console.WriteLine($"Failed to logout: {ex.Message}");
                MessageBox.Show($"Failed to logout: {ex.Message}\nYou may still appear online. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchButtonListUser_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox.Visibility == Visibility.Collapsed)
            {
                SearchTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                SearchTextBox.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchTextBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(searchText))
            {
                UserListBox.ItemsSource = _users;
            }
            else
            {
                var bestMatch = _users
                    .Select(user => new
                    {
                        User = user,
                        MatchScore = user.DisplayName.ToLower().Contains(searchText)
                            ? (searchText.Length / (float)user.DisplayName.Length)
                            : (user.Email.ToLower().Contains(searchText)
                                ? (searchText.Length / (float)user.Email.Length) * 0.5f
                                : 0f)
                    })
                    .Where(x => x.MatchScore > 0)
                    .OrderByDescending(x => x.MatchScore)
                    .FirstOrDefault();

                if (bestMatch != null)
                {
                    UserListBox.ItemsSource = new List<UserData> { bestMatch.User };
                }
                else
                {
                    UserListBox.ItemsSource = new List<UserData>();
                }
            }
        }

        private async void UserAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                    Title = "Select an avatar image"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    string originalDisplayName = App.CurrentUser.DisplayName;
                    string originalEmail = App.CurrentUser.Email;

                    string avatarUrl = await _databaseService.UploadFileToS3Async(filePath, "avatars");
                    if (string.IsNullOrEmpty(avatarUrl))
                    {
                        MessageBox.Show("Failed to upload avatar to S3.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    App.CurrentUser.Avatar = avatarUrl;
                    await _databaseService.SaveUserAsync(App.IdToken, App.CurrentUser);

                    App.CurrentUser.DisplayName = originalDisplayName;
                    App.CurrentUser.Email = originalEmail;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        App.CurrentUser.RaisePropertyChanged(nameof(App.CurrentUser.Avatar));
                        UsernameTextBlock.Text = App.CurrentUser.DisplayName;
                        ProfileUsername.Text = $"Username: {App.CurrentUser.DisplayName}";
                        ProfileEmail.Text = $"Email: {App.CurrentUser.Email}";
                    });

                    await RefreshFriendsAndRequestsAsync();
                    MessageBox.Show("Avatar updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to change avatar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NotificationButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thông báo
        }

        private void MoreOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            UserAddButton.Visibility = UserAddButton.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
            AddGroupsButton.Visibility = AddGroupsButton.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UserAddButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thêm người dùng
        }

        private void AddGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thêm nhóm
        }

        private void SearchButtonChat_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox.Visibility == Visibility.Collapsed)
            {
                SearchTextBox.Visibility = Visibility.Visible;
                SearchTextBox.Focus();
            }
            else
            {
                SearchTextBox.Visibility = Visibility.Collapsed;
                SearchTextBox.Text = string.Empty;
                UserListBox.ItemsSource = _users;
            }
        }

        private void VoiceCallButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút gọi thoại
        }

        private void VideoCallButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút gọi video
        }

        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            AttachOptionsPanel.Visibility = AttachOptionsPanel.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Please select a user to chat with.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                    Title = "Select an image to send"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    string imageUrl = await _databaseService.UploadFileToS3Async(filePath, "images");
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        MessageBox.Show("Failed to upload image to S3.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser.Id,
                        Content = "Sent an image",
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        MessageType = "Image",
                        FileUrl = imageUrl
                    };

                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                    AttachOptionsPanel.Visibility = Visibility.Collapsed;
                    await RefreshMessagesAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Please select a user to chat with.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "All files (*.*)|*.*",
                    Title = "Select a file to send"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(filePath);
                    string fileUrl = await _databaseService.UploadFileToS3Async(filePath, "files");
                    if (string.IsNullOrEmpty(fileUrl))
                    {
                        MessageBox.Show("Failed to upload file to S3.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser.Id,
                        Content = $"Sent a file: {fileName}",
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        MessageType = "File",
                        FileUrl = fileUrl,
                        FileName = fileName
                    };

                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                    AttachOptionsPanel.Visibility = Visibility.Collapsed;
                    await RefreshMessagesAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void VoiceRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Please select a user to chat with.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var waveIn = new WaveInEvent();
                string tempFilePath = Path.Combine(Path.GetTempPath(), $"voice_{DateTime.Now.Ticks}.wav");
                var writer = new WaveFileWriter(tempFilePath, waveIn.WaveFormat);

                waveIn.DataAvailable += (s, args) =>
                {
                    writer.Write(args.Buffer, 0, args.BytesRecorded);
                };

                waveIn.StartRecording();
                MessageBox.Show("Recording... Press OK to stop.", "Recording", MessageBoxButton.OK);

                waveIn.StopRecording();
                writer.Close();
                waveIn.Dispose();

                string voiceUrl = await _databaseService.UploadFileToS3Async(tempFilePath, "voice");
                if (string.IsNullOrEmpty(voiceUrl))
                {
                    MessageBox.Show("Failed to upload voice message to S3.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var message = new MessageData
                {
                    SenderId = App.CurrentUser.Id,
                    ReceiverId = _selectedUser.Id,
                    Content = "Sent a voice message",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    MessageType = "Voice",
                    FileUrl = voiceUrl
                };

                await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                File.Delete(tempFilePath);
                AttachOptionsPanel.Visibility = Visibility.Collapsed;
                await RefreshMessagesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send voice message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            EmojiPanel.Visibility = EmojiPanel.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (AttachOptionsPanel.Visibility == Visibility.Visible)
            {
                AttachOptionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void EmojiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Vui lòng chọn người dùng để trò chuyện.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var button = sender as Button;
                var textBlock = button.Content as TextBlock;
                string emoji = textBlock.Text;

                var message = new MessageData
                {
                    SenderId = App.CurrentUser.Id,
                    ReceiverId = _selectedUser.Id,
                    Content = emoji,
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    MessageType = "Text"
                };

                await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                EmojiPanel.Visibility = Visibility.Collapsed;
                await RefreshMessagesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể gửi tin nhắn emoji: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string userId)
            {
                try
                {
                    await _databaseService.SendFriendRequestAsync(_idToken, App.CurrentUser.Id, userId);
                    var icon = button.FindName("AddFriendIcon") as Image;
                    if (icon != null) icon.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/check.png"));
                    button.IsEnabled = false;
                    MessageBox.Show("Friend request sent!", "Success");
                    // Làm mới dữ liệu và giao diện ngay lập tức
                    await RefreshFriendsAndRequestsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AllUsersListBox.ItemsSource = null;
                        AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to send friend request: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ViewFriendRequests_Click(object sender, RoutedEventArgs e)
        {
            // Đảm bảo danh sách đã được làm mới
            FriendRequestsListBox.ItemsSource = _friendRequests;
        }

        private async void AcceptFriendRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string requestId)
            {
                var requestWithInfo = _friendRequests.FirstOrDefault(r => r.FriendRequest.RequestId == requestId);
                if (requestWithInfo == null)
                {
                    MessageBox.Show("Friend request not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    await _databaseService.AcceptFriendRequestAsync(_idToken, requestWithInfo.FriendRequest);
                    MessageBox.Show("Friend request accepted!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    _friendRequests.Remove(requestWithInfo);

                    // Làm mới dữ liệu và giao diện ngay lập tức
                    await RefreshFriendsAndRequestsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        FriendRequestsListBox.ItemsSource = null;
                        FriendRequestsListBox.ItemsSource = _friendRequests; // Làm mới danh sách lời mời
                        UserListBox.ItemsSource = null;
                        UserListBox.ItemsSource = _users; // Làm mới danh sách bạn bè
                        AllUsersListBox.ItemsSource = null;
                        AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng

                        // Tự động chọn người bạn mới
                        var newFriend = _users.FirstOrDefault(u => u.Id == requestWithInfo.FriendRequest.FromUserId);
                        if (newFriend != null)
                        {
                            UserListBox.SelectedItem = newFriend;
                            _selectedUser = newFriend;
                            ChatWithTextBlock.Text = $"Chat with {_selectedUser.DisplayName}";
                            ProfileUsername.Text = $"Username: {_selectedUser.DisplayName}";
                            ProfileEmail.Text = $"Email: {_selectedUser.Email}";
                            ProfileStatus.Text = $"Status: {(_selectedUser.IsOnline ? "Online" : "Offline")}";
                            ProfileAvatar.Source = (ImageSource)new ImageUrlConverter().Convert(_selectedUser.Avatar, typeof(ImageSource), null, null);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting friend request: {ex.Message}");
                    MessageBox.Show($"Failed to accept friend request: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void RejectFriendRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string requestId)
            {
                var requestWithInfo = _friendRequests.FirstOrDefault(r => r.FriendRequest.RequestId == requestId);
                if (requestWithInfo == null)
                {
                    MessageBox.Show("Friend request not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    await _databaseService.RejectFriendRequestAsync(_idToken, requestWithInfo.FriendRequest);
                    MessageBox.Show("Friend request rejected!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    _friendRequests.Remove(requestWithInfo);

                    // Làm mới dữ liệu và giao diện ngay lập tức
                    await RefreshFriendsAndRequestsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        FriendRequestsListBox.ItemsSource = null;
                        FriendRequestsListBox.ItemsSource = _friendRequests; // Làm mới danh sách lời mời
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to reject friend request: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CancelFriendRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string requestId)
            {
                try
                {
                    var requestWithInfo = _sentFriendRequests.FirstOrDefault(r => r.FriendRequest.RequestId == requestId);
                    if (requestWithInfo == null)
                    {
                        throw new Exception("No pending request found to cancel.");
                    }

                    await _databaseService.CancelFriendRequestAsync(_idToken, App.CurrentUser.Id, requestWithInfo.FriendRequest.ToUserId, requestWithInfo.FriendRequest.RequestId);
                    _sentFriendRequests.Remove(requestWithInfo);
                    MessageBox.Show("Friend request cancelled!", "Success");

                    // Làm mới dữ liệu và giao diện ngay lập tức
                    await RefreshFriendsAndRequestsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AllUsersListBox.ItemsSource = null;
                        AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to cancel friend request: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool IsEmoji(string content)
        {
            var emojis = new List<string> { "👍", "❤️", "😊", "😂", "😍" };
            return emojis.Contains(content);
        }

        private void DeleteUserButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút xóa người dùng
        }

        private void UserPenButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút chỉnh sửa thông tin người dùng
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Code xử lý khi thay đổi tab
        }
    }
}