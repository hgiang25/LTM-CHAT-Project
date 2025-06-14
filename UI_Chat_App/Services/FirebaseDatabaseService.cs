using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3.Transfer;
using Amazon.S3;
using ChatApp.Models;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using UI_Chat_App;
using Google.Apis.Auth.OAuth2;
using System.Windows.Forms;

namespace ChatApp.Services
{
    public class FirebaseDatabaseService
    {
        private readonly FirestoreDb _firestoreDb;

        public FirebaseDatabaseService()
        {
            try
            {
                // Khởi tạo Firestore với tệp service account
                string pathToServiceAccountKey = "firebase-service-account.json";
                if (!File.Exists(pathToServiceAccountKey))
                {
                    throw new FileNotFoundException($"Service account file not found at: {pathToServiceAccountKey}");
                }

                var builder = new FirestoreClientBuilder
                {
                    JsonCredentials = File.ReadAllText(pathToServiceAccountKey)
                };
                _firestoreDb = FirestoreDb.Create("fir-login-4f488", builder.Build());
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize Firestore: " + ex.Message, ex);
            }
        }

        public async Task SaveUserAsync(string idToken, UserData user)
        {
            try
            {
                if (user == null || string.IsNullOrEmpty(user.Id))
                {
                    throw new ArgumentException("User data is null or invalid.");
                }

                DocumentReference docRef = _firestoreDb.Collection("users").Document(user.Id);
                await docRef.SetAsync(user, SetOptions.Overwrite);
                Console.WriteLine($"Successfully saved user {user.Id} to Firestore.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save user to Firestore: {ex.Message}");
                throw;
            }
        }

        public async Task<Dictionary<string, UserData>> GetAllUsersAsync(string idToken)
        {
            try
            {
                CollectionReference usersRef = _firestoreDb.Collection("users");
                QuerySnapshot snapshot = await usersRef.GetSnapshotAsync();
                var usersDict = snapshot.Documents.ToDictionary(
                    doc => doc.Id,
                    doc => doc.ConvertTo<UserData>());
                Console.WriteLine($"Loaded {usersDict.Count} users from Firestore.");
                return usersDict;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get all users from Firestore: " + ex.Message, ex);
            }
        }

        public async Task SaveMessageAsync(string chatRoomId, MessageData message, string idToken)
        {
            try
            {
                CollectionReference messagesRef = _firestoreDb
                    .Collection("messages")
                    .Document(chatRoomId)
                    .Collection("messages");
                var docRef = await messagesRef.AddAsync(message);
                message.MessageId = docRef.Id; // Gán MessageId từ ID của document
                message.Timestamp = Timestamp.GetCurrentTimestamp(); // nếu chưa được gán từ trước
                await docRef.SetAsync(message, SetOptions.Overwrite); // Cập nhật lại document với MessageId
                Console.WriteLine($"Saved message with ID {message.MessageId} to chat room {chatRoomId}");
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save message to Firestore: " + ex.Message, ex);
            }
        }

        public async Task<List<MessageData>> GetMessagesAsync(string chatRoomId, Timestamp? lastTimestamp = null)
        {
            try
            {
                var messagesRef = _firestoreDb
                    .Collection("messages")
                    .Document(chatRoomId)
                    .Collection("messages");

                Query query = messagesRef.OrderBy("Timestamp");

                if (lastTimestamp != null)
                {
                    query = query.WhereGreaterThan("Timestamp", lastTimestamp);
                }

                var snapshot = await query.GetSnapshotAsync();
                var messages = snapshot.Documents
                .Select(doc =>
                {
                    var dict = doc.ToDictionary();

                    var message = new MessageData();
                    message.MessageId = doc.Id;

                    // Lấy các trường cơ bản
                    if (dict.TryGetValue("SenderId", out var senderId)) message.SenderId = senderId as string;
                    if (dict.TryGetValue("ReceiverId", out var receiverId)) message.ReceiverId = receiverId as string;
                    if (dict.TryGetValue("Content", out var content)) message.Content = content as string;
                    if (dict.TryGetValue("MessageType", out var messageType)) message.MessageType = messageType as string;
                    if (dict.TryGetValue("IsSeen", out var isSeen)) message.IsSeen = (bool)isSeen;

                    // Xử lý trường Timestamp
                    if (dict.TryGetValue("Timestamp", out var timestampObj))
                    {
                        if (timestampObj is string tsString)
                        {
                            // Chuyển string sang DateTime rồi sang Timestamp
                            var dt = DateTime.Parse(tsString).ToUniversalTime();
                            message.Timestamp = Google.Cloud.Firestore.Timestamp.FromDateTime(dt);
                        }
                        else if (timestampObj is Google.Cloud.Firestore.Timestamp ts)
                        {
                            message.Timestamp = ts;
                        }
                        else
                        {
                            // Trường hợp khác (nếu có)
                            message.Timestamp = null; // hoặc xử lý phù hợp
                        }
                    }
                    else
                    {
                        message.Timestamp = null; // Hoặc default value
                    }

                    return message;
                })
                .OrderBy(m => m.Timestamp?.ToDateTime() ?? DateTime.MinValue)
                .ToList();


                return messages;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get messages: {ex.Message}", ex);
            }
        }


