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
using System.Net;
using System.Windows.Media.Animation;
using Google.Cloud.Firestore.V1;
using UI_Chat_App.Models;
using System.ComponentModel.Design.Serialization;
using Microsoft.Win32;

namespace UI_Chat_App
{
    public partial class ChatWindow : Window
    {
        private readonly FirebaseDatabaseService _databaseService;
        private readonly FirebaseAuthService _authService;
        private ObservableCollection<UserData> _users; // Danh sách bạn bè
        private ObservableCollection<GroupData> _groups; // Danh sách nhóm
        private ObservableCollection<UserData> _allUsers; // Danh sách tất cả người dùng
        private ObservableCollection<FriendRequestWithUserInfo> _friendRequests; // Danh sách lời mời kết bạn
        private ObservableCollection<FriendRequestWithUserInfo> _sentFriendRequests; // Danh sách lời mời đã gửi
        private ObservableCollection<MessageData> _messages; // Danh sách tin nhắn
        private ObservableCollection<NotificationData> _notifications;
        private UserData _selectedUser;
        private GroupData _selectedGroup;
        private string _currentChatRoomId;
        private bool _isSending = false;
        private string _idToken;
        private DispatcherTimer _refreshTimer; // Timer cho bạn bè và lời mời
        private DispatcherTimer _messageRefreshTimer; // Timer riêng cho tin nhắn
        private Timestamp? _lastMessageTimestamp;        // Lưu thời gian tin nhắn cuối cùng
        private DispatcherTimer _typingTimer;
        private bool _isTyping;
        private TypingListenerWrapper _typingStatusListener;
        public ObservableCollection<object> _chatrooms = new ObservableCollection<object>();



        // Thuộc tính công khai để truy cập _sentFriendRequests từ HasPendingRequestConverter
        public ObservableCollection<FriendRequestWithUserInfo> SentFriendRequests => _sentFriendRequests;

        public ChatWindow()
        {
            InitializeComponent();
            _databaseService = new FirebaseDatabaseService();
            _authService = new FirebaseAuthService();
            _users = new ObservableCollection<UserData>();
            _groups = new ObservableCollection<GroupData>();
            _allUsers = new ObservableCollection<UserData>();
            _friendRequests = new ObservableCollection<FriendRequestWithUserInfo>();
            _sentFriendRequests = new ObservableCollection<FriendRequestWithUserInfo>();
            _messages = new ObservableCollection<MessageData>(); // Khởi tạo danh sách tin nhắn
            _notifications = new ObservableCollection<NotificationData>();
            Loaded += ChatWindow_Loaded;
            Closing += Window_Closing;
            _chatrooms = new ObservableCollection<object> { };
        }

        // Thay thế ChatWindow_Loaded
        private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeChatAsync();
            //_refreshTimer.Start();
            EmptyPromptTextBlock.Visibility = Visibility.Visible;
            UserProfilePanel.Visibility = Visibility.Visible;
            UserProfileColumn.Width = new GridLength(230); // Hiển thị cột profile
            EditUsernameButton.Visibility = Visibility.Visible;
            UpdateUserProfile(App.CurrentUser);


            await StartListeningForMessages(_currentChatRoomId);
            StartListeningToAllGroupChanges();
            //await _databaseService.InitializeAllFriendPrioritiesAsync();
            //await _databaseService.AddBlockedFieldToAllFriendsAsync();

            // Lắng nghe yêu cầu kết bạn mới
            await _databaseService.ListenToFriendRequestsAsync(App.CurrentUser.Id, request =>
            {
                Dispatcher.Invoke(() =>
                {
                    HandleFriendRequestChanged(request);
                });
            });
            // Lắng nghe thay đổi danh sách bạn bè                     
            //StartListeningToAllFriends();                        
            await _databaseService.ListenToFriendsAsync(App.CurrentUser.Id, async (friend, changeType) =>
            {
                Dispatcher.Invoke(() =>
                {
                    switch (changeType)
                    {
                        case Google.Cloud.Firestore.DocumentChange.Type.Added:
                        case Google.Cloud.Firestore.DocumentChange.Type.Modified:
                            HandleFriendChanged(friend);
                            break;

                        case Google.Cloud.Firestore.DocumentChange.Type.Removed:
                            HandleFriendRemoved(friend);
                            break;
                    }
                });

                // Delay nhẹ để chắc chắn _users đã được cập nhật
                await Task.Delay(100);

                var friendIds = _users.Select(u => u.Id).Distinct().ToList();

                await _databaseService.ListenToEachFriendAsync(friendIds, async user =>
{
                    Dispatcher.Invoke(() =>
                    {
                        var existing = _users.FirstOrDefault(f => f.Id == user.Id);
                        if (existing != null)
                        {
                            existing.Avatar = user.Avatar;
                            existing.DisplayName = user.DisplayName;
                            existing.IsOnline = user.IsOnline;
                        }

                        if (_selectedUser?.Id == user.Id)
                        {
                            ProfileAvatar.Source = LoadAvatar(user.Avatar);
                            ProfileUsername.Text = $"Username: {user.DisplayName}";
                            ProfileStatus.Text = $"Status: {(user.IsOnline ? "Online" : "Offline")}";
                        }

                        UpdateChatroomList();
                    });
                });
            });

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
                UserListBox.ItemsSource = _chatrooms;
                FriendRequestsListBox.ItemsSource = _friendRequests;
                //AllUsersListBox.ItemsSource = _allUsers;

                _databaseService.StartListeningForNotifications(App.CurrentUser.Id, notif =>
                {
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        _notifications.Add(notif);

                        // 🔁 Nếu là người đang chat, đánh dấu là đã đọc
                        if (_selectedUser != null && notif.From == _selectedUser.Id && !notif.IsRead)
                        {
                            await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notif.Id);
                        }

                        // ✅ Nếu đang chat với nhóm và thông báo đến từ nhóm đó
                        else if (_selectedGroup != null && notif.IsGroup && notif.GroupId == _selectedGroup.GroupId && !notif.IsRead)
                        {
                            await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notif.Id);
                        }

