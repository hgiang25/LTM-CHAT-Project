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
using Google.Cloud.Firestore;
using static Google.Cloud.Firestore.V1.StructuredAggregationQuery.Types.Aggregation.Types;
using System.Windows.Controls.Primitives;

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
        private ObservableCollection<NotificationData> _notifications;
        private UserData _selectedUser;
        private string _currentChatRoomId;
        private string _idToken;
        private DispatcherTimer _refreshTimer; // Timer cho bạn bè và lời mời
        private DispatcherTimer _messageRefreshTimer; // Timer riêng cho tin nhắn
        private string _lastMessageTimestamp; // Lưu thời gian tin nhắn cuối cùng

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
            _notifications = new ObservableCollection<NotificationData>();
            Loaded += ChatWindow_Loaded;
            Closing += Window_Closing;

            // Timer cho bạn bè và lời mời
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15) // Cập nhật bạn bè/lời mời mỗi 60 giây
            };
            _refreshTimer.Tick += RefreshFriendsAndRequests_Tick;

            // Timer riêng cho tin nhắn
            _messageRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3) // Cập nhật tin nhắn mỗi 5 giây
            };
            _messageRefreshTimer.Tick += MessageRefreshTimer_Tick;
        }

        // Thay thế ChatWindow_Loaded
        private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeChatAsync();
            _refreshTimer.Start();
            _messageRefreshTimer.Start();
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

                _databaseService.StartListeningForNotifications(App.CurrentUser.Id, notif =>
                {
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        // Cập nhật danh sách UI hoặc thông báo
                        _notifications.Add(notif);

                        // Nếu là tin từ người đang chat, thì đánh dấu là đã đọc luôn
                        if (_selectedUser != null && notif.From == _selectedUser.Id && !notif.IsRead)
                        {
                            await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notif.Id);
                        }

                        // Cập nhật lại số lượng chưa đọc
                        int unreadCount = await _databaseService.CountUnreadNotificationsAsync(App.CurrentUser.Id);
                        NotificationCountText.Text = unreadCount.ToString();
                        NotificationCountText.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    });
                });


                // Tải dữ liệu ban đầu
                await RefreshFriendsAndRequestsAsync();
                await LoadAllUsersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize chat: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Thêm hai hàm mới cho timer
        private async void RefreshFriendsAndRequests_Tick(object sender, EventArgs e)
        {
            await RefreshFriendsAndRequestsAsync();
        }

        private async void MessageRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_selectedUser != null && !string.IsNullOrEmpty(_currentChatRoomId))
            {
                await RefreshMessagesAsync();                
            }
            await RefreshNotificationAsync();
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
                        // Tạo danh sách tạm để so sánh
                        var newUsers = new ObservableCollection<UserData>();
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
                            newUsers.Add(friend);
                        }

                        // Chỉ cập nhật _users nếu danh sách thay đổi
                        if (!_users.SequenceEqual(newUsers, new UserDataComparer()))
                        {
                            _users.Clear();
                            foreach (var user in newUsers)
                            {
                                _users.Add(user);
                            }
                        }

                        // Cập nhật người dùng đang chọn mà không gây làm mới giao diện nhắn tin
                        if (previouslySelectedUserId != null)
                        {
                            var userToSelect = _users.FirstOrDefault(u => u.Id == previouslySelectedUserId);
                            if (userToSelect != null && userToSelect != UserListBox.SelectedItem)
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
                var friendIds = friends?.Select(f => f.Id).ToList() ?? new List<string>();
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
                    AllUsersListBox.ItemsSource = _allUsers;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh friends and requests: {ex.Message}");
            }
        }

        // Thêm class UserDataComparer để so sánh danh sách bạn bè
        private class UserDataComparer : IEqualityComparer<UserData>
        {
            public bool Equals(UserData x, UserData y)
            {
                if (x == null || y == null) return false;
                return x.Id == y.Id && x.DisplayName == y.DisplayName && x.Email == y.Email && x.Avatar == y.Avatar && x.IsOnline == y.IsOnline;
            }

            public int GetHashCode(UserData obj)
            {
                return obj.Id.GetHashCode();
            }
        }

        private void ViewSentFriendRequests_Click(object sender, RoutedEventArgs e)
        {
            // Đảm bảo danh sách đã được làm mới
            SentFriendRequestsListBox.ItemsSource = _sentFriendRequests;
        }
        private async Task RefreshNotificationAsync()
        {
            Console.WriteLine($"[DEBUG] Start refresh notification is running");

            try
            {
                var notifications = await _databaseService.GetNotificationsAsync(App.CurrentUser.Id);
                if (notifications != null)
                {
                    // Kiểm tra và xử lý từng thông báo
                    foreach (var notification in notifications)
                    {
                        // Đánh dấu thông báo là đã đọc nếu là từ người đang chat
                        if (_selectedUser != null && !notification.IsRead && notification.From == _selectedUser.Id)
                        {
                            try
                            {
                                await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notification.Id);
                                Console.WriteLine($"Marked notification as read: {notification.Timestamp}");
                                _notifications.Add(notification);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to mark notification as read: {ex.Message}");
                            }
                        }
                    }

                    // ✅ Cập nhật số lượng thông báo chưa đọc trên giao diện (không phụ thuộc vào _selectedUser)
                    try
                    {
                        Console.WriteLine("Calling CountUnreadNotificationsAsync...");
                        int unreadCount = await _databaseService.CountUnreadNotificationsAsync(App.CurrentUser.Id);
                        Console.WriteLine($"Returned unread count = {unreadCount}");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            NotificationCountText.Text = unreadCount.ToString();
                            NotificationCountText.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to refresh notification count: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh notifications: {ex.Message}");
            }
        }


        private async Task RefreshMessagesAsync()
        {
            try
            {
                var messages = await _databaseService.GetMessagesAsync(_currentChatRoomId, _lastMessageTimestamp);
                if (messages != null && messages.Any())
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var message in messages)
                        {
                            // Đánh dấu tin nhắn là "đã xem" nếu người nhận là người dùng hiện tại                          
                            if (!message.IsSeen && message.ReceiverId == App.CurrentUser.Id)
                            {
                                message.IsSeen = true;
                                _ = _databaseService.MarkMessageAsSeenAsync(_currentChatRoomId, message.MessageId);
                            }
                            _messages.Add(message);

                            var isMine = message.SenderId == App.CurrentUser.Id;

                            if (message.MessageType == "Voice")
                            {
                                if (string.IsNullOrEmpty(message.FileUrl))
                                {
                                    var errorText = new TextBlock
                                    {
                                        Text = "Lỗi: Tin nhắn thoại không khả dụng",
                                        Foreground = Brushes.Red,
                                        FontSize = 14
                                    };
                                    MessagesStackPanel.Children.Add(CreateMessageBubble(errorText, isMine));
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
                                        Margin = new Thickness(5, 0, 5, 0)
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

                                    MessagesStackPanel.Children.Add(CreateMessageBubble(playButton, isMine));
                                }
                                catch (Exception)
                                {
                                    var errorText = new TextBlock
                                    {
                                        Text = "Lỗi: Tin nhắn thoại không khả dụng",
                                        Foreground = Brushes.Red,
                                        FontSize = 14
                                    };
                                    MessagesStackPanel.Children.Add(CreateMessageBubble(errorText, isMine));
                                }
                            }
                            else if (message.MessageType == "Image")
                            {
                                var image = new Image
                                {
                                    Width = 200,
                                    Height = 200
                                };
                                var binding = new Binding("FileUrl")
                                {
                                    Source = message,
                                    Converter = (IValueConverter)FindResource("ImageUrlConverter"),
                                    FallbackValue = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute))
                                };
                                image.SetBinding(Image.SourceProperty, binding);
                                MessagesStackPanel.Children.Add(CreateMessageBubble(image, isMine));
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
                                    FontSize = 16,
                                    TextWrapping = TextWrapping.Wrap
                                };
                                MessagesStackPanel.Children.Add(CreateMessageBubble(textBlock, isMine));
                            }
                            else if (message.MessageType == "Emoji")
                            {
                                try
                                {
                                    var emojiPath = $"pack://application:,,,/Emoji/{message.Content}.png";
                                    var emojiImage = new Image
                                    {
                                        Source = new BitmapImage(new Uri(emojiPath, UriKind.Absolute)),
                                        Width = 40,
                                        Height = 40,
                                        Stretch = Stretch.Uniform
                                    };

                                    var emojiContainer = new Border
                                    {
                                        Child = emojiImage,
                                        Background = isMine ? Brushes.LightGreen : Brushes.White,
                                        CornerRadius = new CornerRadius(10),
                                        Padding = new Thickness(10),
                                        Margin = new Thickness(5),
                                        HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                                        {
                                            BlurRadius = 5,
                                            Opacity = 0.2,
                                            ShadowDepth = 2
                                        }
                                    };

                                    MessagesStackPanel.Children.Add(emojiContainer);
                                    continue;
                                }
                                catch
                                {
                                    MessagesStackPanel.Children.Add(new TextBlock
                                    {
                                        Text = "[Không thể hiển thị emoji]",
                                        Foreground = Brushes.Red,
                                        FontSize = 14,
                                        Margin = new Thickness(5),
                                        HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
                                    });
                                    continue;
                                }
                            }
                            else
                            {
                                var bubble = CreateMessageBubble(
                                    message.Content,
                                    DateTime.Parse(message.Timestamp).ToLocalTime().ToShortTimeString(),
                                    isMine,
                                    message.IsSeen
                                );
                                MessagesStackPanel.Children.Add(bubble);
                            }
                        }

                        _lastMessageTimestamp = messages.Max(m => m.Timestamp);
                        MessagesScrollViewer.ScrollToEnd();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh messages: {ex.Message}");
            }
        }

        private UIElement CreateMessageBubble(string text, string time, bool isMine, bool isSeen = false, string messageType = "Text", string fileUrl = null)
        {
            var stack = new StackPanel();

            if (messageType == "Image" && !string.IsNullOrEmpty(fileUrl))
            {
                // Hiển thị hình ảnh emoji
                var image = new Image
                {
                    Source = new BitmapImage(new Uri(fileUrl, UriKind.RelativeOrAbsolute)),
                    Width = 80,
                    Height = 80,
                    Margin = new Thickness(0, 0, 0, 5),
                    Stretch = Stretch.UniformToFill
                };
                stack.Children.Add(image);
            }
            else
            {
                // Hiển thị tin nhắn văn bản
                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(0),
                    Padding = new Thickness(10),
                    TextAlignment = TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.Wrap
                };
                stack.Children.Add(textBlock);
            }

            // Thời gian gửi
            var timeBlock = new TextBlock
            {
                Text = time,
                FontSize = 10,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            };
            stack.Children.Add(timeBlock);

            // Trạng thái seen
            if (isMine && isSeen)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "✔ Seen",
                    FontSize = 10,
                    Foreground = Brushes.Green,
                    HorizontalAlignment = HorizontalAlignment.Right
                });
            }

            // Bọc trong Border
            var border = new Border
            {
                Background = isMine ? Brushes.LightGreen : Brushes.White,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(5),
                MaxWidth = 300,
                Child = stack,
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 5,
                    Opacity = 0.2,
                    ShadowDepth = 2
                }
            };

            return border;
        }


        private UIElement CreateMessageBubble(UIElement content, bool isMine)
        {
            var stack = new StackPanel();
            stack.Children.Add(content);

            return new Border
            {
                Background = isMine ? Brushes.LightGreen : Brushes.White,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(5),
                MaxWidth = 300,
                Child = stack,
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 5,
                    Opacity = 0.2,
                    ShadowDepth = 2
                }
            };
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
            var newSelectedUser = UserListBox.SelectedItem as UserData;

            // Chỉ làm mới nếu người dùng được chọn thay đổi
            if (newSelectedUser != null && (newSelectedUser != _selectedUser || _currentChatRoomId == null))
            {
                _selectedUser = newSelectedUser;
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
                    _currentChatRoomId = null;
                    _lastMessageTimestamp = null;
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
                _lastMessageTimestamp = null;
                _messages.Clear();
                MessagesStackPanel.Children.Clear();
                await RefreshMessagesAsync();
                await RefreshNotificationAsync();
            }
            else if (newSelectedUser == null)
            {
                _selectedUser = null;
                MessagesStackPanel.Children.Clear();
                ChatWithTextBlock.Text = "Chat with [User]";
                ProfileAvatar.Source = null;
                ProfileUsername.Text = "Username: [Username]";
                ProfileEmail.Text = "Email: user@example.com";
                ProfileStatus.Text = "Status: Offline";
                _currentChatRoomId = null;
                _lastMessageTimestamp = null;
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
                    MessageType = "Text",
                    IsSeen = false
                };

                await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);

                await Dispatcher.InvokeAsync(() =>
                {
                    _messages.Add(message);
                    var bubble = CreateMessageBubble(
                        message.Content,
                        DateTime.Parse(message.Timestamp).ToLocalTime().ToShortTimeString(),
                        true,
                        message.IsSeen
                    );
                    MessagesStackPanel.Children.Add(bubble);
                    MessagesScrollViewer.ScrollToEnd();
                    MessageTextBox.Text = string.Empty;
                    _lastMessageTimestamp = message.Timestamp;
                });
                // 🔔 Gửi thông báo
                try
                {
                    await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, messageContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send notification: {ex.Message}");
                    // Có thể thông báo cho người dùng, nhưng không làm gián đoạn quy trình
                }
                await Dispatcher.InvokeAsync(() => MessageTextBox.Text = string.Empty);
                await RefreshMessagesAsync();
                await RefreshNotificationAsync();
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
                _messageRefreshTimer.Stop();
                Console.WriteLine("Timers stopped on window closing.");

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
               await _databaseService.StopListeningForNotificationsAsync();

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
            if (SearchTextBox.Visibility == Visibility.Hidden)
            {
                SearchTextBox.Visibility = Visibility.Visible;
                SearchTextBox.Focus(); // Focus luôn để user gõ liền
            }
            else
            {
                SearchTextBox.Visibility = Visibility.Hidden;
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

        private async void NotificationButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thông báo
            //NotificationPopup.IsOpen = !NotificationPopup.IsOpen;
            NotificationPopup.IsOpen = true;
            NotificationListPanel.Children.Clear();

            // Lấy danh sách tất cả thông báo
            var notifications = await _databaseService.GetNotificationsAsync(App.CurrentUser.Id);

            // Gom nhóm theo người gửi và đếm số lượng chưa đọc
            var grouped = notifications
                .Where(n => !n.IsRead)
                .GroupBy(n => n.From)
                .Select(g => new NotificationSummary
                {
                    SenderId = g.Key,
                    SenderName = GetUserNameById(g.Key), // Nếu bạn có thông tin tên
                    UnreadCount = g.Count()
                });

            foreach (var item in grouped)
            {
                var button = new Button
                {
                    Content = $"{item.SenderName ?? item.SenderId}: {item.UnreadCount} tin nhắn",
                    Margin = new Thickness(0, 5, 0, 5),
                    Tag = item.SenderId,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                button.Click += NotificationItem_Click;

                NotificationListPanel.Children.Add(button);
            }
        }

        private string GetUserNameById(string id)
        {
            // Nếu bạn có sẵn danh sách bạn bè
            var user = _users.FirstOrDefault(f => f.Id == id);
            return user?.DisplayName ?? id;
        }
        private async void NotificationItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string senderId)
            {
                // Tìm user trong danh sách bạn
                var targetUser = _users.FirstOrDefault(u => u.Id == senderId);
                if (targetUser != null)
                {
                    UserListBox.SelectedItem = targetUser;

                    // Đánh dấu các tin nhắn từ người đó là đã đọc
                    var notifications = await _databaseService.GetNotificationsAsync(App.CurrentUser.Id);
                    foreach (var notif in notifications.Where(n => n.From == senderId && !n.IsRead))
                    {
                        await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notif.Id);
                    }

                    // Làm mới lại số lượng chưa đọc
                    await RefreshNotificationAsync();
                }

                NotificationPopup.IsOpen = false;
            }
        }



        private void MoreOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var stackPanel = button?.Parent as StackPanel;
            var popup = stackPanel?.Children.OfType<Popup>().FirstOrDefault();
            if (popup != null)
                popup.IsOpen = true;
        }

        private void AddFriendsTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Chuyển sang tab Add Friends
            TabControl.SelectedIndex = 1;

        }

        private void AddGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thêm nhóm
        }
        private void ThumbtackButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thêm nhóm
        }
        private void BlockUserButton_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút thêm nhóm
        }
        private void ChatTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Chuyển sang tab Chat
            TabControl.SelectedIndex = 0;
        }
        private void Optional_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút xem thêm người dùng
        }
        private void SearchButtonChat_Click(object sender, RoutedEventArgs e)
        {
            // Code xử lý khi nhấn nút tìm kiếm trong chat
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
            AttachOptionsPanel.IsOpen = !AttachOptionsPanel.IsOpen;
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
                    await RefreshNotificationAsync();
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
                    await RefreshNotificationAsync();
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
                await RefreshNotificationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send voice message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
        }


        private async void EmojiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Please select a user to chat with.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var button = sender as Button;
                string emojiKey = button?.Tag as string; // VD: "cuoi"

                if (!string.IsNullOrEmpty(emojiKey))
                {
                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser.Id,
                        Content = emojiKey, // chính là tên ảnh
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        MessageType = "Emoji" // dùng để phân biệt với Image
                    };

                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                    EmojiPopup.IsOpen = false;
                    //EmojiPanel.Visibility = Visibility.Collapsed;
                    await RefreshMessagesAsync();
                    await RefreshNotificationAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send emoji: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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