        public async Task MarkMessageAsSeenAsync(string chatRoomId, string messageId)
        {
            try
            {
                DocumentReference messageRef = _firestoreDb
                    .Collection("messages")
                    .Document(chatRoomId)
                    .Collection("messages")
                    .Document(messageId);
                DocumentSnapshot snapshot = await messageRef.GetSnapshotAsync();
                if (!snapshot.Exists)
                {
                    throw new Exception($"Message with ID {messageId} not found in chat room {chatRoomId}");
                }
                await messageRef.UpdateAsync("IsSeen", true);
                Console.WriteLine($"Marked message {messageId} as seen in chat room {chatRoomId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to mark message as seen: {ex.Message}", ex);
            }
        }

        public async Task<UserData> GetUserAsync(string userId)
        {
            try
            {
                DocumentReference docRef = _firestoreDb.Collection("users").Document(userId);
                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();
                if (snapshot.Exists)
                {
                    var user = snapshot.ConvertTo<UserData>();
                    Console.WriteLine($"Fetched user from Firestore: {user.Id}, Avatar: {user.Avatar}");
                    return user;
                }
                return null;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get user from Firestore: " + ex.Message, ex);
            }
        }

        public string GenerateChatRoomId(string userId1, string userId2)
        {
            var ids = new[] { userId1, userId2 };
            Array.Sort(ids);
            return $"{ids[0]}_{ids[1]}";
        }

        public async Task<string> UploadFileToS3Async(string filePath, string folder)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found at: {filePath}");
                }

                // Lấy thông tin AWS từ biến môi trường
                string accessKey = DotNetEnv.Env.GetString("AWS_ACCESS_KEY");
                string secretKey = DotNetEnv.Env.GetString("AWS_SECRET_KEY");
                string region = DotNetEnv.Env.GetString("AWS_REGION");