                        // Cập nhật số lượng chưa đọc
                        int unreadCount = await _databaseService.CountUnreadNotificationsAsync(App.CurrentUser.Id);
                        NotificationCountText.Text = unreadCount.ToString();
                        NotificationCountText.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    });
                });
                // Tải dữ liệu ban đầu
                await RefreshFriendsAndRequestsAsync();
                await LoadAllUsersAsync();
                //StartListeningToUserGroups();
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
            //await RefreshGroupsAsync();
        }

        private void HandleFriendRequestChanged(FriendRequest request)
        {
            if (request == null) return;

            // Kiểm tra đã tồn tại trong danh sách chưa
            var existing = _friendRequests.FirstOrDefault(r => r.FriendRequest.RequestId == request.RequestId);
            if (existing == null)
            {
                // Lấy thông tin người gửi để hiển thị trong giao diện
                _ = Dispatcher.Invoke(async () =>
                {
                    var sender = await _databaseService.GetUserAsync(request.FromUserId);
                    if (sender != null)
                    {
                        var wrapped = new FriendRequestWithUserInfo
                        {
                            FriendRequest = request,
                            Sender = sender
                        };
                        _friendRequests.Add(wrapped);
                    }
                });
            }
            else
            {
                // Cập nhật lại nếu đã có
                existing.FriendRequest.Status = request.Status;
                existing.FriendRequest.CreatedAt = request.CreatedAt;
            }

            // Hiển thị thông báo (tuỳ bạn)
            Console.WriteLine($"🔔 Yêu cầu kết bạn mới từ {request.FromUserId}");
        }

        private void HandleFriendRemoved(FriendData removedFriend)
        {
            var userToRemove = _users.FirstOrDefault(u => u.Id == removedFriend.FriendId);
            if (userToRemove != null)
            {
                _users.Remove(userToRemove);
                _chatrooms.Remove(userToRemove);

                // Nếu đang chat với người đó thì xóa UI
                if (_selectedUser?.Id == userToRemove.Id)
                {
                    _selectedUser = null;
                    ChatWithTextBlock.Text = "";
                    MessagesStackPanel.Children.Clear();
                    EmptyPromptTextBlock.Visibility = Visibility.Visible;
                }
            }

            Dispatcher.Invoke(() =>
            {
                if (UserListBox.ItemsSource != _chatrooms)
                    UserListBox.ItemsSource = _chatrooms;
            });
        }


        private void HandleFriendChanged(FriendData friend)
        {
            if (friend == null) return;

            // Kiểm tra có trong danh sách bạn chưa
            var existing = _users.FirstOrDefault(u => u.Id == friend.FriendId);
            if (existing == null)
            {
                _ = Dispatcher.Invoke(async () =>
                {
                    var user = await _databaseService.GetUserAsync(friend.FriendId);
                    if (user != null)
                    {
                        user.Tag = friend.Priority;
                        user.IsBlocked = friend.Blocked;
                        if (string.IsNullOrEmpty(user.Avatar))
                            user.Avatar = "Icons/user.png";

                        if (!_users.Any(u => u.Id == user.Id))
                            _users.Add(user);
                        if (!_chatrooms.Any(u => (u as UserData)?.Id == user.Id))
                            _chatrooms.Add(user);
                    }
                });
            }
            else
            {
                existing.IsBlocked = friend.Blocked;
                existing.Tag = friend.Priority;
                Console.WriteLine($"🔁 Cập nhật bạn: {friend.FriendId}");
            }

            // Làm mới UI nếu cần
            Dispatcher.Invoke(() =>
            {
                if (UserListBox.ItemsSource != _chatrooms)
                    UserListBox.ItemsSource = _chatrooms;
            });
        }

        private List<string> _previousFriendIds = new List<string>();        

        private async void StartListeningToAllFriends()
        {
            await _databaseService.StopFriendListenersAsync();

            await _databaseService.ListenToFriendsAsync(App.CurrentUser.Id, async (friend, changeType) =>
            {
                // Cập nhật UI trước
                switch (changeType)
                {
                    case Google.Cloud.Firestore.DocumentChange.Type.Added:
                    case Google.Cloud.Firestore.DocumentChange.Type.Modified:
                        Dispatcher.Invoke(() => HandleFriendChanged(friend));
                        break;

                    case Google.Cloud.Firestore.DocumentChange.Type.Removed:
                        Dispatcher.Invoke(() => HandleFriendRemoved(friend));
                        break;
                }

                // Sau khi cập nhật UI, lấy danh sách ID bạn bè
                var friendIds = _users.Select(u => u.Id).Distinct().ToList();

                if (!_previousFriendIds.SequenceEqual(friendIds))
                {
                    _previousFriendIds = friendIds;

                    await _databaseService.ListenToEachFriendAsync(friendIds, async user =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var existing = _users.FirstOrDefault(f => f.Id == user.Id);
                            if (existing != null)
                            {
                                existing.Avatar = user.Avatar;
                                existing.DisplayName = user.DisplayName;
                                existing.IsOnline = user.IsOnline;
                            }

                            if (_selectedUser?.Id == user.Id)
                            {
                                ProfileAvatar.Source = LoadAvatar(user.Avatar);
                                ProfileUsername.Text = $"Username: {user.DisplayName}";
                                ProfileStatus.Text = $"Status: {(user.IsOnline ? "Online" : "Offline")}";
                            }

                            UpdateChatroomList();
                        });
                    });
                }
            });
        }



        private async Task RefreshFriendsAndRequestsAsync()
        {
            try
            {
                string previouslySelectedChatroomId = null;
                object previouslySelectedItem = UserListBox.SelectedItem;

                if (previouslySelectedItem is UserData u)
                    previouslySelectedChatroomId = u.Id;
                else if (previouslySelectedItem is GroupData g)
                    previouslySelectedChatroomId = g.GroupId;

                // ==== Load Friends using shared method ====
                var friends = await _databaseService.GetFriendsAsync(App.CurrentUser.Id);
                var newUsers = new ObservableCollection<UserData>();

                foreach (var friend in friends)
                {
                    if (string.IsNullOrEmpty(friend.Avatar))
                    {
                        friend.Avatar = "Icons/user.png";
                        _ = _databaseService.SaveUserAsync(_idToken, friend);
                    }
                    newUsers.Add(friend);
                }
                // ==== Sort theo Priority DESC ====
                newUsers = new ObservableCollection<UserData>(
                    newUsers.OrderByDescending(user => (int)(user.Tag ?? 0))
                            .ThenBy(user => user.DisplayName));


                // ==== Load Friend Requests ====
                var receivedRequests = await _databaseService.GetFriendRequestsAsync(App.CurrentUser.Id);
                var sentRequests = await _databaseService.GetSentFriendRequestsAsync(App.CurrentUser.Id);

                // ==== Load All Users (exclude friends and pending requests) ====
                var usersDict = await _databaseService.GetAllUsersAsync(_idToken);
                var allUsers = usersDict.Values.Where(user => user.Id != App.CurrentUser.Id).ToList();

                var friendIds = newUsers.Select(f => f.Id).ToHashSet();
                var receivedRequestIds = receivedRequests?.Select(r => r.FriendRequest.FromUserId).ToHashSet() ?? new HashSet<string>();
                var sentRequestIds = sentRequests?.Select(r => r.FriendRequest.ToUserId).ToHashSet() ?? new HashSet<string>();

                var nonFriends = allUsers
                    .Where(user =>
                        !friendIds.Contains(user.Id) &&
                        !sentRequestIds.Contains(user.Id) &&
                        !receivedRequestIds.Contains(user.Id))
                    .ToList();

                // ==== Load Groups ====
                var groups = await _databaseService.GetGroupsForUserAsync(App.CurrentUser.Id);
                var newGroups = new ObservableCollection<GroupData>();
                foreach (var group in groups ?? Enumerable.Empty<GroupData>())
                {
                    if (string.IsNullOrEmpty(group.Avatar))
                    {
                        group.Avatar = "Icons/group.png";
                    }
                    newGroups.Add(group);
                }

                // ==== UI Updates ====
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (!_users.SequenceEqual(newUsers, new UserDataComparer()))
                    {
                        _users.Clear();
                        foreach (var user in newUsers)
                            _users.Add(user);
                    }

                    if (!_groups.SequenceEqual(newGroups, new GroupDataComparer()))
                    {
                        _groups.Clear();
                        foreach (var group in newGroups)
                            _groups.Add(group);
                    }

                    var newChatrooms = new ObservableCollection<object>(_users.Cast<object>().Concat(_groups));
                    if (!_chatrooms.SequenceEqual(newChatrooms, new ChatroomComparer()))
                    {
                        _chatrooms.Clear();
                        foreach (var chatroom in newChatrooms)
                            _chatrooms.Add(chatroom);
                    }

                    if (UserListBox.ItemsSource != _chatrooms)
                        UserListBox.ItemsSource = _chatrooms;

                    // Restore selection
                    if (!string.IsNullOrEmpty(previouslySelectedChatroomId))
                    {
                        var itemToSelect = _chatrooms.FirstOrDefault(item =>
                            (item is UserData user && user.Id == previouslySelectedChatroomId) ||
                            (item is GroupData group && group.GroupId == previouslySelectedChatroomId));

                        if (itemToSelect != null)
                        {
                            UserListBox.SelectedItem = itemToSelect;

                            if (itemToSelect is UserData selectedUser)
                            {
                                _selectedUser = selectedUser;
                                _selectedGroup = null;
                                ProfileStatus.Text = $"Status: {(selectedUser.IsOnline ? "Online" : "Offline")}";
                                ProfileAvatar.Source = (ImageSource)new ImageUrlConverter().Convert(selectedUser.Avatar, typeof(ImageSource), null, null);
                            }
                            else if (itemToSelect is GroupData selectedGroup)
                            {
                                _selectedGroup = selectedGroup;
                                _selectedUser = null;
                                ProfileStatus.Text = $"Members: {selectedGroup.MemberCount}";
                                ProfileAvatar.Source = (ImageSource)new ImageUrlConverter().Convert(selectedGroup.Avatar, typeof(ImageSource), null, null);
                            }
                        }
                    }

                    _friendRequests.Clear();
                    foreach (var request in receivedRequests ?? Enumerable.Empty<FriendRequestWithUserInfo>())
                        _friendRequests.Add(request);

                    _sentFriendRequests.Clear();
                    foreach (var request in sentRequests ?? Enumerable.Empty<FriendRequestWithUserInfo>())
                        _sentFriendRequests.Add(request);

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

                    if (TabControl.SelectedIndex == 0)
                    {
                        if (UserListBox.ItemsSource != _chatrooms)
                            UserListBox.ItemsSource = _chatrooms;
                    }

                    if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
                    {
                        SearchTextBox_TextChanged(null, null);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh: {ex.Message}");
            }
        }



        private class GroupDataComparer : IEqualityComparer<GroupData>
        {
            public bool Equals(GroupData x, GroupData y)
            {
                if (x == null || y == null) return false;
                return x.GroupId == y.GroupId &&
                       x.Name == y.Name &&
                       x.Avatar == y.Avatar &&
                       x.MemberCount == y.MemberCount;
            }

            public int GetHashCode(GroupData obj)
            {
                return obj.GroupId.GetHashCode();
            }
        }


        public class ChatroomComparer : IEqualityComparer<object>
        {
            public bool Equals(object x, object y)
            {
                if (x is UserData ux && y is UserData uy)
                    return ux.Id == uy.Id && ux.DisplayName == uy.DisplayName && ux.Avatar == uy.Avatar;

                if (x is GroupData gx && y is GroupData gy)
                    return gx.GroupId == gy.GroupId
                           && gx.Name == gy.Name
                           && gx.Avatar == gy.Avatar;

                return false;
            }

            public int GetHashCode(object obj)
            {
                if (obj is UserData u) return u.Id.GetHashCode();
                if (obj is GroupData g) return g.GroupId.GetHashCode();
                return obj?.GetHashCode() ?? 0;
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
                    foreach (var notification in notifications)
                    {
                        if (!notification.IsRead)
                        {
                            bool shouldMarkRead = false;

                            // Nếu đang chat 1-1, đánh dấu các thông báo từ người đó là đã đọc
                            if (_selectedUser != null && !notification.IsGroup && notification.From == _selectedUser.Id)
                            {
                                shouldMarkRead = true;
                            }

                            // Nếu đang chat nhóm, đánh dấu các thông báo trong nhóm đó là đã đọc
                            if (_selectedGroup != null && notification.IsGroup && notification.GroupId == _selectedGroup.GroupId)
                            {
                                shouldMarkRead = true;
                            }

                            if (shouldMarkRead)
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
                    }

                    // Cập nhật badge số lượng thông báo chưa đọc
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


        private async Task StartListeningForMessages(string chatRoomId)
        {
            if (string.IsNullOrEmpty(chatRoomId))
            {
                Console.WriteLine("ChatRoomId is null or empty. Cannot start listening.");
                return;
            }

            await _databaseService.StartListeningToMessagesAsync(
                chatRoomId,
                async message =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (!_messages.Any(m => m.MessageId == message.MessageId))
                        {
                            await AddMessageToUI(message);
                        }
                    });
                });
        }


        private async Task AddMessageToUI(MessageData message)
        {
            try
            {
                if (message == null || string.IsNullOrEmpty(message.MessageId) || _messages.Any(m => m.MessageId == message.MessageId))
                    return;

                var isMine = message.SenderId == App.CurrentUser.Id;
                _messages.Add(message);

                bool isGroup = _selectedGroup != null;
                bool isSystemMessage = message.MessageType == "System";

                var stack = new StackPanel(); // chứa nội dung tin nhắn

                switch (message.MessageType)
                {
                    case "Text":
                        stack.Children.Add(new TextBlock
                        {
                            Text = message.Content,
                            FontSize = 16,
                            Foreground = Brushes.Black,
                            TextWrapping = TextWrapping.Wrap
                        });
                        break;

                    case "Image":
                        var image = new Image
                        {
                            Width = 200,
                            Height = 200,
                            HorizontalAlignment = message.SenderId == App.CurrentUser.Id
                                                                    ? HorizontalAlignment.Right
                                                                    : HorizontalAlignment.Left,
                            Cursor = Cursors.Hand // Thêm con trỏ tay
                        };

                        var binding = new Binding("FileUrl")
                        {
                            Source = message,
                            Converter = (IValueConverter)FindResource("ImageUrlConverter"),
                            FallbackValue = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png"))
                        };

                        image.SetBinding(Image.SourceProperty, binding);

                        // Thêm sự kiện click để phóng to ảnh
                        image.MouseLeftButtonDown += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(message.FileUrl))
                            {
                                var viewer = new ImageViewerWindow(message.FileUrl);
                                viewer.Owner = this; // Đặt cửa sổ chính là owner
                                viewer.ShowDialog();
                            }
                        };

                        stack.Children.Add(image);
                        break;


                    case "File":
                        if (string.IsNullOrEmpty(message.FileUrl) || !Uri.TryCreate(message.FileUrl, UriKind.Absolute, out var fileUri))
                            return;

                        var hyperlink = new Hyperlink
                        {
                            NavigateUri = fileUri,
                            Inlines = { new Run(message.Content ?? "Tệp đính kèm") }
                        };
                        hyperlink.RequestNavigate += (s, e) =>
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = message.FileUrl,
                                UseShellExecute = true
                            });
                        };
                        stack.Children.Add(new TextBlock
                        {
                            Inlines = { hyperlink },
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap
                        });
                        break;

                    case "Voice":
                        if (string.IsNullOrEmpty(message.FileUrl))
                        {
                            Debug.WriteLine($"⛔ Voice message không khả dụng. FileUrl: {message.FileUrl ?? "null"}");
                            stack.Children.Add(new TextBlock { Text = "Lỗi: Tin nhắn thoại không khả dụng" });
                            break;
                        }

                        var loadingText = new TextBlock
                        {
                            Text = "Đang tải tin nhắn thoại...",
                            FontStyle = FontStyles.Italic,
                            Foreground = Brushes.Gray
                        };
                        stack.Children.Add(loadingText);

                        string voicePath = null;

                        // 👇 Retry tối đa 3 lần với delay 1s giữa mỗi lần
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            voicePath = await DownloadToTempFileAsync(message.FileUrl, "wav");
                            if (!string.IsNullOrEmpty(voicePath)) break;
                            await Task.Delay(1000); // đợi 1 giây rồi thử lại
                        }

                        stack.Children.Remove(loadingText);

                        if (string.IsNullOrEmpty(voicePath))
                        {
                            stack.Children.Add(new TextBlock
                            {
                                Text = "Không thể tải tin nhắn thoại. Vui lòng thử lại sau.",
                                Foreground = Brushes.Red,
                                FontSize = 12
                            });
                            break;
                        }

                        string voiceDuration = "0:00";
                        try
                        {
                            using (var audioFile = new AudioFileReader(voicePath))
                            {
                                voiceDuration = audioFile.TotalTime.ToString(@"m\:ss");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"⛔ Không thể lấy thời lượng âm thanh: {ex.Message}");
                        }

                        // Tạo panel cho tin nhắn thoại
                        // Tạo panel cho tin nhắn thoại
                        var voicePanel = new Border
                        {
                            Background = isMine ? Brushes.LightGreen : Brushes.White,
                            CornerRadius = new CornerRadius(15),
                            Padding = new Thickness(12, 8, 12, 8),
                            Cursor = Cursors.Hand,
                            ToolTip = "Nhấn để phát",
                            MinWidth = 150, // 👈 Thêm MinWidth
                            MaxWidth = 300, // 👈 Thêm MaxWidth nếu muốn giới hạn chiều ngang
                            Effect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                BlurRadius = 5,
                                Opacity = 0.2,
                                ShadowDepth = 2
                            }
                        };

                        var voiceStack = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Left,
                        };

                        // Icon loa
                        var voiceIcon = new Image
                        {
                            Source = new BitmapImage(new Uri("pack://application:,,,/Icons/voice_play.png")),
                            Width = 20,
                            Height = 20,
                            Margin = new Thickness(0, 0, 8, 0)
                        };

                        // Thời lượng
                        var durationText = new TextBlock
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brushes.Black,
                            FontSize = 14,
                            Text = voiceDuration, // 👈 Thêm giá trị mặc định để tránh chồng
                            Margin = new Thickness(4, 0, 0, 0)
                        };

                        voiceStack.Children.Add(voiceIcon);
                        voiceStack.Children.Add(durationText);
                        voicePanel.Child = voiceStack;


                        // Sự kiện click để phát
                        voicePanel.MouseLeftButtonDown += (s, e) =>
                        {
                            try
                            {
                                var audioFile = new AudioFileReader(voicePath);
                                var outputDevice = new WaveOutEvent();
                                outputDevice.Init(audioFile);
                                outputDevice.Play();

                                // Hiệu ứng khi đang phát
                                voiceIcon.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/sound.png"));
                                voicePanel.Background = Brushes.LightBlue;

                                outputDevice.PlaybackStopped += (snd, args) =>
                                {
                                    // Khôi phục trạng thái ban đầu
                                    voiceIcon.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/voice_play.png"));
                                    voicePanel.Background = isMine ? Brushes.LightGreen : Brushes.White;
                                    audioFile.Dispose();
                                    outputDevice.Dispose();
                                };
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Không thể phát: {ex.Message}");
                            }
                        };

                        stack.Children.Add(voicePanel);
                        break;

                    case "Emoji":
                        try
                        {
                            var emojiPath = $"pack://application:,,,/Emoji/{message.Content}.png";
                            var emojiImage = new Image
                            {
                                Source = new BitmapImage(new Uri(emojiPath)),
                                Width = 40,
                                Height = 40,
                                Stretch = Stretch.Uniform
                            };
                            stack.Children.Add(emojiImage);
                        }
                        catch
                        {
                            stack.Children.Add(new TextBlock
                            {
                                Text = "[Không thể hiển thị emoji]",
                                Foreground = Brushes.Red,
                                FontSize = 14
                            });
                        }
                        break;
                }

                if (isSystemMessage)
                {
                    var systemText = new TextBlock
                    {
                        Text = message.Content,
                        FontSize = 14,
                        Foreground = Brushes.DarkSlateGray,
                        FontStyle = FontStyles.Italic,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(10)
                    };

                    var systemBorder = new Border
                    {
                        Background = Brushes.Transparent,
                        Padding = new Thickness(5),
                        Margin = new Thickness(5),
                        Child = systemText,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    MessagesStackPanel.Children.Add(systemBorder);
                    return;
                }

                // ==== Lấy thông tin người gửi ====
                string senderAvatar = "Icons/user.png";
                string senderName = "Unknown";

                if (isGroup)
                {
                    var senderUser = _users.FirstOrDefault(u => u.Id == message.SenderId)
                                  ?? _allUsers.FirstOrDefault(u => u.Id == message.SenderId);

                    if (senderUser != null)
                    {
                        senderAvatar = senderUser.Avatar;
                        senderName = senderUser.DisplayName;
                    }
                }
                else
                {
                    senderAvatar = isMine ? App.CurrentUser.Avatar : _selectedUser?.Avatar;
                    senderName = isMine ? App.CurrentUser.DisplayName : _selectedUser?.DisplayName;
                }

                // ==== Avatar ====
                var avatarImage = new Image
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Source = (ImageSource)new ImageUrlConverter().Convert(
                    string.IsNullOrEmpty(senderAvatar) ? "Icons/user.png" : senderAvatar,
                    typeof(ImageSource), null, null)
                };

                // ==== Hiển thị tên người gửi (nếu là nhóm) ====
                if (isGroup && !isMine)
                {
                    stack.Children.Insert(0, new TextBlock
                    {
                        Text = senderName,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5),
                        Foreground = Brushes.DarkSlateBlue
                    });
                }

                // ==== Thời gian gửi + trạng thái ====
                var time = message.Timestamp?.ToDateTime().ToLocalTime().ToShortTimeString() ?? "";
                stack.Children.Add(new TextBlock
                {
                    Text = time,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0)
                });

                if (isMine && message.IsSeen)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "✔ Seen",
                        FontSize = 10,
                        Foreground = Brushes.Green,
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                }

                // ==== Border bọc nội dung ====
                var messageBorder = new Border
                {
                    Background = isMine ? Brushes.LightGreen : Brushes.White,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(5),
                    MaxWidth = 400,
                    Child = stack,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 5,
                        Opacity = 0.2,
                        ShadowDepth = 2
                    }
                };

                // ==== StackPanel ngang: avatar + tin nhắn ====
                var messageRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };

                if (!isMine)
                {
                    messageRow.Children.Add(avatarImage);
                    messageRow.Children.Add(messageBorder);
                }
                else
                {
                    messageRow.Children.Add(messageBorder);
                }

                // ==== Thêm vào UI ====
                MessagesStackPanel.Children.Add(messageRow);
                MessagesScrollViewer.ScrollToEnd();

                if (!message.IsSeen && message.ReceiverId == App.CurrentUser.Id)
                {
                    message.IsSeen = true;
                    _ = _databaseService.MarkMessageAsSeenAsync(_currentChatRoomId, message.MessageId);
                }

                _lastMessageTimestamp = message.Timestamp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi hiển thị tin nhắn: {ex.Message}");
            }
        }


        private async Task<string> DownloadToTempFileAsync(string url, string extension, int retryCount = 2)
        {
            if (string.IsNullOrEmpty(url)) return null;

            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), $"chat_{Guid.NewGuid().ToString()}.{extension}");

                    using (var client = new WebClient())
                    {
                        await client.DownloadFileTaskAsync(new Uri(url), tempPath);
                    }

                    return tempPath;
                }
                catch
                {
                    if (attempt < retryCount)
                        await Task.Delay(1000); // đợi 1 giây rồi thử lại
                }
            }

            return null;
        }





        private async Task LoadInitialMessagesAsync(string chatRoomId)
        {
            var messages = await _databaseService.GetMessagesAsync(chatRoomId, _lastMessageTimestamp);

            if (messages.Any())
            {
                _lastMessageTimestamp = messages.Max(m => m.Timestamp); // Use string timestamp
            }            
                foreach (var message in messages.OrderBy(m => m.Timestamp))
                {
                    if (!_messages.Any(m => m.MessageId == message.MessageId))
                    {
                        await AddMessageToUI(message);
                    }
                }
                MessagesStackPanel.UpdateLayout();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessagesScrollViewer.ScrollToEnd();
                }), DispatcherPriority.Background);
        }

        private UIElement CreateMessageBubble(string text, string time, bool isMine, bool isSeen = false, string messageType = "Text", string fileUrl = null)
        {
            // Tin nhắn hệ thống
            if (messageType == "System")
            {
                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 14,
                    FontStyle = FontStyles.Italic,
                    Foreground = Brushes.Gray,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(5)
                };

                return new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(5),
                    Margin = new Thickness(5),
                    Child = textBlock,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            }

            // Tin nhắn người dùng
            var stack = new StackPanel();

            if (messageType == "Image" && !string.IsNullOrEmpty(fileUrl))
            {
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
                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(0),
                    Padding = new Thickness(10),
                    TextAlignment = TextAlignment.Left,
                    TextWrapping = TextWrapping.Wrap
                };
                stack.Children.Add(textBlock);
            }

            // Thời gian gửi
            stack.Children.Add(new TextBlock
            {
                Text = time,
                FontSize = 10,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 0, 0)
            });

            // Trạng thái seen (nếu là của mình)
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

            return new Border
            {
                Background = isMine ? Brushes.LightBlue : Brushes.LightGray,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(5),
                Padding = new Thickness(10),
                Child = stack,
                MaxWidth = 300,
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
        }


        private void DisplayName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Ẩn group profile nếu đang hiển thị
            GroupProfilePanel.Visibility = Visibility.Collapsed;

            // Hiển thị user profile
            UserProfilePanel.Visibility = Visibility.Visible;
            UserProfileColumn.Width = new GridLength(230); // Hiển thị cột profile
            EditUsernameButton.Visibility = Visibility.Visible;

            // Cập nhật dữ liệu cho profile của user hiện tại
            UpdateUserProfile(App.CurrentUser);

            EditUsernameButton.Visibility = Visibility.Visible;

        }

        private void UpdateUserProfile(UserData user)
        {
            ProfileUsername.Text = $"Username: {user.DisplayName}";
            ProfileEmail.Text = $"Email: {user.Email}";
            ProfileStatus.Text = $"Status: {(user.IsOnline ? "Online" : "Offline")}";

            // Load avatar - sử dụng converter đã có
            var converter = new ImageUrlConverter();
            ProfileAvatar.Source = (ImageSource)converter.Convert(
                user.Avatar,
                typeof(ImageSource),
                null,
                null
            );

            // Đặt lại trạng thái chat
            _selectedUser = null;
            _selectedGroup = null;
            UserListBox.SelectedItem = null;
            MessagesStackPanel.Children.Clear();
            ChatWithTextBlock.Text = "Chat";
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
                    //AllUsersListBox.ItemsSource = _allUsers;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load all users: {ex.Message}");
            }
        }

        private async void UserListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditUsernameButton.Visibility = Visibility.Collapsed;

            var selectedItem = UserListBox.SelectedItem;

            if (_selectedUser != null)
            {
                await SetTypingStatusAsync(false);
                _typingTimer?.Stop();
                _isTyping = false;
            }

            if (_typingStatusListener != null)
            {
                await _typingStatusListener.StopAsync();
                _typingStatusListener = null;
            }

            if (selectedItem is UserData newSelectedUser)
            {
                EmptyPromptTextBlock.Visibility = Visibility.Collapsed;

                if ((newSelectedUser != _selectedUser && newSelectedUser != null) || _currentChatRoomId == null)
                {
                    _selectedUser = newSelectedUser;
                    _selectedGroup = null;
                    _isUserProfileVisible = true;
                    _isShowingMyProfile = false;

                    bool areFriends = await _databaseService.AreFriendsAsync(App.CurrentUser.Id, _selectedUser.Id);
                    if (!areFriends)
                    {
                        MessageBox.Show("You can only chat with friends.");
                        ResetChatUI();
                        return;
                    }

                    ShowUserProfile(_selectedUser); // ✅ gọi hàm có animation

                    _currentChatRoomId = _databaseService.GenerateChatRoomId(App.CurrentUser.Id, _selectedUser.Id);
                    _lastMessageTimestamp = null;
                    _messages.Clear();
                    MessagesStackPanel.Children.Clear();
                    MessageTextBox.Clear();

                    await RefreshNotificationAsync();
                    await _databaseService.StopListeningToMessagesAsync();
                    await LoadInitialMessagesAsync(_currentChatRoomId);
                    await StartListeningForMessages(_currentChatRoomId);

                    EditUsernameButton.Visibility = Visibility.Collapsed;
                    EditGroupNameButton.Visibility = Visibility.Collapsed;

                    StartListeningToTyping();
                }
            }
            else if (selectedItem is GroupData selectedGroup)
            {
                EmptyPromptTextBlock.Visibility = Visibility.Collapsed;

                _selectedGroup = await _databaseService.GetGroupAsync(selectedGroup.GroupId);
                _selectedUser = null;
                _isUserProfileVisible = true;
                _isShowingMyProfile = false;

                await ShowGroupProfileAsync(); // ✅ gọi hàm có animation

                _currentChatRoomId = _selectedGroup.GroupId;
                _lastMessageTimestamp = null;
                _messages.Clear();
                MessagesStackPanel.Children.Clear();
                MessageTextBox.Clear();

                await RefreshNotificationAsync();
                await _databaseService.StopListeningToMessagesAsync();
                await LoadInitialMessagesAsync(_currentChatRoomId);
                await StartListeningForMessages(_currentChatRoomId);

                bool isAdmin = _selectedGroup.Members.TryGetValue(App.CurrentUser.Id, out var role)
                               && role.Equals("admin", StringComparison.OrdinalIgnoreCase);
                EditGroupNameButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                DeleteGroupButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

                EditUsernameButton.Visibility = Visibility.Collapsed;
                TypingStatusTextBlock.Text = "";
                TypingStatusTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                ResetChatUI();
            }
        }




        private void ResetChatUI()
        {
            _selectedUser = null;
            _selectedGroup = null;
            UserListBox.SelectedItem = null;
            _currentChatRoomId = null;
            _lastMessageTimestamp = null;

            MessagesStackPanel.Children.Clear();
            ChatWithTextBlock.Text = "Chat with [User/Group]";

            ProfileAvatar.Source = null;
            ProfileUsername.Text = "Username: [Username]";
            ProfileEmail.Text = "Email: user@example.com";
            ProfileStatus.Text = "Status: Offline";

            // Ẩn cả hai profile panel
            UserProfilePanel.Visibility = Visibility.Collapsed;
            GroupProfilePanel.Visibility = Visibility.Collapsed;
            EmptyPromptTextBlock.Visibility = Visibility.Visible;

            //// 👉 Reset UI liên quan đến nhóm
            //GroupMembersList.ItemsSource = null;
            //GroupMembersList.Visibility = Visibility.Collapsed;
            //PendingMembersListBox.ItemsSource = null;
            //PendingMembersPopup.IsOpen = false;

            //// Nếu bạn có thể disable các nút (tùy thiết kế)
            //ViewMembersButton.IsEnabled = false;
            //InviteMemberButton.IsEnabled = false;
            //LeaveGroupButton.IsEnabled = false;
        }



        private BitmapImage LoadAvatar(string avatarUrl)
        {
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                try
                {
                    return new BitmapImage(new Uri(avatarUrl, UriKind.Absolute));
                }
                catch
                {
                    // fall through
                }
            }
            return new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute));
        }


        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSending) return; // Ngăn chặn gửi nhiều lần
            _isSending = true;
            await SendMessageAsync();
            _isSending = false;
        }

        private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift && !_isSending)
            {
                e.Handled = true;
                _isSending = true;
                await SendMessageAsync();
                _isSending = false;
            }
        }



        //Cập nhật trạng thái "typing" lên Firebase
                        
        private async Task SetTypingStatusAsync(bool isTyping)
        {
            if (_selectedUser == null || App.CurrentUser == null) return;

            await _databaseService.SetTypingStatusAsync(App.CurrentUser.Id, _selectedUser.Id, isTyping);
        }


        private async void StartListeningToTyping()
        {
            if (_selectedUser == null || App.CurrentUser == null) return;

            var selectedUserId = _selectedUser.Id;
            var selectedDisplayName = _selectedUser.DisplayName;

            // Stop listener cũ nếu có
            if (_typingStatusListener != null)
            {
                await _typingStatusListener.StopAsync();
                _typingStatusListener = null;
            }

            var listener = _databaseService.ListenToTypingStatus(App.CurrentUser.Id, selectedUserId, isTyping =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_selectedUser == null || _selectedUser.Id != selectedUserId)
                        return;

                    TypingStatusTextBlock.Text = isTyping ? $"{selectedDisplayName} is typing..." : "";
                    TypingStatusTextBlock.Visibility = isTyping ? Visibility.Visible : Visibility.Collapsed;
                });
            });

            _typingStatusListener = new TypingListenerWrapper { Listener = listener };
        }





        private async Task SendMessageAsync()
        {
            string messageContent = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(messageContent)) return;

            var timestamp = Timestamp.GetCurrentTimestamp();

            if (_selectedUser == null && _selectedGroup == null)
            {
                MessageBox.Show("Hãy chọn người dùng hoặc nhóm để gửi tin nhắn.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // ✅ Kiểm tra block TRƯỚC khi gửi
            if (_selectedUser != null)
            {
                bool isBlocked = await _databaseService.IsBlockedBetweenUsers(App.CurrentUser.Id, _selectedUser.Id);
                if (isBlocked)
                {
                    MessageBox.Show("Bạn không thể gửi tin nhắn vì đã bị chặn hoặc đã chặn người này.", "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var message = new MessageData
            {
                SenderId = App.CurrentUser.Id,
                ReceiverId = _selectedUser?.Id, // null nếu là nhóm
                Content = messageContent,
                Timestamp = timestamp,
                MessageType = "Text",
                IsSeen = false
            };

            await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);

            await Dispatcher.InvokeAsync(() =>
            {
                if (!_messages.Any(m => m.MessageId == message.MessageId))
                {
                    _messages.Add(message);
                    var bubble = CreateMessageBubble(
                        message.Content,
                        message.Timestamp.HasValue ? message.Timestamp.Value.ToDateTime().ToLocalTime().ToShortTimeString() : "Unknown time",
                        true,
                        message.IsSeen
                    );
                    MessagesStackPanel.Children.Add(bubble);
                    MessagesScrollViewer.ScrollToEnd();
                }
                MessageTextBox.Clear();
                _lastMessageTimestamp = message.Timestamp;
            });

            if (_selectedUser != null)
            {
                await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, messageContent);
                await RefreshNotificationAsync();
            }

            if (_selectedGroup != null)
            {
                var groupMembers = await _databaseService.GetGroupMembersAsync(_selectedGroup.GroupId);
                foreach (var memberId in groupMembers)
                {
                    Console.WriteLine($"Gửi thông báo tới thành viên nhóm: {memberId}");
                    if (memberId != App.CurrentUser.Id)
                    {
                        await _databaseService.SendNotificationAsync(memberId, App.CurrentUser.Id, messageContent, _selectedGroup.GroupId);
                    }
                }

                await RefreshNotificationAsync();
            }
        }



        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                //_refreshTimer.Stop();
                //_messageRefreshTimer.Stop();
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
                await _databaseService.StopListeningToUserRelatedGroupsAsync();
                if (_typingStatusListener != null)
                {
                    await _typingStatusListener.StopAsync();
                    _typingStatusListener = null;
                }
                await _databaseService.StopFriendListenersAsync();
                //await _databaseService.StopListeningToUserGroupsAsync();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save user on window closing: {ex.Message}");
                MessageBox.Show($"Failed to update online status: {ex.Message}\nYou may still appear online.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            //await _databaseService.StopListeningForCalls();
            //AgoraService.Instance.Dispose();
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
                await _databaseService.StopListeningForNotificationsAsync();
                await _databaseService.StopListeningToUserRelatedGroupsAsync();
                if (_typingStatusListener != null)
                {
                    await _typingStatusListener.StopAsync();
                    _typingStatusListener = null;
                }
                await _databaseService.StopFriendListenersAsync();
                //await _databaseService.StopListeningToUserGroupsAsync();
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

        private void ResetSearchResults()
        {
            if (TabControl.SelectedIndex == 0) // Tab Chat
            {
                UserListBox.ItemsSource = _chatrooms;
            }
            else if (TabControl.SelectedIndex == 1) // Tab Add Friends
            {
                AllUsersListBox.ItemsSource = null;
            }
        }


        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchTextBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(searchText))
            {
                ResetSearchResults();
                return;
            }

            if (TabControl.SelectedIndex == 0) // Tìm trong bạn bè
            {
                var matchingFriends = _users
                    .Where(user => user.DisplayName.ToLower().Contains(searchText) ||
                                  user.Email.ToLower().Contains(searchText))
                    .OrderBy(user => user.DisplayName.ToLower().Contains(searchText) ? 0 : 1)
                    .ToList();

                UserListBox.ItemsSource = matchingFriends;
            }
            else if (TabControl.SelectedIndex == 1) // Tìm trong tất cả user
            {
                var matchingNonFriends = _allUsers
                    .Where(user => user.DisplayName.ToLower().Contains(searchText) ||
                                  user.Email.ToLower().Contains(searchText))
                    .OrderBy(user => user.DisplayName.ToLower().Contains(searchText) ? 0 : 1)
                    .ToList();

                AllUsersListBox.ItemsSource = matchingNonFriends;
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
                        UsernameTextBlock.Text = App.CurrentUser.DisplayName;
                        ProfileUsername.Text = $"Username: {App.CurrentUser.DisplayName}";
                        ProfileEmail.Text = $"Email: {App.CurrentUser.Email}";
                        ProfileAvatar.Source = LoadAvatar(App.CurrentUser.Avatar); // ✅ đảm bảo load ảnh mới
                    });

                    await RefreshFriendsAndRequestsAsync();
                    // await RefreshGroupsAsync();
                    MessageBox.Show("Avatar updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to change avatar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private CustomPopupPlacement[] NotificationPopup_PlacementCallback(Size popupSize, Size targetSize, Point offset)
        {
            // Hiển thị ngay dưới NotificationButton
            double xOffset = (targetSize.Width - popupSize.Width) / 2;
            double yOffset = targetSize.Height;

            return new[]
            {
        new CustomPopupPlacement(new Point(xOffset, yOffset), PopupPrimaryAxis.Horizontal)
    };
        }


        private async void NotificationButton_Click(object sender, RoutedEventArgs e)
        {
            NotificationPopup.PlacementTarget = NotificationButton;
            NotificationPopup.IsOpen = true;
            NotificationListPanel.Children.Clear();

            var notifications = await _databaseService.GetNotificationsAsync(App.CurrentUser.Id);

            var grouped = notifications
                .Where(n => !n.IsRead)
                .GroupBy(n => n.IsGroup ? n.GroupId : n.From)
                .Select(g =>
                {
                    // Lấy thông tin group hoặc user theo key
                    var key = g.Key;
                    var isGroup = notifications.FirstOrDefault(n => (n.IsGroup ? n.GroupId : n.From) == key)?.IsGroup ?? false;

                    string displayName = key;
                    if (isGroup)
                    {
                        var group = _groups.FirstOrDefault(gr => gr.GroupId == key);
                        displayName = group?.Name ?? key;
                    }
                    else
                    {
                        var user = _users.FirstOrDefault(u => u.Id == key);
                        displayName = user?.DisplayName ?? key;
                    }

                    return new NotificationSummary
                    {
                        SenderId = key,
                        SenderName = displayName,
                        UnreadCount = g.Count()
                    };
                });

            foreach (var item in grouped)
            {
                var button = new Button
                {
                    Content = $"{item.SenderName}: {item.UnreadCount} tin nhắn",
                    Margin = new Thickness(0, 5, 0, 5),
                    Tag = item.SenderId,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                button.Click += NotificationItem_Click;

                NotificationListPanel.Children.Add(button);
            }
        }


        private async Task<string> GetUserNameById(string id)
        {
            // Ưu tiên lấy từ cache (_users)
            var user = _users.FirstOrDefault(f => f.Id == id);
            if (user != null)
                return user.DisplayName;

            try
            {
                // Nếu không có, truy vấn Firestore
                var userDoc = await _databaseService.GetDb().Collection("users").Document(id).GetSnapshotAsync();
                if (userDoc.Exists)
                {
                    var displayName = userDoc.GetValue<string>("DisplayName");
                    return displayName ?? id;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi lấy user {id}: {ex.Message}");
            }

            // Fallback nếu không tìm được
            return id;
        }

        private async void NotificationItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string senderId)
            {
                // Tìm nhóm trước (để ưu tiên)
                var targetGroup = _groups.FirstOrDefault(g => g.GroupId == senderId);
                if (targetGroup != null)
                {
                    UserListBox.SelectedItem = targetGroup;
                }
                else
                {
                    // Nếu không phải group thì tìm user
                    var targetUser = _users.FirstOrDefault(u => u.Id == senderId);
                    if (targetUser != null)
                    {
                        UserListBox.SelectedItem = targetUser;
                    }
                }

                // Đánh dấu tất cả thông báo thuộc senderId (groupId hoặc userId) là đã đọc
                var notifications = await _databaseService.GetNotificationsAsync(App.CurrentUser.Id);

                var unreadNotifications = notifications.Where(n =>
                    !n.IsRead && ((n.IsGroup && n.GroupId == senderId) || (!n.IsGroup && n.From == senderId))
                );

                foreach (var notif in unreadNotifications)
                {
                    await _databaseService.MarkNotificationsAsReadAsync(App.CurrentUser.Id, notif.Id);
                }

                await RefreshNotificationAsync();
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

        private void GroupNameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (string.IsNullOrEmpty(tb.Text))
                tb.Text = tb.Tag.ToString();
            tb.Foreground = Brushes.Gray;
        }

        private void GroupNameTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb.Text == tb.Tag.ToString())
            {
                tb.Text = "";
                tb.Foreground = Brushes.Black;
            }
        }

        private void GroupNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                tb.Text = tb.Tag.ToString();
                tb.Foreground = Brushes.Gray;
            }
        }


        private async void CreateGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var groupName = GroupNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(groupName) || groupName == "Group Name...")
            {
                MessageBox.Show("Vui lòng nhập tên nhóm.");
                return;
            }

            var selectedUsers = GroupUserListBox.SelectedItems.Cast<UserData>().ToList();
            if (selectedUsers.Count == 0)
            {
                if (MessageBox.Show("Bạn chưa chọn thành viên nào. Nhóm sẽ chỉ có bạn là admin. Tiếp tục?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.No)
                    return;
            }

            var memberIds = selectedUsers.Select(u => u.Id).ToList();

            CreateGroupButton.IsEnabled = false;
            try
            {
                // (Tuỳ chọn) Kiểm tra nhóm trùng tên
                // var existingGroups = await _firestoreDb.Collection("groups")
                //     .WhereEqualTo("name", groupName).GetSnapshotAsync();
                // if (existingGroups.Count > 0)
                // {
                //     MessageBox.Show("Tên nhóm đã tồn tại, vui lòng chọn tên khác.");
                //     return;
                // }

                var groupId = await _databaseService.CreateGroupAsync(groupName, App.CurrentUser.Id, memberIds);
                MessageBox.Show($"Tạo nhóm thành công!\nID nhóm: {groupId}");
                await RefreshFriendsAndRequestsAsync();
                // (Tuỳ chọn mở chat nhóm hoặc cập nhật danh sách nhóm UI)

                //this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tạo nhóm thất bại: {ex.Message}");
            }
            finally
            {
                CreateGroupButton.IsEnabled = true;
            }
        }



        private void AddGroupsButton_Click(object sender, RoutedEventArgs e)
        {
            // Chuyển sang tab "Add Groups"
            if (TabControl != null && TabControl.Items.Count > 2)
            {
                // Tab thứ 3 (0 = Chat, 1 = AddFriends, 2 = AddGroups)
                TabControl.SelectedIndex = 2;
            }
        }
        private async void ThumbtackButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UserData user)
            {
                await _databaseService.ToggleFriendPriorityAsync(App.CurrentUser.Id, user.Id);
                await RefreshFriendsAndRequestsAsync();
            }
        }



        private async void BlockUserButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UserData user)
            {
                bool theyBlockedMe = await _databaseService.IsBlockingUser(user.Id, App.CurrentUser.Id);
                if (theyBlockedMe)
                {
                    MessageBox.Show(
                        $"Không thể thực hiện thao tác vì bạn đang bị {user.DisplayName} chặn.",
                        "Không thể chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool currentlyBlocking = user.IsBlocked;

                var result = MessageBox.Show(
                    $"Bạn có chắc muốn {(currentlyBlocking ? "bỏ chặn" : "chặn")} {user.DisplayName}?",
                    "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                // ✅ Thay đổi trạng thái block
                await _databaseService.SetBlockStatusAsync(App.CurrentUser.Id, user.Id, !currentlyBlocking);

                // ✅ Cập nhật UI
                user.IsBlocked = !currentlyBlocking;

                MessageBox.Show(
                    $"{(currentlyBlocking ? "Đã bỏ chặn" : "Đã chặn")} {user.DisplayName}.",
                    "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);

                // ✅ Gửi tin nhắn hệ thống
                string chatRoomId = _databaseService.GetChatRoomId(App.CurrentUser.Id, user.Id); // cần có hàm này
                string systemMessage = currentlyBlocking
                    ? $"{App.CurrentUser.DisplayName} đã bỏ chặn {user.DisplayName}."
                    : $"{user.DisplayName} đã bị chặn bởi {App.CurrentUser.DisplayName}.";

                await _databaseService.SendSystemMessageToChatAsync(chatRoomId, systemMessage);
            }
        }



        private void ChatTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Chuyển sang tab Chat
            TabControl.SelectedIndex = 0;
        }

        private bool _isUserProfileVisible = false;
        private bool _isShowingMyProfile = false;

        private async void Optional_Click(object sender, RoutedEventArgs e)
        {
            if (!_isUserProfileVisible)
            {
                if (_selectedGroup != null)
                {
                    await ShowGroupProfileAsync();
                }
                else if (_selectedUser != null)
                {
                    ShowUserProfile(_selectedUser);
                }
                else
                {
                    ShowMyProfile();
                }

                _isUserProfileVisible = true;
                _isShowingMyProfile = false;
            }
            else if (!_isShowingMyProfile)
            {
                ShowMyProfile();
                _isShowingMyProfile = true;
            }
            else
            {
                await HideProfilePanelAsync();
                _isUserProfileVisible = false;
                _isShowingMyProfile = false;
            }
        }



        private async Task ShowGroupProfileAsync()
        {
            if (_selectedGroup == null) return;

            var adminName = await GetUserNameById(_selectedGroup.CreatedBy);

            ChatWithTextBlock.Text = $"Group: {_selectedGroup.Name}";
            GroupProfileName.Text = _selectedGroup.Name;
            GroupCreatedBy.Text = $"Admin: {adminName}";
            GroupMemberCount.Text = $"Members: {_selectedGroup.MemberCount} members";
            GroupProfileAvatar.Source = LoadAvatar(_selectedGroup.Avatar);

            UserProfilePanel.Visibility = Visibility.Collapsed;
            GroupProfilePanel.Visibility = Visibility.Visible;
            UserProfileColumn.Width = new GridLength(230);

            var showStoryboard = (Storyboard)this.Resources["SlideInUserProfile"];
            showStoryboard.Begin(GroupProfilePanel);

            _isUserProfileVisible = true;
            _isShowingMyProfile = false;
        }

        private void ShowUserProfile(UserData user)
        {
            ChatWithTextBlock.Text = $"Chat with {user.DisplayName}";
            ProfileUsername.Text = $"Username: {user.DisplayName}";
            ProfileEmail.Text = $"Email: {user.Email}";
            ProfileStatus.Text = $"Status: {(user.IsOnline ? "Online" : "Offline")}";
            ProfileAvatar.Source = LoadAvatar(user.Avatar);

            GroupProfilePanel.Visibility = Visibility.Collapsed;
            UserProfilePanel.Visibility = Visibility.Visible;
            UserProfileColumn.Width = new GridLength(230);
            EditUsernameButton.Visibility = Visibility.Collapsed;

            var showStoryboard = (Storyboard)this.Resources["SlideInUserProfile"];
            showStoryboard.Begin(UserProfilePanel);

            _isUserProfileVisible = true;
            _isShowingMyProfile = false;
        }

        private void ShowMyProfile()
        {
            var me = App.CurrentUser;

            ProfileUsername.Text = $"Username: {me.DisplayName}";
            ProfileEmail.Text = $"Email: {me.Email}";
            ProfileStatus.Text = $"Status: {(me.IsOnline ? "Online" : "Offline")}";
            ProfileAvatar.Source = LoadAvatar(me.Avatar);

            GroupProfilePanel.Visibility = Visibility.Collapsed;
            UserProfilePanel.Visibility = Visibility.Visible;
            UserProfileColumn.Width = new GridLength(230);

            var showStoryboard = (Storyboard)this.Resources["SlideInUserProfile"];
            showStoryboard.Begin(UserProfilePanel);

            EditUsernameButton.Visibility = Visibility.Visible; // ✅ Hiện nút edit tên
        }


        private async Task HideProfilePanelAsync()
        {
            var hideStoryboard = (Storyboard)this.Resources["SlideOutUserProfile"];
            hideStoryboard.Begin(UserProfilePanel);
            hideStoryboard.Begin(GroupProfilePanel);

            await Task.Delay(200);

            UserProfilePanel.Visibility = Visibility.Collapsed;
            GroupProfilePanel.Visibility = Visibility.Collapsed;
            UserProfileColumn.Width = new GridLength(0);

            _isUserProfileVisible = false;
            _isShowingMyProfile = false;
        }




        private bool isSearchVisible = false;

        private async void SearchButtonChat_Click(object sender, RoutedEventArgs e)
        {
            isSearchVisible = !isSearchVisible;
            SearchBoxContainer.Visibility = isSearchVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!isSearchVisible && SearchBoxContainer != null && ChatSearchTextBox != null)
            {
                ChatSearchTextBox.Text = ChatSearchTextBox.Tag?.ToString() ?? "Search messages...";
                ChatSearchTextBox.Foreground = Brushes.Gray;
                if (!string.IsNullOrEmpty(_currentChatRoomId))
                {
                    _messages.Clear();
                    MessagesStackPanel?.Children.Clear();
                    await LoadInitialMessagesAsync(_currentChatRoomId);
                }
            }
        }

        private async void ChatSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (ChatSearchTextBox == null)
                {
                    Console.WriteLine("ChatSearchTextBox is null");
                    return;
                }
                if (App.CurrentUser?.Id == null)
                {
                    Console.WriteLine("App.CurrentUser.Id is null");
                    return;
                }

                string query = ChatSearchTextBox.Text?.Trim() ?? string.Empty;
                string tagText = ChatSearchTextBox.Tag?.ToString() ?? string.Empty;
                if (query == tagText)
                {
                    query = string.Empty;
                }

                if (string.IsNullOrEmpty(_currentChatRoomId))
                {
                    MessagesStackPanel?.Children.Clear();
                    if (EmptyPromptTextBlock == null)
                    {
                        Console.WriteLine("EmptyPromptTextBlock is null at _currentChatRoomId check");
                    }
                    else
                    {
                        EmptyPromptTextBlock.Text = "Please select a chat room.";
                        EmptyPromptTextBlock.Visibility = Visibility.Visible;
                    }
                    return;
                }

                if (MessagesStackPanel != null) MessagesStackPanel.Children.Clear();
                _messages.Clear();

                if (string.IsNullOrEmpty(query))
                {
                    await LoadInitialMessagesAsync(_currentChatRoomId);
                    if (EmptyPromptTextBlock == null)
                    {
                        Console.WriteLine("EmptyPromptTextBlock is null at query empty check");
                    }
                    else
                    {
                        EmptyPromptTextBlock.Visibility = _messages.Any() ? Visibility.Collapsed : Visibility.Visible;
                    }
                    MessagesScrollViewer?.ScrollToEnd();
                    return;
                }

                var firebaseService = new FirebaseDatabaseService();
                var searchResults = await firebaseService.SearchMessagesSimpleAsync(_currentChatRoomId, query, App.CurrentUser.Id);

                if (MessagesStackPanel != null && searchResults != null)
                {
                    foreach (var message in searchResults)
                    {
                        await AddMessageToUI(message, query);
                    }
                }

                if (EmptyPromptTextBlock == null)
                {
                    Console.WriteLine("EmptyPromptTextBlock is null at search results check");
                }
                else
                {
                    EmptyPromptTextBlock.Text = searchResults?.Any() == true ? "Please choose someone to start" : "No messages found.";
                    EmptyPromptTextBlock.Visibility = searchResults?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
                }

                MessagesScrollViewer?.ScrollToEnd();
            }
            catch (Exception ex)
            {
                if (MessagesStackPanel != null) MessagesStackPanel.Children.Clear();
                if (EmptyPromptTextBlock == null)
                {
                    Console.WriteLine("EmptyPromptTextBlock is null in catch block");
                }
                else
                {
                    EmptyPromptTextBlock.Text = $"Error: {ex.Message}";
                    EmptyPromptTextBlock.Visibility = Visibility.Visible;
                }
                Console.WriteLine($"SearchError: {ex.Message}");
            }
        }

        private void ChatSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ChatSearchTextBox == null) return;
            if (ChatSearchTextBox.Text == (ChatSearchTextBox.Tag?.ToString() ?? string.Empty))
            {
                ChatSearchTextBox.Text = string.Empty;
                ChatSearchTextBox.Foreground = Brushes.Black;
            }
        }

        private void ChatSearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ChatSearchTextBox == null) return;
            if (string.IsNullOrEmpty(ChatSearchTextBox.Text))
            {
                ChatSearchTextBox.Text = ChatSearchTextBox.Tag?.ToString() ?? "Search messages...";
                ChatSearchTextBox.Foreground = Brushes.Gray;
            }
        }

        private async Task AddMessageToUI(MessageData message, string query = null)
        {
            try
            {
                if (message == null || string.IsNullOrEmpty(message.MessageId) || _messages.Any(m => m.MessageId == message.MessageId))
                    return;

                var isMine = message.SenderId == App.CurrentUser.Id;
                _messages.Add(message);

                bool isGroup = _selectedGroup != null;
                bool isSystemMessage = message.MessageType == "System";

                var stack = new StackPanel(); // chứa nội dung tin nhắn

                switch (message.MessageType)
                {
                    case "Text":
                        var textBlock = new TextBlock
                        {
                            FontSize = 16,
                            Foreground = Brushes.Black,
                            TextWrapping = TextWrapping.Wrap
                        };

                        // Highlight từ khóa nếu có
                        if (!string.IsNullOrEmpty(query) && !string.IsNullOrEmpty(message.Content))
                        {
                            string content = message.Content;
                            int index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                            if (index >= 0)
                            {
                                textBlock.Inlines.Add(content.Substring(0, index));
                                textBlock.Inlines.Add(new Run(content.Substring(index, query.Length)) { Background = Brushes.Yellow });
                                textBlock.Inlines.Add(content.Substring(index + query.Length));
                            }
                            else
                            {
                                textBlock.Text = content;
                            }
                        }
                        else
                        {
                            textBlock.Text = message.Content;
                        }
                        stack.Children.Add(textBlock);
                        break;

                    case "Image":
                        var image = new Image
                        {
                            Width = 200,
                            Height = 200,
                            HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
                        };
                        var binding = new Binding("FileUrl")
                        {
                            Source = message,
                            Converter = (IValueConverter)FindResource("ImageUrlConverter"),
                            FallbackValue = new BitmapImage(new Uri("pack://application:,,,/Icons/user.png", UriKind.Absolute))
                        };
                        image.SetBinding(Image.SourceProperty, binding);
                        stack.Children.Add(image);
                        break;

                    case "File":
                        if (string.IsNullOrEmpty(message.FileUrl) || !Uri.TryCreate(message.FileUrl, UriKind.Absolute, out var fileUri))
                            return;

                        var hyperlink = new Hyperlink
                        {
                            NavigateUri = fileUri,
                            Inlines = { new Run(message.Content ?? "Tệp đính kèm") }
                        };
                        hyperlink.RequestNavigate += (s, e) =>
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = message.FileUrl,
                                UseShellExecute = true
                            });
                        };
                        stack.Children.Add(new TextBlock
                        {
                            Inlines = { hyperlink },
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap
                        });
                        break;

                    case "Voice":
                        if (string.IsNullOrEmpty(message.FileUrl))
                        {
                            stack.Children.Add(new TextBlock { Text = "Lỗi: Tin nhắn thoại không khả dụng" });
                            break;
                        }

                        string tempFile = await DownloadToTempFileAsync(message.FileUrl, "wav");
                        if (string.IsNullOrEmpty(tempFile))
                        {
                            stack.Children.Add(new TextBlock { Text = "Lỗi: Không tải được tin nhắn thoại" });
                            break;
                        }

                        var playButton = new Button
                        {
                            Content = "Phát tin nhắn thoại",
                            Tag = tempFile,
                            Margin = new Thickness(5)
                        };
                        playButton.Click += (s, e) =>
                        {
                            try
                            {
                                var filePath = (string)((Button)s).Tag;
                                var audioFile = new AudioFileReader(filePath);
                                var outputDevice = new WaveOutEvent();
                                outputDevice.Init(audioFile);
                                outputDevice.Play();
                                outputDevice.PlaybackStopped += (snd, args) =>
                                {
                                    audioFile.Dispose();
                                    outputDevice.Dispose();
                                };
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Không thể phát tin nhắn thoại: {ex.Message}");
                            }
                        };
                        stack.Children.Add(playButton);
                        break;

                    case "Emoji":
                        try
                        {
                            var emojiPath = $"pack://application:,,,/Emoji/{message.Content}.png";
                            var emojiImage = new Image
                            {
                                Source = new BitmapImage(new Uri(emojiPath)),
                                Width = 40,
                                Height = 40,
                                Stretch = Stretch.Uniform
                            };
                            stack.Children.Add(emojiImage);
                        }
                        catch
                        {
                            stack.Children.Add(new TextBlock
                            {
                                Text = "[Không thể hiển thị emoji]",
                                Foreground = Brushes.Red,
                                FontSize = 14
                            });
                        }
                        break;
                }

                if (isSystemMessage)
                {
                    var systemText = new TextBlock
                    {
                        Text = message.Content,
                        FontSize = 14,
                        Foreground = Brushes.DarkSlateGray,
                        FontStyle = FontStyles.Italic,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(10)
                    };

                    var systemBorder = new Border
                    {
                        Background = Brushes.Transparent,
                        Padding = new Thickness(5),
                        Margin = new Thickness(5),
                        Child = systemText,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    MessagesStackPanel.Children.Add(systemBorder);
                    return;
                }

                // ==== Lấy thông tin người gửi ====
                string senderAvatar = "Icons/user.png";
                string senderName = "Unknown";

                if (isGroup)
                {
                    var senderUser = _users.FirstOrDefault(u => u.Id == message.SenderId)
                                  ?? _allUsers.FirstOrDefault(u => u.Id == message.SenderId);

                    if (senderUser != null)
                    {
                        senderAvatar = senderUser.Avatar;
                        senderName = senderUser.DisplayName;
                    }
                }
                else
                {
                    senderAvatar = isMine ? App.CurrentUser.Avatar : _selectedUser?.Avatar;
                    senderName = isMine ? App.CurrentUser.DisplayName : _selectedUser?.DisplayName;
                }

                // ==== Avatar ====
                var avatarImage = new Image
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Source = (ImageSource)new ImageUrlConverter().Convert(
                        string.IsNullOrEmpty(senderAvatar) ? "Icons/user.png" : senderAvatar,
                        typeof(ImageSource), null, null)
                };

                // ==== Hiển thị tên người gửi (nếu là nhóm) ====
                if (isGroup && !isMine)
                {
                    stack.Children.Insert(0, new TextBlock
                    {
                        Text = senderName,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5),
                        Foreground = Brushes.DarkSlateBlue
                    });
                }

                // ==== Thời gian gửi + trạng thái ====
                var time = message.Timestamp?.ToDateTime().ToLocalTime().ToShortTimeString() ?? "";
                stack.Children.Add(new TextBlock
                {
                    Text = time,
                    FontSize = 10,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0)
                });

                if (isMine && message.IsSeen)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "✔ Seen",
                        FontSize = 10,
                        Foreground = Brushes.Green,
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                }

                // ==== Border bọc nội dung ====
                var messageBorder = new Border
                {
                    Background = isMine ? Brushes.LightGreen : Brushes.White,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(5),
                    MaxWidth = 400,
                    Child = stack,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 5,
                        Opacity = 0.2,
                        ShadowDepth = 2
                    }
                };

                // ==== StackPanel ngang: avatar + tin nhắn ====
                var messageRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };

                if (!isMine)
                {
                    messageRow.Children.Add(avatarImage);
                    messageRow.Children.Add(messageBorder);
                }
                else
                {
                    messageRow.Children.Add(messageBorder);
                }

                // ==== Thêm vào UI ====
                MessagesStackPanel.Children.Add(messageRow);
                MessagesScrollViewer.ScrollToEnd();

                if (!message.IsSeen && message.ReceiverId == App.CurrentUser.Id)
                {
                    message.IsSeen = true;
                    await _databaseService.MarkMessageAsSeenAsync(_currentChatRoomId, message.MessageId);
                }

                _lastMessageTimestamp = message.Timestamp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi hiển thị tin nhắn: {ex.Message}");
            }
        }




        private void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            AttachOptionsPanel.IsOpen = !AttachOptionsPanel.IsOpen;
        }

        private async void ImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null && _selectedGroup == null)
            {
                MessageBox.Show("Hãy chọn người dùng hoặc nhóm để gửi ảnh.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_selectedUser != null)
            {
                bool isBlocked = await _databaseService.IsBlockedBetweenUsers(App.CurrentUser.Id, _selectedUser.Id);
                if (isBlocked)
                {
                    MessageBox.Show("Bạn không thể gửi ảnh vì đã bị chặn hoặc đã chặn người này.", "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
                        MessageBox.Show("Tải ảnh lên thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var timestamp = Timestamp.GetCurrentTimestamp();
                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser?.Id,
                        Content = "Đã gửi một ảnh",
                        Timestamp = timestamp,
                        MessageType = "Image",
                        FileUrl = imageUrl
                    };                    
                    await AddMessageToUI(message);


                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);  

                    AttachOptionsPanel.Visibility = Visibility.Collapsed;

                    if (_selectedUser != null)
                        await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, "Image");

                    await RefreshNotificationAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi ảnh thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null && _selectedGroup == null)
            {
                MessageBox.Show("Hãy chọn người dùng hoặc nhóm để gửi file.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_selectedUser != null)
            {
                bool isBlocked = await _databaseService.IsBlockedBetweenUsers(App.CurrentUser.Id, _selectedUser.Id);
                if (isBlocked)
                {
                    MessageBox.Show("Bạn không thể gửi file vì đã bị chặn hoặc đã chặn người này.", "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
                        MessageBox.Show("Tải file lên thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var timestamp = Timestamp.GetCurrentTimestamp();
                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser?.Id,
                        Content = $"Đã gửi file: {fileName}",
                        Timestamp = timestamp,
                        MessageType = "File",
                        FileUrl = fileUrl,
                        FileName = fileName
                    };
                    AddMessageToUI(message);
                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                    AttachOptionsPanel.Visibility = Visibility.Collapsed;

                    if (_selectedUser != null)
                        await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, "File");

                    await RefreshNotificationAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi file thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void VoiceRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null && _selectedGroup == null)
            {
                MessageBox.Show("Hãy chọn người dùng hoặc nhóm để gửi tin nhắn thoại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_selectedUser != null)
            {
                bool isBlocked = await _databaseService.IsBlockedBetweenUsers(App.CurrentUser.Id, _selectedUser.Id);
                if (isBlocked)
                {
                    MessageBox.Show("Bạn không thể gửi tin nhắn thoại vì đã bị chặn hoặc đã chặn người này.", "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
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
                MessageBox.Show("Đang ghi âm... Nhấn OK để dừng.", "Ghi âm", MessageBoxButton.OK);
                waveIn.StopRecording();
                writer.Close();
                waveIn.Dispose();

                string voiceUrl = await _databaseService.UploadFileToS3Async(tempFilePath, "voice");
                if (string.IsNullOrEmpty(voiceUrl))
                {
                    MessageBox.Show("Tải tin nhắn thoại thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var message = new MessageData
                {
                    SenderId = App.CurrentUser.Id,
                    ReceiverId = _selectedUser?.Id,
                    Content = "[Voice]",
                    Timestamp = Timestamp.GetCurrentTimestamp(),
                    MessageType = "Voice",
                    FileUrl = voiceUrl,
                    IsSeen = false
                };

                // ❌ KHÔNG gọi AddMessageToUI ở đây
                await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);

                File.Delete(tempFilePath);
                AttachOptionsPanel.Visibility = Visibility.Collapsed;

                if (_selectedUser != null)
                    await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, "Voice");

                await RefreshNotificationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi tin nhắn thoại thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }





        private void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
        }


        private async void EmojiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null && _selectedGroup == null)
            {
                MessageBox.Show("Hãy chọn người dùng hoặc nhóm để gửi emoji.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_selectedUser != null)
            {
                bool isBlocked = await _databaseService.IsBlockedBetweenUsers(App.CurrentUser.Id, _selectedUser.Id);
                if (isBlocked)
                {
                    MessageBox.Show("Bạn không thể gửi emoji vì đã bị chặn hoặc đã chặn người này.", "Bị chặn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }


            try
            {
                var button = sender as Button;
                string emojiKey = button?.Tag as string;
                if (!string.IsNullOrEmpty(emojiKey))
                {
                    var timestamp = Timestamp.GetCurrentTimestamp();
                    var message = new MessageData
                    {
                        SenderId = App.CurrentUser.Id,
                        ReceiverId = _selectedUser?.Id,
                        Content = emojiKey,
                        Timestamp = timestamp,
                        MessageType = "Emoji"
                    };

                    await _databaseService.SaveMessageAsync(_currentChatRoomId, message, _idToken);
                    EmojiPopup.IsOpen = false;

                    if (_selectedUser != null)
                        await _databaseService.SendNotificationAsync(_selectedUser.Id, App.CurrentUser.Id, "Emoji");

                    await RefreshNotificationAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gửi emoji thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    // await RefreshGroupsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AllUsersListBox.ItemsSource = null;
                        //AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng
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
                    // await RefreshGroupsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        FriendRequestsListBox.ItemsSource = null;
                        FriendRequestsListBox.ItemsSource = _friendRequests; // Làm mới danh sách lời mời
                        UserListBox.ItemsSource = null;
                        UserListBox.ItemsSource = _users; // Làm mới danh sách bạn bè
                        AllUsersListBox.ItemsSource = null;
                        //AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng

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
                    // await RefreshGroupsAsync();
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
                    // await RefreshGroupsAsync();
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AllUsersListBox.ItemsSource = null;
                        //AllUsersListBox.ItemsSource = _allUsers; // Làm mới danh sách người dùng
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to cancel friend request: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser != null)
            {
                var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa {_selectedUser.DisplayName} khỏi danh sách bạn bè không?",
                    "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _databaseService.RemoveFriendAsyncAndDeleteMessages(App.CurrentUser.Id, _selectedUser.Id);
                        await RefreshFriendsAndRequestsAsync(); // Cập nhật lại UI
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Xóa bạn thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }              

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                // Xóa nội dung tìm kiếm khi chuyển tab
                SearchTextBox.Text = string.Empty;

                // Làm mới danh sách dựa trên tab hiện tại
                if (TabControl.SelectedIndex == 0) // Tab Chat
                {
                    UserListBox.ItemsSource = _chatrooms;
                }
                else if (TabControl.SelectedIndex == 1) // Tab Add Friends
                {
                    //AllUsersListBox.ItemsSource = _allUsers;
                }
                else if (TabControl.SelectedIndex == 2)
                {
                    GroupUserListBox.ItemsSource = _users;
                }

                // Đảm bảo giao diện cập nhật
                SearchTextBox_TextChanged(null, null);
            }
        }

        private async void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedUser == null) return;

            if (!_isTyping)
            {
                _isTyping = true;
                await SetTypingStatusAsync(true);
            }

            if (_typingTimer == null)
            {
                _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _typingTimer.Tick += async (s, args) =>
                {
                    _typingTimer.Stop();
                    _isTyping = false;
                    await SetTypingStatusAsync(false);
                };
            }

            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private List<string> _previousGroupIds = new List<string>();

        private async void StartListeningToAllGroupChanges()
        {
            await _databaseService.StopListeningToUserRelatedGroupsAsync();
            await _databaseService.StopListeningToEachUserGroupAsync();

            _databaseService.ListenToUserRelatedGroupsAsync(App.CurrentUser.Id, async updatedGroups =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    string selectedGroupId = _selectedGroup?.GroupId;

                    // 🔁 Cập nhật danh sách nhóm
                    foreach (var updatedGroup in updatedGroups)
                    {
                        var existing = _groups.FirstOrDefault(g => g.GroupId == updatedGroup.GroupId);
                        if (existing == null)
                        {
                            // 👇 Gán AvatarBitmap trước khi thêm
                            if (!string.IsNullOrEmpty(updatedGroup.Avatar))
                            {
                                var bitmap = await DownloadImageSafelyAsync(updatedGroup.Avatar);
                                if (bitmap != null)
                                    updatedGroup.AvatarBitmap = bitmap;
                            }

                            // ✅ Chỉ thêm vào khi ảnh đã sẵn sàng (hoặc không có ảnh)
                            if (updatedGroup.AvatarBitmap != null || string.IsNullOrEmpty(updatedGroup.Avatar))
                            {
                                _groups.Add(updatedGroup);
                            }
                        }
                        else
                        {
                            // Cập nhật thuộc tính nếu đã có
                            existing.Name = updatedGroup.Name;
                            existing.MemberCount = updatedGroup.MemberCount;

                            if (!string.IsNullOrEmpty(updatedGroup.Avatar) && updatedGroup.Avatar != existing.Avatar)
                            {
                                var bitmap = await DownloadImageSafelyAsync(updatedGroup.Avatar);
                                if (bitmap != null)
                                {
                                    existing.Avatar = updatedGroup.Avatar;
                                    existing.AvatarBitmap = bitmap;
                                }
                            }
                        }
                    }


                    // Loại bỏ những group không còn nữa
                    var toRemove = _groups.Where(g => !updatedGroups.Any(ug => ug.GroupId == g.GroupId)).ToList();
                    foreach (var g in toRemove)
                        _groups.Remove(g);


                    UpdateChatroomList();

                    // 🔁 Trì hoãn gán lại selectedGroup khi UI đã binding lại
                    await Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (!string.IsNullOrEmpty(selectedGroupId))
                        {
                            var matched = _groups.FirstOrDefault(g => g.GroupId == selectedGroupId);
                            if (matched != null && !ReferenceEquals(_selectedGroup, matched))
                            {
                                _selectedGroup = matched;
                                await RefreshGroupUIAsync();
                                await UpdateGroupHeaderAsync();
                            }
                        }

                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);

                    // 🔁 Gắn lại các listener nếu GroupId thay đổi
                    var newGroupIds = updatedGroups.Select(g => g.GroupId).ToList();
                    if (!_previousGroupIds.SequenceEqual(newGroupIds))
                    {
                        _previousGroupIds = newGroupIds;

                        await _databaseService.ListenToEachUserGroupAsync(newGroupIds, async updatedGroup =>
                        {
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var existing = _groups.FirstOrDefault(g => g.GroupId == updatedGroup.GroupId);
                                if (existing != null)
                                {
                                    existing.Name = updatedGroup.Name;

                                    if (!string.IsNullOrEmpty(updatedGroup.Avatar))
                                    {
                                        var bitmap = await DownloadImageSafelyAsync(updatedGroup.Avatar);
                                        if (bitmap != null)
                                        {
                                            existing.Avatar = updatedGroup.Avatar;
                                            existing.AvatarBitmap = bitmap;
                                        }
                                    }
                                }

                                // Nếu là nhóm đang chọn
                                if (_selectedGroup?.GroupId == updatedGroup.GroupId)
                                {
                                    _selectedGroup.Name = updatedGroup.Name;

                                    if (!string.IsNullOrEmpty(updatedGroup.Avatar))
                                    {
                                        var bitmap = await DownloadImageSafelyAsync(updatedGroup.Avatar);
                                        if (bitmap != null)
                                            _selectedGroup.Avatar = updatedGroup.Avatar;
                                    }

                                    await RefreshGroupUIAsync();
                                    await UpdateGroupHeaderAsync();
                                }
                            });
                        });
                    }
                });
            });
        }





        private async Task<BitmapImage> DownloadImageSafelyAsync(string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Cho phép dùng trên UI thread khác
                    return bitmap;
                }
                catch
                {
                    await Task.Delay(1000); // Retry sau 1s
                }
            }

            return null;
        }




        private async Task RefreshGroupUIAsync()
        {
            // 1. Lấy lại dữ liệu nhóm
            if (_selectedGroup == null)
            {
                Console.WriteLine("⚠️ _selectedGroup is null, skipping RefreshGroupUIAsync.");
                return;
            }

            _selectedGroup = await _databaseService.GetGroupAsync(_selectedGroup.GroupId);            

            // 2. Cập nhật danh sách thành viên
            var memberIds = _selectedGroup.Members?.Keys.ToList() ?? new List<string>();
            var members = await _databaseService.GetUsersByIdsAsync(memberIds);
            GroupMembersList.ItemsSource = members;
            GroupMembersList.Visibility = members.Any() ? Visibility.Visible : Visibility.Collapsed;

            // 3. Nếu là admin thì cập nhật danh sách pending và danh sách bạn bè có thể mời
            bool isAdmin = false;
            if (_selectedGroup?.Members != null &&
                _selectedGroup.Members.TryGetValue(App.CurrentUser.Id, out var role))
            {
                isAdmin = role == "admin";
            }
            if (isAdmin)
            {
                // 3.1 Cập nhật danh sách thành viên chờ duyệt
                var pendingIds = _selectedGroup.PendingMembers?.Keys.ToList() ?? new List<string>();
                var pendingUsers = await _databaseService.GetUsersByIdsAsync(pendingIds);
                var pendingModels = pendingUsers.Select(u => new PendingMemberModel
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName
                }).ToList();

                PendingMembersListBox.ItemsSource = pendingModels;

                bool hasPending = pendingModels.Any();
                PendingMembersLabel.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
                PendingMembersListBox.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
                ApproveSelectedButton.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
                RejectSelectedButton.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;

                // ✅ 3.2 Cập nhật lại danh sách bạn bè có thể mời
                var availableFriends = await LoadAvailableFriendsAsync();
                FriendCheckboxList.ItemsSource = availableFriends;
            }
            else
            {
                PendingMembersLabel.Visibility = Visibility.Collapsed;
                PendingMembersListBox.Visibility = Visibility.Collapsed;
                ApproveSelectedButton.Visibility = Visibility.Collapsed;
                RejectSelectedButton.Visibility = Visibility.Collapsed;
            }
        }


        private async Task UpdateGroupHeaderAsync()
        {
            if (_selectedGroup == null)
            {
                ChatWithTextBlock.Text = "[No Group]";
                GroupCreatedBy.Text = "";
                GroupMemberCount.Text = "Members: 0 members";
                GroupProfileAvatar.Source = null;
                GroupProfileName.Text = "";
                return;
            }

            // ✅ Tên nhóm
            ChatWithTextBlock.Text = _selectedGroup.Name ?? "[No Name]";
            GroupProfileName.Text = _selectedGroup.Name ?? "[No Name]"; // <- Cần bổ sung dòng này

            // ✅ Admin
            if (!string.IsNullOrEmpty(_selectedGroup.CreatedBy))
            {
                var adminName = await GetUserNameById(_selectedGroup.CreatedBy);
                GroupCreatedBy.Text = $"Admin: {adminName}";
            }
            else
            {
                GroupCreatedBy.Text = "Admin: [Unknown]";
            }

            // ✅ Số lượng thành viên
            int memberCount = _selectedGroup.Members?.Count ?? 0;
            GroupMemberCount.Text = $"Members: {memberCount} members";

            // ✅ Avatar nhóm
            try
            {
                GroupProfileAvatar.Source = LoadAvatar(_selectedGroup.Avatar);
            }
            catch
            {
                GroupProfileAvatar.Source = null;
            }
        }





        private void ToggleVisibility(UIElement element, bool visible)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }


        private async void ViewMembersButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null)
            {
                MessageBox.Show("No group selected.");
                return;
            }

            bool isVisible = GroupMembersList.Visibility == Visibility.Visible || PendingMembersListBox.Visibility == Visibility.Visible;

            if (isVisible)
            {
                GroupMembersList.ItemsSource = null;
                PendingMembersListBox.ItemsSource = null;

                ToggleVisibility(GroupMembersList, false);
                ToggleVisibility(PendingMembersListBox, false);
                ToggleVisibility(PendingMembersLabel, false);
                ToggleVisibility(ApproveSelectedButton, false);
                ToggleVisibility(RejectSelectedButton, false);
                return;
            }

            await RefreshGroupUIAsync();
        }

        private async void RemoveMemberButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string userId)
            {
                bool isAdmin = _selectedGroup.Members.TryGetValue(App.CurrentUser.Id, out var role) && role == "admin";
                if (!isAdmin)
                {
                    MessageBox.Show("Chỉ admin mới có quyền xoá thành viên khỏi nhóm.");
                    return;
                }

                if (userId == App.CurrentUser.Id)
                {
                    MessageBox.Show("Bạn không thể tự xoá chính mình khỏi nhóm.");
                    return;
                }



                var result = MessageBox.Show("Bạn có chắc muốn xoá thành viên này khỏi nhóm?",
                                             "Xác nhận xoá",
                                             MessageBoxButton.YesNo,
                                             MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;

                try
                {
                    await _databaseService.RemoveMemberFromGroupAsync(_selectedGroup.GroupId, userId);
                    MessageBox.Show("Đã xoá thành viên.");
                    var displayName = await GetUserNameById(userId);
                    await _databaseService.SendSystemMessageToChatAsync(
                        _selectedGroup.GroupId,
                        $"{displayName} đã bị xoá khỏi nhóm bởi {App.CurrentUser.DisplayName}."
                    );                    
                    await RefreshGroupUIAsync();
                    UpdateGroupHeaderAsync();                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi xoá thành viên: " + ex.Message);
                }
            }
        }



        private async void ApproveSelectedPendingMembers_Click(object sender, RoutedEventArgs e)
        {
            if (PendingMembersListBox.ItemsSource is IEnumerable<PendingMemberModel> pendingMembers)
            {
                var selectedMembers = pendingMembers.Where(m => m.IsSelected).ToList();
                if (!selectedMembers.Any())
                {
                    MessageBox.Show("Vui lòng chọn ít nhất một thành viên để duyệt.");
                    return;
                }

                try
                {
                    foreach (var member in selectedMembers)
                    {
                        await _databaseService.ApproveMemberAsync(_selectedGroup.GroupId, member.Id);
                        await _databaseService.SendSystemMessageToChatAsync(
                            _selectedGroup.GroupId,
                            $"{member.DisplayName} đã được duyệt vào nhóm bởi {App.CurrentUser.DisplayName}."
                        );
                    }
                    MessageBox.Show($"Đã duyệt {selectedMembers.Count} thành viên.");
                    await RefreshGroupUIAsync();
                    UpdateGroupHeaderAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi duyệt thành viên: " + ex.Message);
                }
            }
        }


        private async void RejectSelectedPendingMembers_Click(object sender, RoutedEventArgs e)
        {
            if (PendingMembersListBox.ItemsSource is IEnumerable<PendingMemberModel> pendingMembers)
            {
                var selectedMembers = pendingMembers.Where(m => m.IsSelected).ToList();
                if (!selectedMembers.Any())
                {
                    MessageBox.Show("Vui lòng chọn ít nhất một thành viên để từ chối.");
                    return;
                }

                try
                {
                    foreach (var member in selectedMembers)
                    {
                        await _databaseService.RejectMemberAsync(_selectedGroup.GroupId, member.Id);
                        await _databaseService.SendSystemMessageToChatAsync(
                            _selectedGroup.GroupId,
                            $"{member.DisplayName} đã bị từ chối vào nhóm bởi {App.CurrentUser.DisplayName}."
                        );
                    }
                    MessageBox.Show($"Đã từ chối {selectedMembers.Count} thành viên.");
                    await RefreshGroupUIAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi từ chối thành viên: " + ex.Message);
                }
            }
        }


        private async Task<List<InviteFriendModel>> LoadAvailableFriendsAsync()
        {
            var friends = _users;
            var existingMembers = await _databaseService.GetGroupMembersAsync(_selectedGroup.GroupId);
            var pendingMembers = await _databaseService.GetGroupPendingMembersAsync(_selectedGroup.GroupId);

            var availableFriends = friends
                .Where(f => !existingMembers.Contains(f.Id) && !pendingMembers.Contains(f.Id))
                .Select(f => new InviteFriendModel
                {
                    Id = f.Id,
                    DisplayName = f.DisplayName
                })
                .ToList();

            return availableFriends;
        }



        private async void InviteMemberButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = FriendCheckboxList.Visibility == Visibility.Visible;

            // Toggle visibility
            FriendCheckboxList.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            ConfirmInviteButton.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            InviteFriendLabel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;

            if (isVisible) return;

            try
            {
                var availableFriends = await LoadAvailableFriendsAsync();
                FriendCheckboxList.ItemsSource = availableFriends;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải danh sách bạn bè: " + ex.Message);
            }
        }




        private async void ConfirmInvite_Click(object sender, RoutedEventArgs e)
        {
            var groupId = _selectedGroup.GroupId;
            var inviterId = App.CurrentUser.Id;

            var selectedFriends = FriendCheckboxList.Items
                .Cast<InviteFriendModel>()
                .Where(f => f.IsSelected)
                .ToList();

            int invitedCount = 0;

            foreach (var friend in selectedFriends)
            {
                try
                {
                    await _databaseService.InviteMemberToGroupAsync(groupId, inviterId, friend.Id);
                    invitedCount++;                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi mời {friend.DisplayName}: {ex.Message}");
                }
            }

            MessageBox.Show($"Đã mời {invitedCount} người!");

            // Làm mới danh sách bạn bè có thể mời
            var updatedFriends = await LoadAvailableFriendsAsync();
            FriendCheckboxList.ItemsSource = updatedFriends;

            // Nếu người mời là admin, cập nhật lại thông tin nhóm và member count
            if (_selectedGroup.Members.TryGetValue(inviterId, out var role) && role == "admin")
            {
                //_selectedGroup = await _databaseService.GetGroupAsync(groupId); // lấy lại dữ liệu nhóm mới                
                await RefreshGroupUIAsync();
                UpdateGroupHeaderAsync();
            }
        }







        private async void LeaveGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null)
            {
                MessageBox.Show("Không có nhóm nào được chọn.");
                return;
            }

            var currentUserId = App.CurrentUser.Id;
            var groupId = _selectedGroup.GroupId;

            // Cập nhật lại dữ liệu nhóm mới nhất từ Firestore
            var latestGroup = await _databaseService.GetGroupAsync(groupId);
            if (latestGroup == null || latestGroup.Members == null)
            {
                MessageBox.Show("Không thể lấy thông tin nhóm.");
                return;
            }

            bool isAdmin = latestGroup.Members.TryGetValue(currentUserId, out var role) && role == "admin";

            if (isAdmin)
            {
                if (latestGroup.Members.Count <= 1)
                {
                    MessageBox.Show("Bạn là thành viên duy nhất. Không thể rời nhóm.");
                    return;
                }

                // Danh sách các thành viên còn lại
                var otherMembers = latestGroup.Members
                    .Where(kvp => kvp.Key != currentUserId)
                    .ToList();

                var users = await _databaseService.GetUsersByIdsAsync(otherMembers.Select(m => m.Key).ToList());
                var memberOptions = users.ToDictionary(u => u.Id, u => u.DisplayName);

                // Tạo popup chọn admin mới
                var window = new Window
                {
                    Title = "Chọn admin mới",
                    Width = 300,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = Application.Current.MainWindow
                };

                var stack = new StackPanel { Margin = new Thickness(10) };
                stack.Children.Add(new TextBlock
                {
                    Text = "Chọn thành viên để chuyển quyền admin:",
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var comboBox = new ComboBox
                {
                    ItemsSource = memberOptions,
                    DisplayMemberPath = "Value",
                    SelectedValuePath = "Key",
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stack.Children.Add(comboBox);

                var confirmButton = new Button
                {
                    Content = "Xác nhận",
                    Height = 30,
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                confirmButton.Click += (s, args) =>
                {
                    if (comboBox.SelectedValue is string newAdminId)
                    {
                        window.Tag = newAdminId;
                        window.DialogResult = true;
                        window.Close();
                    }
                    else
                    {
                        MessageBox.Show("Vui lòng chọn một thành viên.");
                    }
                };
                stack.Children.Add(confirmButton);

                window.Content = stack;

                if (window.ShowDialog() == true && window.Tag is string selectedAdminId)
                {
                    try
                    {
                        await _databaseService.ChangeGroupAdminAsync(groupId, currentUserId, selectedAdminId);
                        await _databaseService.RemoveMemberFromGroupAsync(groupId, currentUserId);

                        var newAdminName = users.FirstOrDefault(u => u.Id == selectedAdminId)?.DisplayName ?? "[Người được chọn]";
                        await _databaseService.SendSystemMessageToChatAsync(
                            groupId,
                            $"{App.CurrentUser.DisplayName} đã rời nhóm và chuyển quyền admin cho {newAdminName}."
                        );

                        MessageBox.Show("Bạn đã rời nhóm và chuyển quyền admin thành công.");
                        _selectedGroup = null;

                        await RefreshGroupUIAsync();
                        await RefreshFriendsAndRequestsAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi khi rời nhóm: " + ex.Message);
                    }
                }
            }
            else
            {
                var confirm = MessageBox.Show("Bạn có chắc muốn rời nhóm?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    await _databaseService.RemoveMemberFromGroupAsync(groupId, currentUserId);
                    await _databaseService.SendSystemMessageToChatAsync(
                        groupId,
                        $"{App.CurrentUser.DisplayName} đã rời nhóm."
                    );

                    MessageBox.Show("Bạn đã rời nhóm.");
                    _selectedGroup = null;

                    await RefreshGroupUIAsync();
                    await RefreshFriendsAndRequestsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi rời nhóm: " + ex.Message);
                }
            }
        }


        private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null)
            {
                MessageBox.Show("Không có nhóm nào được chọn.");
                return;
            }

            var currentUserId = App.CurrentUser.Id;            

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa nhóm \"{_selectedGroup.Name}\"? Tất cả tin nhắn và dữ liệu sẽ bị xoá vĩnh viễn.",
                "Xác nhận xóa nhóm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await _databaseService.DeleteGroupAsync(_selectedGroup.GroupId);

                MessageBox.Show("Đã xóa nhóm thành công.");

                _selectedGroup = null;
                ResetChatUI();
                await RefreshGroupUIAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi xóa nhóm: " + ex.Message);
            }
        }        





        private async void UpdateChatroomList()
        {
            var newChatrooms = new ObservableCollection<object>(_users.Cast<object>().Concat(_groups));

            // 🔍 So sánh toàn bộ danh sách mới và cũ
            if (!_chatrooms.SequenceEqual(newChatrooms, new ChatroomComparer()))
            {
                _chatrooms.Clear();
                foreach (var chatroom in newChatrooms)
                    _chatrooms.Add(chatroom);
            }

            // 👉 Đảm bảo binding vẫn đúng
            if (UserListBox.ItemsSource != _chatrooms)
                UserListBox.ItemsSource = _chatrooms;
        }

        //private async void StartCallButton_Click(object sender, RoutedEventArgs e)
        //{
        //    if (_selectedUser == null)
        //    {
        //        MessageBox.Show("Vui lòng chọn một người bạn để bắt đầu cuộc gọi.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        //        return;
        //    }

        //    if (AgoraService.Instance.IsInCall)
        //    {
        //        MessageBox.Show("Bạn đang trong một cuộc gọi khác.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        //        return;
        //    }

        //    // ✅ Thêm kiểm tra thiết bị
        //    if (!AgoraService.Instance.HasRequiredDevices())
        //    {
        //        MessageBox.Show("Không tìm thấy camera hoặc microphone. Vui lòng kiểm tra lại thiết bị của bạn.", "Lỗi Thiết Bị", MessageBoxButton.OK, MessageBoxImage.Error);
        //        return;
        //    }

        //    // ✅ Bọc toàn bộ logic trong try-catch để xử lý lỗi
        //    try
        //    {
        //        var callData = new CallData
        //        {
        //            ChannelName = Guid.NewGuid().ToString(),
        //            CallerId = App.CurrentUser.Id,
        //            CallerName = App.CurrentUser.DisplayName,
        //            ReceiverId = _selectedUser.Id,
        //            Status = "calling" // Trạng thái ban đầu
        //        };

        //        // ✅ Chờ cho việc lưu vào Firebase hoàn tất
        //        await _databaseService.InitiateCallAsync(callData);

        //        // Mở cửa sổ cuộc gọi
        //        CallWindow callWindow = new CallWindow(callData, true); // true = người gọi
        //        callWindow.Show();
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Lỗi khi bắt đầu cuộc gọi: {ex.Message}");
        //        MessageBox.Show($"Không thể bắt đầu cuộc gọi. Vui lòng kiểm tra kết nối mạng và thử lại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}

        private void EditGroupNameButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle edit mode for group name
            GroupProfileName.Visibility = Visibility.Collapsed;
            ProfileGroupTextBox.Visibility = Visibility.Visible;

            // Set text and focus
            ProfileGroupTextBox.Text = GroupProfileName.Text;
            ProfileGroupTextBox.Focus();
            ProfileGroupTextBox.SelectAll();
        }

        private void GroupnameEditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Save on Enter
                SaveGroupName();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Cancel on Escape
                CancelGroupNameEdit();
                e.Handled = true;
            }
        }

        private void GroupnameEditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Auto-save when focus is lost
            SaveGroupName();
        }

        private async void SaveGroupName()
        {
            string newName = ProfileGroupTextBox.Text.Trim();

            // 🔒 Kiểm tra rỗng/null
            if (_selectedGroup == null || string.IsNullOrEmpty(newName) || newName == _selectedGroup.Name)
            {
                GroupProfileName.Visibility = Visibility.Visible;
                ProfileGroupTextBox.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                SetLoadingState(true);

                // ✅ Cập nhật trên Firestore
                await _databaseService.UpdateGroupNameAsync(_selectedGroup.GroupId, newName);

                // ✅ Cập nhật UI
                _selectedGroup.Name = newName;
                GroupProfileName.Text = newName;
                ChatWithTextBlock.Text = $"Group: {newName}";

                await _databaseService.SendSystemMessageToChatAsync(
                    _selectedGroup.GroupId,
                    $"📝 Đã đổi tên nhóm thành '{newName}'"
                );

                await UpdateGroupHeaderAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi cập nhật tên nhóm: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
                GroupProfileName.Visibility = Visibility.Visible;
                ProfileGroupTextBox.Visibility = Visibility.Collapsed;
            }
        }



        private void CancelGroupNameEdit()
        {
            // Reset without saving
            GroupProfileName.Visibility = Visibility.Visible;
            ProfileGroupTextBox.Visibility = Visibility.Collapsed;
        }

        private async void GroupProfileAvatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectedGroup == null) return;

            bool isAdmin = _selectedGroup.Members.TryGetValue(App.CurrentUser.Id, out var role) &&
                           role.Equals("admin", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin)
            {
                MessageBox.Show("Chỉ admin mới có quyền đổi ảnh nhóm");
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png",
                Title = "Chọn ảnh đại diện nhóm"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    SetLoadingState(true);

                    // ✅ Upload ảnh lên S3
                    string newAvatarUrl = await _databaseService.UploadFileToS3Async(
                        openFileDialog.FileName,
                        "group-avatars"
                    );

                    // ✅ Cập nhật Firestore
                    await _databaseService.UpdateGroupAvatarAsync(_selectedGroup.GroupId, newAvatarUrl);

                    // ✅ Cập nhật local
                    _selectedGroup.Avatar = newAvatarUrl;

                    // ✅ Hiển thị ảnh mới ngay
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(newAvatarUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    GroupProfileAvatar.Source = bitmap;

                    // ✅ Gửi thông báo hệ thống
                    await _databaseService.SendSystemMessageToChatAsync(
                        _selectedGroup.GroupId,
                        "🖼️ Đã cập nhật ảnh đại diện nhóm"
                    );

                    // ✅ Cập nhật UI thêm nếu cần
                    await UpdateGroupHeaderAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi cập nhật ảnh đại diện: {ex.Message}");
                }
                finally
                {
                    SetLoadingState(false);
                }
            }
        }


        private void SetLoadingState(bool isLoading)
        {
            GroupProfileAvatar.IsEnabled = !isLoading;

        }

        private void EditUsernameButton_Click(object sender, RoutedEventArgs e)
        {
            // Tách chỉ tên người dùng từ TextBlock
            string currentText = ProfileUsername.Text;
            string userName = currentText.Replace("Username: ", ""); // Loại bỏ phần tiền tố

            // Toggle edit mode for username
            ProfileUsername.Visibility = Visibility.Collapsed;
            ProfileUsernameTextBox.Visibility = Visibility.Visible;

            // Set text and focus - chỉ chứa tên người dùng
            ProfileUsernameTextBox.Text = userName;
            ProfileUsernameTextBox.Focus();
            ProfileUsernameTextBox.SelectAll();
        }

        private void UsernameEditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveUsername();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelUsernameEdit();
                e.Handled = true;
            }
        }

        private void UsernameEditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveUsername();
        }

        private void SaveUsername()
        {
            string newName = ProfileUsernameTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(newName))
            {
                // Cập nhật tên mới cho người dùng
                App.CurrentUser.DisplayName = newName;

                // Update UI với định dạng đầy đủ
                ProfileUsername.Text = $"Username: {newName}";
                UsernameTextBlock.Text = newName; // Cập nhật tên trên header

                // Lưu vào database
                _ = _databaseService.SaveUserAsync(App.IdToken, App.CurrentUser);
            }

            // Reset UI
            ProfileUsername.Visibility = Visibility.Visible;
            ProfileUsernameTextBox.Visibility = Visibility.Collapsed;
        }

        private void CancelUsernameEdit()
        {
            ProfileUsername.Visibility = Visibility.Visible;
            ProfileUsernameTextBox.Visibility = Visibility.Collapsed;
        }

        private class ImageViewerWindow : Window
        {
            public ImageViewerWindow(string imageUrl)
            {
                // Thiết lập cửa sổ
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
                Background = Brushes.Black;
                Topmost = true;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                // Tạo Image control
                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                try
                {
                    // Tải ảnh chất lượng cao
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imageUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    image.Source = bitmap;
                }
                catch
                {
                    // Xử lý lỗi tải ảnh
                    image.Source = new BitmapImage(new Uri("pack://application:,,,/Icons/error.png"));
                }

                // Click để đóng cửa sổ
                image.MouseLeftButtonDown += (s, e) => Close();

                Content = image;
            }
        }
    }
}