                if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(region))
                {
                    throw new Exception("AWS credentials or region not found in environment variables.");
                }

                var s3Client = new AmazonS3Client(accessKey, secretKey, Amazon.RegionEndpoint.GetBySystemName(region));

                string fileName = Path.GetFileName(filePath);
                string s3Key = $"{folder}/{App.CurrentUser.Id}_{DateTime.Now.Ticks}_{fileName}";
                string bucketName = "vantai-chatapp";

                var fileTransferUtility = new TransferUtility(s3Client);
                await fileTransferUtility.UploadAsync(filePath, bucketName, s3Key);

                string fileUrl = $"https://{bucketName}.s3.{region}.amazonaws.com/{s3Key}";
                if (string.IsNullOrEmpty(fileUrl))
                {
                    throw new Exception("Failed to generate S3 URL after upload.");
                }

                Console.WriteLine($"Successfully uploaded file to S3: {fileUrl}");
                return fileUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to upload file to S3: {ex.Message}");
                throw;
            }
        }

        public async Task SendFriendRequestAsync(string idToken, string fromUserId, string toUserId)
        {
            try
            {
                if (fromUserId == toUserId)
                {
                    throw new Exception("You cannot send a friend request to yourself.");
                }

                if (await AreFriendsAsync(fromUserId, toUserId))
                {
                    throw new Exception("You are already friends with this user.");
                }

                CollectionReference requestsRef = _firestoreDb
                    .Collection("users").Document(toUserId)
                    .Collection("friendRequests");
                QuerySnapshot existingRequests = await requestsRef
                    .WhereEqualTo("FromUserId", fromUserId)
                    .WhereEqualTo("Status", "pending")
                    .GetSnapshotAsync();
                if (existingRequests.Count > 0)
                {
                    throw new Exception("A friend request has already been sent to this user.");
                }

                var request = new FriendRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    FromUserId = fromUserId,
                    ToUserId = toUserId,
                    Status = "pending",
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                DocumentReference requestRef = requestsRef.Document(request.RequestId);
                await requestRef.SetAsync(request);
                Console.WriteLine($"Sent friend request from {fromUserId} to {toUserId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send friend request: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<FriendRequestWithUserInfo>> GetFriendRequestsAsync(string userId)
        {
            try
            {
                CollectionReference requestsRef = _firestoreDb
                    .Collection("users").Document(userId)
                    .Collection("friendRequests");
                QuerySnapshot snapshot = await requestsRef
                    .WhereEqualTo("Status", "pending")
                    .GetSnapshotAsync();
                var requests = snapshot.Documents
                    .Where(doc => doc.Exists)
                    .Select(doc => doc.ConvertTo<FriendRequest>())
                    .ToList();

                // Lấy thông tin người gửi cho mỗi request
                var friendRequestsWithInfo = new List<FriendRequestWithUserInfo>();
                foreach (var request in requests)
                {
                    var sender = await GetUserAsync(request.FromUserId);
                    if (sender != null)
                    {
                        friendRequestsWithInfo.Add(new FriendRequestWithUserInfo
                        {
                            FriendRequest = request,
                            Sender = sender
                        });
                    }
                }

                Console.WriteLine($"Loaded {friendRequestsWithInfo.Count} friend requests for user {userId}");
                return friendRequestsWithInfo;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get friend requests: {ex.Message}", ex);
            }
        }

        public async Task AcceptFriendRequestAsync(string idToken, FriendRequest request)
        {
            try
            {
                DocumentReference requestRef = _firestoreDb
                    .Collection("users").Document(request.ToUserId)
                    .Collection("friendRequests").Document(request.RequestId);
                await requestRef.DeleteAsync();

                DocumentReference friendRef1 = _firestoreDb
                    .Collection("users").Document(request.ToUserId)
                    .Collection("friends").Document(request.FromUserId);
                await friendRef1.SetAsync(new FriendData
                {
                    FriendId = request.FromUserId,
                    Status = "accepted",
                    AddedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                DocumentReference friendRef2 = _firestoreDb
                    .Collection("users").Document(request.FromUserId)
                    .Collection("friends").Document(request.ToUserId);
                await friendRef2.SetAsync(new FriendData
                {
                    FriendId = request.ToUserId,
                    Status = "accepted",
                    AddedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                Console.WriteLine($"Friend request accepted: {request.FromUserId} and {request.ToUserId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to accept friend request: {ex.Message}", ex);
            }
        }

        public async Task RejectFriendRequestAsync(string idToken, FriendRequest request)
        {
            try
            {
                DocumentReference requestRef = _firestoreDb
                    .Collection("users").Document(request.ToUserId)
                    .Collection("friendRequests").Document(request.RequestId);
                await requestRef.DeleteAsync();
                Console.WriteLine($"Friend request rejected: {request.FromUserId} to {request.ToUserId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to reject friend request: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<UserData>> GetFriendsAsync(string userId)
        {
            try
            {
                CollectionReference friendsRef = _firestoreDb
                    .Collection("users").Document(userId)
                    .Collection("friends");
                QuerySnapshot snapshot = await friendsRef
                    .WhereEqualTo("Status", "accepted")
                    .GetSnapshotAsync();

                var friendIds = snapshot.Documents
                    .Select(doc => doc.ConvertTo<FriendData>().FriendId)
                    .ToList();

                List<UserData> friends = new List<UserData>();
                foreach (var friendId in friendIds)
                {
                    var user = await GetUserAsync(friendId);
                    if (user != null)
                    {
                        friends.Add(user);
                    }
                }

                Console.WriteLine($"Loaded {friends.Count} friends for user {userId}");
                return friends;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get friends: {ex.Message}", ex);
            }
        }

        public async Task<bool> AreFriendsAsync(string userId1, string userId2)
        {
            try
            {
                DocumentReference friendRef = _firestoreDb
                    .Collection("users").Document(userId1)
                    .Collection("friends").Document(userId2);
                DocumentSnapshot snapshot = await friendRef.GetSnapshotAsync();
                return snapshot.Exists && snapshot.GetValue<string>("Status") == "accepted";
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to check friendship: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<FriendRequestWithUserInfo>> GetSentFriendRequestsAsync(string userId)
        {
            try
            {
                // Tạo danh sách để lưu tất cả các lời mời đã gửi
                var sentFriendRequests = new List<FriendRequest>();

                // Lấy tất cả người dùng để kiểm tra các lời mời đã gửi
                CollectionReference usersRef = _firestoreDb.Collection("users");
                QuerySnapshot usersSnapshot = await usersRef.GetSnapshotAsync();

                // Duyệt qua từng người dùng để kiểm tra xem userId có gửi lời mời cho họ không
                foreach (var userDoc in usersSnapshot.Documents)
                {
                    if (userDoc.Id == userId) continue; // Bỏ qua chính người dùng hiện tại

                    CollectionReference requestsRef = _firestoreDb
                        .Collection("users").Document(userDoc.Id)
                        .Collection("friendRequests");
                    QuerySnapshot snapshot = await requestsRef
                        .WhereEqualTo("FromUserId", userId)
                        .WhereEqualTo("Status", "pending")
                        .GetSnapshotAsync();

                    var requests = snapshot.Documents
                        .Where(doc => doc.Exists)
                        .Select(doc => doc.ConvertTo<FriendRequest>())
                        .ToList();

                    sentFriendRequests.AddRange(requests);
                }

                // Lấy thông tin người nhận cho mỗi request
                var friendRequestsWithInfo = new List<FriendRequestWithUserInfo>();
                foreach (var request in sentFriendRequests)
                {
                    var receiver = await GetUserAsync(request.ToUserId);
                    if (receiver != null)
                    {
                        friendRequestsWithInfo.Add(new FriendRequestWithUserInfo
                        {
                            FriendRequest = request,
                            Sender = receiver // Ở đây Sender là người nhận lời mời
                        });
                    }
                }

                Console.WriteLine($"Loaded {friendRequestsWithInfo.Count} sent friend requests for user {userId}");
                return friendRequestsWithInfo;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get sent friend requests: {ex.Message}", ex);
            }
        }

        public async Task CancelFriendRequestAsync(string idToken, string fromUserId, string toUserId, string requestId)
        {
            try
            {
                DocumentReference requestRef = _firestoreDb
                    .Collection("users").Document(toUserId)
                    .Collection("friendRequests").Document(requestId);
                DocumentSnapshot snapshot = await requestRef.GetSnapshotAsync();
                if (snapshot.Exists && snapshot.GetValue<string>("FromUserId") == fromUserId && snapshot.GetValue<string>("Status") == "pending")
                {
                    await requestRef.DeleteAsync();
                    Console.WriteLine($"Cancelled friend request {requestId} from {fromUserId} to {toUserId}");
                }
                else
                {
                    throw new Exception("Friend request not found or cannot be cancelled.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to cancel friend request: " + ex.Message, ex);
            }
        }

        public async Task<List<NotificationData>> GetNotificationsAsync(string userId)
        {
            try
            {
                CollectionReference notifRef = _firestoreDb
                    .Collection("notifications")
                    .Document(userId)
                    .Collection("items");

                QuerySnapshot snapshot = await notifRef
                    .OrderBy("Timestamp")
                    .GetSnapshotAsync();

                var notifications = snapshot.Documents
                    .Select(doc =>
                    {
                        var notification = doc.ConvertTo<NotificationData>();
                        notification.Id = doc.Id;
                        return notification;
                    })                    
                    .ToList();

                Console.WriteLine($"Loaded {notifications.Count} notifications for user {userId}");
                return notifications;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get notifications for user {userId}: {ex.Message}", ex);
            }
        }
        public async Task SendNotificationAsync(string receiverId, string senderId, string content, string groupId = null)
        {
            var notification = new NotificationData
            {
                Type = "New Notification",
                From = senderId,
                To = receiverId,
                Content = content,
                Timestamp = DateTime.UtcNow.ToString("o"),
                IsRead = false,
                GroupId = groupId,
                IsGroup = groupId != null
            };

            DocumentReference docRef = _firestoreDb
                .Collection("notifications")
                .Document(receiverId)
                .Collection("items")
                .Document();

            await docRef.SetAsync(notification);
        }

        public async Task<string> GetGroupNameAsync(string groupId)
        {
            var doc = await _firestoreDb.Collection("groups").Document(groupId).GetSnapshotAsync();
            if (doc.Exists && doc.ContainsField("name"))
            {
                return doc.GetValue<string>("name");
            }
            return "Nhóm không xác định";
        }

        public async Task MarkNotificationsAsReadAsync(string currUserId, string notificationId)
        {
            DocumentReference notificationRef = _firestoreDb
                .Collection("notifications")
                .Document(currUserId)
                .Collection("items")
                .Document(notificationId);
            DocumentSnapshot snapshot = await notificationRef.GetSnapshotAsync();
            if (!snapshot.Exists)
            {
                throw new Exception($"Notification with ID {notificationId} not found in user notice {currUserId}");
            }
            await notificationRef.UpdateAsync("IsRead", true);
            Console.WriteLine($"Marked notification {notificationId} as seen in notification room {currUserId}");                        
        }
        public async Task<int> CountUnreadNotificationsAsync(string userId)
        {
            Console.WriteLine($"[DEBUG] CountUnreadNotificationsAsync called for userId: {userId}");
            try
            {
                if (string.IsNullOrEmpty(userId))
                    throw new ArgumentException("UserId cannot be empty.");

                CollectionReference notifRef = _firestoreDb
                    .Collection("notifications")
                    .Document(userId)
                    .Collection("items");
                var query = notifRef.WhereEqualTo("IsRead", false);
                var snapshot = await query.GetSnapshotAsync();
                int unreadCount = snapshot.Count;


                Console.WriteLine($"Found {unreadCount} unread notifications for user {userId}");
                return unreadCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to count unread notifications for user {userId}: {ex.Message}");
                throw new Exception($"Failed to count unread notifications: {ex.Message}", ex);
            }
        }
        private FirestoreChangeListener _notifListener;

        public void StartListeningForNotifications(string userId, Action<NotificationData> onNewNotification)
        {
            var notifRef = _firestoreDb
                .Collection("notifications")
                .Document(userId)
                .Collection("items");

            _notifListener = notifRef.Listen(snapshot =>
            {
                foreach (var change in snapshot.Changes)
                {
                    if (change.ChangeType == Google.Cloud.Firestore.DocumentChange.Type.Added)
                    {
                        var notif = change.Document.ConvertTo<NotificationData>();
                        onNewNotification?.Invoke(notif);
                    }
                }
            });
        }

        public async Task StopListeningForNotificationsAsync()
        {
            if (_notifListener != null)
            {
                await _notifListener.StopAsync();
                _notifListener = null;
            }
        }

        public async Task SetTypingStatusAsync(string senderId, string receiverId, bool isTyping)
        {
            try
            {
                var typingRef = _firestoreDb
                    .Collection("typingStatus")
                    .Document(receiverId)
                    .Collection("users")
                    .Document(senderId);

                var data = new Dictionary<string, object>
        {
            { "isTyping", isTyping },
            { "timestamp", Timestamp.GetCurrentTimestamp() }
        };

                await typingRef.SetAsync(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set typing status: {ex.Message}");
            }
        }

        public FirestoreChangeListener ListenToTypingStatus(string receiverId, string senderId, Action<bool> onTypingStatusChanged)
        {
            string docPath = $"typingStatus/{receiverId}/users/{senderId}";
            DocumentReference docRef = _firestoreDb.Document(docPath);

            return docRef.Listen(snapshot =>
            {
                if (snapshot.Exists && snapshot.TryGetValue("isTyping", out bool isTyping))
                {
                    onTypingStatusChanged(isTyping);
                }
                else
                {
                    onTypingStatusChanged(false);
                }
            });
        }

        private FirestoreChangeListener _messageListener;       

        public async Task StartListeningToMessagesAsync(string chatRoomId, Action<MessageData> onMessageReceived)
        {
            if (_messageListener != null)
            {
                await _messageListener.StopAsync();
                _messageListener = null;
            }

            var messagesRef = _firestoreDb
                .Collection("messages")
                .Document(chatRoomId)
                .Collection("messages");

            var query = messagesRef.OrderBy("Timestamp");

            _messageListener = query.Listen(snapshot =>
            {
                foreach (var docChange in snapshot.Changes)
                {
                    if (docChange.ChangeType == Google.Cloud.Firestore.DocumentChange.Type.Added)
                    {
                        var dict = docChange.Document.ToDictionary();
                        var message = new MessageData();
                        message.MessageId = docChange.Document.Id;

                        // Các trường cơ bản
                        if (dict.ContainsKey("SenderId")) message.SenderId = dict["SenderId"] as string;
                        if (dict.ContainsKey("ReceiverId")) message.ReceiverId = dict["ReceiverId"] as string;
                        if (dict.ContainsKey("Content")) message.Content = dict["Content"] as string;
                        if (dict.ContainsKey("MessageType")) message.MessageType = dict["MessageType"] as string;
                        if (dict.ContainsKey("IsSeen") && dict["IsSeen"] is bool seen) message.IsSeen = seen;

                        // Xử lý Timestamp giống hệt GetMessagesAsync
                        if (dict.ContainsKey("Timestamp"))
                        {
                            var tsObj = dict["Timestamp"];
                            if (tsObj is string tsString)
                            {
                                DateTime dt;
                                if (DateTime.TryParse(tsString, out dt))
                                {
                                    message.Timestamp = Timestamp.FromDateTime(dt.ToUniversalTime());
                                }
                            }
                            else if (tsObj is Timestamp ts)
                            {
                                message.Timestamp = ts;
                            }
                            else
                            {
                                message.Timestamp = null;
                            }
                        }
                        else
                        {
                            message.Timestamp = null;
                        }

                        onMessageReceived?.Invoke(message);
                    }
                }
            });
        }




        public async Task StopListeningToMessagesAsync()
        {
            if (_messageListener != null)
            {
                await _messageListener.StopAsync();
                _messageListener = null;
            }
        }


        public async Task<string> CreateGroupAsync(string groupName, string creatorId, List<string> memberIds)
        {
            var groupId = Guid.NewGuid().ToString();

            var members = new Dictionary<string, string>
            {
                [creatorId] = "admin"
            };

            if (memberIds != null)
            {
                foreach (var id in memberIds)
                {
                    if (id != creatorId)
                        members[id] = "member";
                }
            }

            var groupData = new Dictionary<string, object>
                {
                    { "name", groupName },
                    { "createdBy", creatorId },
                    { "createdAt", Timestamp.GetCurrentTimestamp() },
                    { "avatar", "Icons/group.png" },
                    { "members", members },
                    { "memberCount", members.Count }
                };

            var groupRef = _firestoreDb.Collection("groups").Document(groupId);
            await groupRef.SetAsync(groupData);

            return groupId;
        }


        public async Task<List<string>> GetGroupMembersAsync(string groupId)
        {
            var doc = await _firestoreDb.Collection("groups").Document(groupId).GetSnapshotAsync();

            var result = new HashSet<string>(); // Dùng HashSet để loại trùng nếu có

            if (doc.Exists)
            {
                if (doc.ContainsField("members"))
                {
                    var membersMap = doc.GetValue<Dictionary<string, object>>("members");
                    foreach (var key in membersMap.Keys)
                        result.Add(key);
                }

                if (doc.ContainsField("pending members"))
                {
                    var pendingMap = doc.GetValue<Dictionary<string, object>>("pending members");
                    foreach (var key in pendingMap.Keys)
                        result.Add(key);
                }
            }

            return result.ToList();
        }


        public async Task<List<string>> GetGroupPendingMembersAsync(string groupId)
        {
            var doc = await _firestoreDb.Collection("groups").Document(groupId).GetSnapshotAsync();
            if (doc.Exists && doc.ContainsField("pending members"))
            {
                var pendingMap = doc.GetValue<Dictionary<string, object>>("pending members");
                return pendingMap.Keys.ToList();
            }
            return new List<string>();
        }


        public async Task<List<GroupData>> GetGroupsForUserAsync(string userId)
        {
            try
            {
                // Truy vấn tất cả nhóm mà "members" chứa userId
                Query groupsQuery = _firestoreDb.Collection("groups")
                    .WhereGreaterThanOrEqualTo($"members.{userId}", ""); // Chỉ cần key tồn tại trong map

                QuerySnapshot snapshot = await groupsQuery.GetSnapshotAsync();
                List<GroupData> groups = new List<GroupData>();

                foreach (var doc in snapshot.Documents)
                {
                    Dictionary<string, object> data = doc.ToDictionary();

                    var group = new GroupData
                    {
                        GroupId = doc.Id,
                        Name = data.ContainsKey("name") ? data["name"].ToString() : "[No name]",
                        Avatar = data.ContainsKey("avatar") ? data["avatar"].ToString() : "Icons/group.png",
                        CreatedBy = data.ContainsKey("createdBy") ? data["createdBy"].ToString() : "[Unknown]",
                        MemberCount = data.ContainsKey("memberCount") ? Convert.ToInt32(data["memberCount"]) : 0
                    };

                    groups.Add(group);
                }

                Console.WriteLine($"[Firestore] Loaded {groups.Count} groups for user {userId}");
                return groups;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get groups for user: {ex.Message}", ex);
            }
        }

        public async Task RemoveMemberFromGroupAsync(string groupId, string userId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);

            await _firestoreDb.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(groupRef);
                if (!snapshot.Exists)
                {
                    throw new Exception("Nhóm không tồn tại.");
                }

                // ✅ Đọc đúng kiểu Dictionary<string, string>
                var members = snapshot.GetValue<Dictionary<string, string>>("members");

                if (!members.ContainsKey(userId))
                {
                    throw new Exception("Thành viên không tồn tại trong nhóm.");
                }

                members.Remove(userId);

                transaction.Update(groupRef, new Dictionary<string, object>
                {
                    { "members", members },
                    { "memberCount", members.Count }
                });
            });
        }




        public async Task InviteMemberToGroupAsync(string groupId, string inviterId, string targetUserId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);
            var groupSnap = await groupRef.GetSnapshotAsync();

            if (!groupSnap.Exists)
                throw new Exception("Group does not exist.");

            // Load members và pendingMembers
            var members = groupSnap.ContainsField("members")
                ? groupSnap.GetValue<Dictionary<string, object>>("members").ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                : new Dictionary<string, string>();

            var pendingMembers = groupSnap.ContainsField("pending members")
                ? groupSnap.GetValue<Dictionary<string, object>>("pending members").ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                : new Dictionary<string, string>();

            // Nếu đã là thành viên => không làm gì
            if (members.ContainsKey(targetUserId))
                throw new Exception("User is already a member.");

            // Kiểm tra người mời có phải admin không
            bool isInviterAdmin = members.TryGetValue(inviterId, out var role) && role == "admin";

            if (isInviterAdmin)
            {
                // Nếu có trong pending thì xoá
                if (pendingMembers.ContainsKey(targetUserId))
                {
                    pendingMembers.Remove(targetUserId);
                }

                // Thêm vào members
                members[targetUserId] = "member";

                // Cập nhật cả members và pendingMembers
                var updates = new Dictionary<string, object>
        {
            { "members", members },
            { "pending members", pendingMembers },
            { "memberCount", members.Count }
        };

                await groupRef.UpdateAsync(updates);
            }
            else
            {
                // Nếu người thường mời => check đã được mời chưa
                if (pendingMembers.ContainsKey(targetUserId))
                    throw new Exception("User has already been invited.");

                pendingMembers[targetUserId] = "invited";

                await groupRef.UpdateAsync("pending members", pendingMembers);
            }
        }


        public async Task<List<UserData>> GetUsersByIdsAsync(List<string> userIds)
        {
            var users = new List<UserData>();
            var usersCollection = _firestoreDb.Collection("users");

            foreach (var userId in userIds)
            {
                var snapshot = await usersCollection.Document(userId).GetSnapshotAsync();
                if (snapshot.Exists)
                {
                    users.Add(snapshot.ConvertTo<UserData>());
                }
                else
                {
                    Console.WriteLine($"User not found: {userId}");
                }
            }

            return users;
        }

        public async Task RejectMemberAsync(string groupId, string userId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);

            // Xóa trực tiếp userId khỏi trường pendingMembers mà không cần tải toàn bộ
            var updates = new Dictionary<string, object>
    {
        { $"pending members.{userId}", FieldValue.Delete }
    };

            await groupRef.UpdateAsync(updates);

            Console.WriteLine($"User {userId} has been rejected from group {groupId}.");
        }


        public async Task ApproveMemberAsync(string groupId, string userId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);
            var snapshot = await groupRef.GetSnapshotAsync();

            var members = snapshot.GetValue<Dictionary<string, object>>("members")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

            var pendingMembers = snapshot.ContainsField("pending members")
                ? snapshot.GetValue<Dictionary<string, object>>("pending members").ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                : new Dictionary<string, string>();

            if (!pendingMembers.ContainsKey(userId))
                throw new Exception("User is not in pending list.");

            // Thêm vào members, xoá khỏi pending
            members[userId] = "member";
            pendingMembers.Remove(userId);

            await groupRef.UpdateAsync(new Dictionary<string, object>
            {
                { "members", members },
                { "pending members", pendingMembers },
                { "memberCount", members.Count }
            });
        }

        public async Task<GroupData> GetGroupAsync(string groupId)
        {
            var docRef = _firestoreDb.Collection("groups").Document(groupId);
            var snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists)
                throw new Exception("Group not found.");

            var groupData = snapshot.ConvertTo<GroupData>();
            groupData.GroupId = snapshot.Id;

            // Handle members field
            if (snapshot.ContainsField("members"))
            {
                var membersMap = snapshot.GetValue<Dictionary<string, object>>("members");
                groupData.Members = membersMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
                Console.WriteLine($"Loaded members: {string.Join(", ", groupData.Members.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            }
            else
            {
                groupData.Members = new Dictionary<string, string>();
                Console.WriteLine("No members found in group document.");
            }

            // Handle pendingMembers field
            if (snapshot.ContainsField("pending members"))
            {
                var pendingMembersMap = snapshot.GetValue<Dictionary<string, object>>("pending members");
                groupData.PendingMembers = pendingMembersMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
                Console.WriteLine($"Loaded pending members: {string.Join(", ", groupData.PendingMembers.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            }
            else
            {
                groupData.PendingMembers = new Dictionary<string, string>();
                Console.WriteLine("No pending members found in group document.");
            }

            return groupData;
        }


        public async Task<List<UserData>> LoadUsersByIdsAsync(List<string> userIds)
        {
            var users = new List<UserData>();
            var usersCollection = _firestoreDb.Collection("users");

            // Dùng Task để thực hiện truy vấn song song
            var tasks = userIds.Select(async userId =>
            {
                var snapshot = await usersCollection.Document(userId).GetSnapshotAsync();
                return snapshot.Exists ? snapshot.ConvertTo<UserData>() : null;
            });

            var results = await Task.WhenAll(tasks);

            foreach (var user in results)
            {
                if (user != null)
                    users.Add(user);
            }

            return users;
        }

        public async Task ChangeGroupAdminAsync(string groupId, string oldAdminId, string newAdminId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);

            await _firestoreDb.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(groupRef);
                if (!snapshot.Exists)
                    throw new Exception("Nhóm không tồn tại.");

                var data = snapshot.ToDictionary();
                var members = data["members"] as Dictionary<string, object>;

                if (!members.ContainsKey(oldAdminId))
                    throw new Exception("Admin cũ không tồn tại."); // Lỗi nằm ở đây nếu bạn đã xoá trước

                if (!members.ContainsKey(newAdminId))
                    throw new Exception("Admin mới không tồn tại.");

                members[oldAdminId] = "member";
                members[newAdminId] = "admin";

                transaction.Update(groupRef, new Dictionary<string, object>
        {
            { "members", members }
        });
            });
        }

        private FirestoreChangeListener _userGroupsListener;

        public void ListenToUserGroups(string userId, Action<List<GroupData>> onGroupsChanged)
        {
            var groupsRef = _firestoreDb.Collection("groups");

            // Lắng nghe mọi thay đổi trong tập hợp nhóm
            _userGroupsListener = groupsRef.Listen(snapshot =>
            {
                Console.WriteLine($"📥 Received group snapshot: {snapshot.Documents.Count} documents");

                var userGroups = new List<GroupData>();

                foreach (var doc in snapshot.Documents)
                {
                    if (doc.Exists)
                    {
                        var group = doc.ConvertTo<GroupData>();

                        if (group.Members != null)
                            Console.WriteLine($"✅ Group: {group.GroupId} | Members: {string.Join(",", group.Members.Keys)}");

                        if (group.Members != null && group.Members.ContainsKey(userId))
                        {
                            userGroups.Add(group);
                        }
                    }
                }

                Console.WriteLine($"✅ User {userId} belongs to {userGroups.Count} groups");
                onGroupsChanged?.Invoke(userGroups);
            });

        }

        public async Task StopListeningToUserGroupsAsync()
        {
            if (_userGroupsListener != null)
            {
                await _userGroupsListener.StopAsync();
                _userGroupsListener = null;
            }
        }

        public async Task SendSystemMessageToChatAsync(string chatRoomId, string content)
        {
            var message = new MessageData
            {
                SenderId = "system",
                ReceiverId = null,
                Content = content,
                Timestamp = Timestamp.GetCurrentTimestamp(),
                MessageType = "System",
                IsSeen = true
            };

            await SaveMessageAsync(chatRoomId, message, ""); // Truyền rỗng vì không cần idToken
        }




        //private FirestoreChangeListener _groupMessageListener;

        //public void ListenToGroupMessages(string groupId, Action<MessageData> onMessageReceived)
        //{
        //    var messagesRef = _firestoreDb
        //        .Collection("groups")
        //        .Document(groupId)
        //        .Collection("messages")
        //        .OrderBy("timestamp");

        //    _groupMessageListener = messagesRef.Listen(snapshot =>
        //    {
        //        foreach (var change in snapshot.Changes)
        //        {
        //            if (change.ChangeType == Google.Cloud.Firestore.DocumentChange.Type.Added)
        //            {
        //                var message = change.Document.ConvertTo<MessageData>();
        //                onMessageReceived?.Invoke(message); // Cập nhật UI
        //            }
        //        }
        //    });
        //}

        //// Gọi khi rời nhóm hoặc đổi nhóm
        //public void StopListeningToMessages()
        //{
        //    _groupMessageListener?.StopAsync();
        //    _groupMessageListener = null;
        //}


        public FirestoreDb GetDb() => _firestoreDb;


    }
}