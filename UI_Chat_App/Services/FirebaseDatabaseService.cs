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
using System.Threading;

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
                _firestoreDb = FirestoreDb.Create("my-chatapp-6e8f6", builder.Build());
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
                CollectionReference messagesRef = _firestoreDb
                    .Collection("messages")
                    .Document(chatRoomId)
                    .Collection("messages");
                QuerySnapshot snapshot = await messagesRef.OrderBy("Timestamp").GetSnapshotAsync();
                var messages = snapshot.Documents
                    .Select(doc =>
                    {
                        var message = doc.ConvertTo<MessageData>();
                        message.MessageId = doc.Id; // Assign MessageId from document ID
                        return message;
                    })
                    .OrderBy(m => m.Timestamp?.ToDateTime()) // Convert Timestamp? to DateTime for ordering
                    .ToList();
                Console.WriteLine($"Loaded {messages.Count} messages for chat room {chatRoomId}");
                return messages;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get messages for chat room {chatRoomId}: {ex.Message}", ex);
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

        public string GetChatRoomId(string userId1, string userId2)
        {
            return string.CompareOrdinal(userId1, userId2) < 0
                ? $"{userId1}_{userId2}"
                : $"{userId2}_{userId1}";
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
                    AddedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    Priority = 0,
                    Blocked = false
                });

                DocumentReference friendRef2 = _firestoreDb
                    .Collection("users").Document(request.FromUserId)
                    .Collection("friends").Document(request.ToUserId);
                await friendRef2.SetAsync(new FriendData
                {
                    FriendId = request.ToUserId,
                    Status = "accepted",
                    AddedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    Priority = 0,
                    Blocked = false
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

        public async Task InitializeAllFriendPrioritiesAsync()
        {
            var usersRef = _firestoreDb.Collection("users");
            var usersSnapshot = await usersRef.GetSnapshotAsync();

            foreach (var userDoc in usersSnapshot.Documents)
            {
                string userId = userDoc.Id;
                var friendsRef = usersRef.Document(userId).Collection("friends");
                var friendsSnapshot = await friendsRef.GetSnapshotAsync();

                foreach (var friendDoc in friendsSnapshot.Documents)
                {
                    if (!friendDoc.ContainsField("Priority"))
                    {
                        await friendDoc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    { "Priority", 0 }
                });

                        Console.WriteLine($"[OK] Updated Priority=0 for friend {friendDoc.Id} of user {userId}");
                    }
                }
            }
        }

        public async Task AddBlockedFieldToAllFriendsAsync()
        {
            var usersRef = _firestoreDb.Collection("users");
            var usersSnapshot = await usersRef.GetSnapshotAsync();

            foreach (var userDoc in usersSnapshot.Documents)
            {
                string userId = userDoc.Id;
                var friendsRef = usersRef.Document(userId).Collection("friends");
                var friendsSnapshot = await friendsRef.GetSnapshotAsync();

                foreach (var friendDoc in friendsSnapshot.Documents)
                {
                    if (!friendDoc.ContainsField("Blocked"))
                    {
                        await friendDoc.Reference.UpdateAsync(new Dictionary<string, object>
                {
                    { "Blocked", false }
                });

                        Console.WriteLine($"[OK] Added Blocked=false to friend {friendDoc.Id} of user {userId}");
                    }
                }
            }

            Console.WriteLine("✅ Completed adding 'Blocked' field to all friends.");
        }

        public async Task<bool> IsBlockedBetweenUsers(string userId, string otherUserId)
        {
            var myFriendRef = _firestoreDb.Collection("users").Document(userId).Collection("friends").Document(otherUserId);
            var otherFriendRef = _firestoreDb.Collection("users").Document(otherUserId).Collection("friends").Document(userId);

            var myDoc = await myFriendRef.GetSnapshotAsync();
            var otherDoc = await otherFriendRef.GetSnapshotAsync();

            bool iBlocked = myDoc.Exists && myDoc.ContainsField("Blocked") && myDoc.GetValue<bool>("Blocked");
            bool theyBlocked = otherDoc.Exists && otherDoc.ContainsField("Blocked") && otherDoc.GetValue<bool>("Blocked");

            return iBlocked || theyBlocked;
        }

        public async Task<bool> IsBlockingUser(string userId, string targetUserId)
        {
            var docRef = _firestoreDb.Collection("users").Document(userId)
                .Collection("friends").Document(targetUserId);

            var snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists || !snapshot.ContainsField("Blocked")) return false;

            return snapshot.GetValue<bool>("Blocked");
        }

        public async Task SetBlockStatusAsync(string userId, string targetUserId, bool isBlocked)
        {
            var docRef = _firestoreDb.Collection("users").Document(userId)
                .Collection("friends").Document(targetUserId);

            await docRef.UpdateAsync(new Dictionary<string, object>
    {
        { "Blocked", isBlocked }
    });
        }




        public async Task RemoveFriendAsyncAndDeleteMessages(string currentUserId, string targetUserId)
        {
            try
            {
                // Xóa mối quan hệ bạn bè ở cả 2 phía
                var currentUserFriendRef = _firestoreDb
                    .Collection("users").Document(currentUserId)
                    .Collection("friends").Document(targetUserId);
                var targetUserFriendRef = _firestoreDb
                    .Collection("users").Document(targetUserId)
                    .Collection("friends").Document(currentUserId);

                WriteBatch batch = _firestoreDb.StartBatch();
                batch.Delete(currentUserFriendRef);
                batch.Delete(targetUserFriendRef);

                // Xóa toàn bộ tin nhắn giữa hai người dùng
                string chatRoomId = GenerateChatRoomId(currentUserId, targetUserId);
                CollectionReference messagesRef = _firestoreDb
                    .Collection("messages").Document(chatRoomId)
                    .Collection("messages");

                var snapshot = await messagesRef.GetSnapshotAsync();
                foreach (var doc in snapshot.Documents)
                {
                    batch.Delete(doc.Reference);
                }

                // Xóa document cha của chatRoom (nếu không cần giữ lại)
                var chatRoomDoc = _firestoreDb.Collection("messages").Document(chatRoomId);
                batch.Delete(chatRoomDoc);

                await batch.CommitAsync();

                Console.WriteLine($"Removed friend and deleted all messages between {currentUserId} and {targetUserId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to remove friend and delete messages: {ex.Message}", ex);
            }
        }

        public async Task SetFriendPriorityAsync(string userId, string friendId, int priority)
        {
            DocumentReference friendRef = _firestoreDb
                .Collection("users").Document(userId)
                .Collection("friends").Document(friendId);

            await friendRef.UpdateAsync("Priority", priority);
        }


        public async Task ToggleFriendPriorityAsync(string userId, string friendId)
        {
            var friendRef = _firestoreDb
                .Collection("users").Document(userId)
                .Collection("friends").Document(friendId);

            var snapshot = await friendRef.GetSnapshotAsync();
            if (!snapshot.Exists) return;

            var friendData = snapshot.ConvertTo<FriendData>();
            int currentPriority = 0;
            if (snapshot.ContainsField("Priority"))
            {
                currentPriority = snapshot.GetValue<int>("Priority");
            }

            if (currentPriority > 0)
            {
                // Nếu đã ghim, thì bỏ ghim (Priority = 0)
                await friendRef.UpdateAsync(new Dictionary<string, object>
        {
            { "Priority", 0 }
        });
                Console.WriteLine($"Unpinned friend {friendId}");
            }
            else
            {
                // Nếu chưa ghim, thì ghim với Priority cao nhất + 1
                var friendsRef = _firestoreDb
                    .Collection("users").Document(userId)
                    .Collection("friends");

                var highestSnapshot = await friendsRef
                    .WhereGreaterThan("Priority", 0)
                    .OrderByDescending("Priority")
                    .Limit(1)
                    .GetSnapshotAsync();

                int newPriority = 1;
                if (highestSnapshot.Count > 0)
                {
                    newPriority = highestSnapshot.Documents[0].GetValue<int>("Priority") + 1;
                }

                await friendRef.UpdateAsync(new Dictionary<string, object>
        {
            { "Priority", newPriority }
        });

                Console.WriteLine($"Pinned friend {friendId} with priority {newPriority}");
            }
        }



        public async Task<IEnumerable<UserData>> GetFriendsAsync(string userId)
        {
            try
            {
                var friendsRef = _firestoreDb
                    .Collection("users").Document(userId)
                    .Collection("friends");

                var snapshot = await friendsRef
                    .WhereEqualTo("Status", "accepted")
                    .GetSnapshotAsync();

                var friends = new List<UserData>();

                foreach (var doc in snapshot.Documents)
                {
                    var friendData = doc.ConvertTo<FriendData>();
                    var user = await GetUserAsync(friendData.FriendId);
                    if (user != null)
                    {
                        user.Tag = friendData.Priority;
                        user.IsBlocked = friendData.Blocked;
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
                if (string.IsNullOrEmpty(userId))
                    throw new ArgumentException("UserId cannot be empty.");

                // Truy vấn subcollection sentFriendRequests của người dùng
                CollectionReference sentRequestsRef = _firestoreDb
                    .Collection("users").Document(userId)
                    .Collection("sentFriendRequests");
                QuerySnapshot snapshot = await sentRequestsRef
                    .WhereEqualTo("Status", "pending")
                    .GetSnapshotAsync();

                var sentFriendRequests = snapshot.Documents
                    .Select(doc => doc.ConvertTo<FriendRequest>())
                    .ToList();

                // Lấy thông tin người nhận
                var friendRequestsWithInfo = new List<FriendRequestWithUserInfo>();
                foreach (var request in sentFriendRequests)
                {
                    var receiver = await GetUserAsync(request.ToUserId);
                    if (receiver != null)
                    {
                        friendRequestsWithInfo.Add(new FriendRequestWithUserInfo
                        {
                            FriendRequest = request,
                            Sender = receiver
                        });
                    }
                }

                Console.WriteLine($"Loaded {friendRequestsWithInfo.Count} sent friend requests for user {userId}");
                return friendRequestsWithInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get sent friend requests for user {userId}: {ex.Message}");
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

        private FirestoreChangeListener _friendRequestListener;
        public async Task ListenToFriendRequestsAsync(string userId, Action<FriendRequest> onRequestChanged)
        {
            if (_friendRequestListener != null)
            {
                await _friendRequestListener.StopAsync();
                _friendRequestListener = null;
            }

            var requestsRef = _firestoreDb
                .Collection("users").Document(userId)
                .Collection("friendRequests")
                .WhereEqualTo("Status", "pending");

            _friendRequestListener = requestsRef.Listen(snapshot =>
            {
                foreach (var change in snapshot.Changes)
                {
                    if (change.Document.Exists)
                    {
                        var dict = change.Document.ToDictionary();
                        var request = new FriendRequest
                        {
                            RequestId = change.Document.Id,
                            FromUserId = dict.TryGetValue("FromUserId", out var f) ? f as string : null,
                            ToUserId = dict.TryGetValue("ToUserId", out var t) ? t as string : null,
                            Status = dict.TryGetValue("Status", out var s) ? s as string : null,
                            CreatedAt = dict.TryGetValue("CreatedAt", out var c) && c is Timestamp ts ? ts : (Timestamp?)null
                        };
                        onRequestChanged?.Invoke(request);
                    }
                }
            });
        }

        private FirestoreChangeListener _friendsListener;
        public async Task ListenToFriendsAsync(string userId, Action<FriendData, Google.Cloud.Firestore.DocumentChange.Type> onFriendChanged)
        {
            if (_friendsListener != null)
            {
                await _friendsListener.StopAsync();
                _friendsListener = null;
            }

            var friendsRef = _firestoreDb
                .Collection("users").Document(userId)
                .Collection("friends")
                .WhereEqualTo("Status", "accepted");

            _friendsListener = friendsRef.Listen(snapshot =>
            {
                foreach (var change in snapshot.Changes)
                {
                    var dict = change.Document.ToDictionary();
                    var friend = new FriendData
                    {
                        FriendId = change.Document.Id,
                        Status = dict.TryGetValue("Status", out var s) ? s as string : null,
                        Blocked = dict.TryGetValue("Blocked", out var b) && b is bool blk && blk,
                        Priority = dict.TryGetValue("Priority", out var p) && p is int pri ? pri : 0,
                        AddedAt = dict.TryGetValue("AddedAt", out var t) && t is Timestamp ts ? ts : (Timestamp?)null
                    };
                    // Gửi loại thay đổi kèm dữ liệu
                    onFriendChanged?.Invoke(friend, change.ChangeType);
                }
            });
        }

        public async Task StopFriendListenersAsync()
        {
            if (_friendRequestListener != null)
            {
                await _friendRequestListener.StopAsync();
                _friendRequestListener = null;
            }

            if (_friendsListener != null)
            {
                await _friendsListener.StopAsync();
                _friendsListener = null;
            }

            Console.WriteLine("Stopped listening to friend data");
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
                if (snapshot.Exists &&
                    snapshot.TryGetValue("isTyping", out bool isTyping) &&
                    snapshot.TryGetValue("timestamp", out Timestamp ts))
                {
                    var typingTime = ts.ToDateTime();
                    var currentTime = DateTime.UtcNow;

                    // Nếu thời gian typing quá 5 giây thì coi như không còn typing
                    if ((currentTime - typingTime).TotalSeconds > 5)
                    {
                        onTypingStatusChanged(false);
                    }
                    else
                    {
                        onTypingStatusChanged(isTyping);
                    }
                }
                else
                {
                    onTypingStatusChanged(false);
                }
            });
        }



        private FirestoreChangeListener _messageListener;
        //private bool _isMessageListenerActive = false;
        private CancellationTokenSource _messageListeningCts;
        private string _activeListeningRoomId;

        public async Task StartListeningToMessagesAsync(string chatRoomId, Func<MessageData, Task> onMessageReceived)
        {
            // Ngắt lắng nghe trước đó (nếu có)
            if (_messageListener != null)
            {
                try { await _messageListener.StopAsync(); } catch { /* Ignore */ }
                _messageListener = null;
            }

            _messageListeningCts?.Cancel();
            _messageListeningCts = new CancellationTokenSource();
            var localToken = _messageListeningCts.Token;

            _activeListeningRoomId = chatRoomId;

            var messagesRef = _firestoreDb
                .Collection("messages")
                .Document(chatRoomId)
                .Collection("messages");

            var query = messagesRef.OrderBy("Timestamp");

            _messageListener = query.Listen(snapshot =>
            {
                // ❗ Nếu user đã đổi phòng hoặc cancel rồi, thì bỏ qua dữ liệu nhận được
                if (chatRoomId != _activeListeningRoomId || localToken.IsCancellationRequested)
                    return;

                foreach (var docChange in snapshot.Changes)
                {
                    if (docChange.ChangeType == Google.Cloud.Firestore.DocumentChange.Type.Added)
                    {
                        var dict = docChange.Document.ToDictionary();
                        var message = new MessageData
                        {
                            MessageId = docChange.Document.Id,
                            SenderId = dict.TryGetValue("SenderId", out var sid) ? sid as string : null,
                            ReceiverId = dict.TryGetValue("ReceiverId", out var rid) ? rid as string : null,
                            Content = dict.TryGetValue("Content", out var content) ? content as string : null,
                            MessageType = dict.TryGetValue("MessageType", out var type) ? type as string : null,
                            IsSeen = dict.TryGetValue("IsSeen", out var seenObj) && seenObj is bool seen && seen,
                            FileUrl = dict.TryGetValue("FileUrl", out var fileObj) ? fileObj as string : null
                        };

                        if (dict.TryGetValue("Timestamp", out var tsObj))
                        {
                            if (tsObj is Timestamp ts)
                                message.Timestamp = ts;
                            else if (tsObj is string tsStr && DateTime.TryParse(tsStr, out var dt))
                                message.Timestamp = Timestamp.FromDateTime(dt.ToUniversalTime());
                        }

                        _ = onMessageReceived?.Invoke(message); // không await vì Firestore SDK yêu cầu non-blocking
                    }
                }
            });
        }


        public async Task StopListeningToMessagesAsync()
        {
            if (_messageListener != null)
            {
                try { await _messageListener.StopAsync(); } catch { /* Ignore */ }
                _messageListener = null;
            }

            _messageListeningCts?.Cancel();
            _messageListeningCts = null;
            _activeListeningRoomId = null;
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

        public async Task DeleteGroupAsync(string groupId)
        {
            var groupRef = _firestoreDb.Collection("groups").Document(groupId);
            var messagesRef = _firestoreDb.Collection("messages").Document(groupId).Collection("messages");

            // Kiểm tra nhóm có tồn tại
            var groupSnapshot = await groupRef.GetSnapshotAsync();
            if (!groupSnapshot.Exists)
                throw new Exception("Nhóm không tồn tại.");

            // 1. Xóa toàn bộ tin nhắn trong nhóm (song song)
            var messagesSnapshot = await messagesRef.GetSnapshotAsync();
            var deleteTasks = messagesSnapshot.Documents
                .Select(doc => doc.Reference.DeleteAsync());

            await Task.WhenAll(deleteTasks);

            // 2. Xoá document metadata chatroom nếu có
            await _firestoreDb.Collection("messages").Document(groupId).DeleteAsync();

            // 3. Xoá document nhóm
            await groupRef.DeleteAsync();

            Console.WriteLine($"✅ Nhóm {groupId} và toàn bộ tin nhắn đã được xóa.");
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

                if (doc.ContainsField("pendingMembers"))
                {
                    var pendingMap = doc.GetValue<Dictionary<string, object>>("pendingMembers");
                    foreach (var key in pendingMap.Keys)
                        result.Add(key);
                }
            }

            return result.ToList();
        }


        public async Task<List<string>> GetGroupPendingMembersAsync(string groupId)
        {
            var doc = await _firestoreDb.Collection("groups").Document(groupId).GetSnapshotAsync();
            if (doc.Exists && doc.ContainsField("pendingMembers"))
            {
                var pendingMap = doc.GetValue<Dictionary<string, object>>("pendingMembers");
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

            var pendingMembers = groupSnap.ContainsField("pendingMembers")
                ? groupSnap.GetValue<Dictionary<string, object>>("pendingMembers").ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                : new Dictionary<string, string>();

            // Nếu đã là thành viên => không làm gì
            if (members.ContainsKey(targetUserId))
                throw new Exception("User is already a member.");

            // Kiểm tra người mời có phải admin không
            bool isInviterAdmin = members.TryGetValue(inviterId, out var role) && role == "admin";

            var Inviter = await GetUserAsync(inviterId);
            var TargetUser = await GetUserAsync(targetUserId);

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
                    { "pendingMembers", pendingMembers },
                    { "memberCount", members.Count }
                };

                await groupRef.UpdateAsync(updates);
                await SendSystemMessageToChatAsync(groupId, $"👥 {Inviter.DisplayName} đã thêm {TargetUser.DisplayName} vào nhóm.");
            }
            else
            {
                // Nếu người thường mời => check đã được mời chưa
                if (pendingMembers.ContainsKey(targetUserId))
                    throw new Exception("User has already been invited.");

                pendingMembers[targetUserId] = "invited";

                await groupRef.UpdateAsync("pendingMembers", pendingMembers);
                await SendSystemMessageToChatAsync(groupId, $"👥 {Inviter.DisplayName} đã mời {TargetUser.DisplayName} tham gia nhóm.");
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

            var pendingMembers = snapshot.ContainsField("pendingMembers")
                ? snapshot.GetValue<Dictionary<string, object>>("pendingMembers").ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                : new Dictionary<string, string>();

            if (!pendingMembers.ContainsKey(userId))
                throw new Exception("User is not in pending list.");

            // Thêm vào members, xoá khỏi pending
            members[userId] = "member";
            pendingMembers.Remove(userId);

            await groupRef.UpdateAsync(new Dictionary<string, object>
            {
                { "members", members },
                { "pendingMembers", pendingMembers },
                { "memberCount", members.Count }
            });
        }

        public async Task<GroupData> GetGroupAsync(string groupId)
        {
            var docRef = _firestoreDb.Collection("groups").Document(groupId);
            var snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists)
                throw new Exception("Group not found.");

            var data = snapshot.ToDictionary();

            var groupData = new GroupData
            {
                GroupId = snapshot.Id,
                Name = data.ContainsKey("name") ? data["name"].ToString() : "[No name]",
                CreatedBy = data.ContainsKey("createdBy") ? data["createdBy"].ToString() : "",
                Avatar = data.ContainsKey("avatar") ? data["avatar"].ToString() : "Icons/group.png",
                MemberCount = data.ContainsKey("memberCount") ? Convert.ToInt32(data["memberCount"]) : 0,
                Members = new Dictionary<string, string>(),
                PendingMembers = new Dictionary<string, string>()
            };

            // Xử lý trường "members"
            if (snapshot.ContainsField("members"))
            {
                var membersMap = snapshot.GetValue<Dictionary<string, object>>("members");
                groupData.Members = membersMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
                Console.WriteLine($"[DEBUG] Loaded members: {string.Join(", ", groupData.Members.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            }
            else
            {
                Console.WriteLine("[DEBUG] No members found in group document.");
            }

            // Xử lý trường "pending members"
            if (snapshot.ContainsField("pendingMembers"))
            {
                var pendingMap = snapshot.GetValue<Dictionary<string, object>>("pendingMembers");
                groupData.PendingMembers = pendingMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
                Console.WriteLine($"[DEBUG] Loaded pending members: {string.Join(", ", groupData.PendingMembers.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            }
            else
            {
                Console.WriteLine("[DEBUG] No pending members found in group document.");
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
                var rawMembers = snapshot.GetValue<Dictionary<string, object>>("members");
                var members = rawMembers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

                if (!members.ContainsKey(oldAdminId))
                    throw new Exception("Admin cũ không tồn tại."); // Lỗi nằm ở đây nếu bạn đã xoá trước

                if (!members.ContainsKey(newAdminId))
                    throw new Exception("Admin mới không tồn tại.");

                members[oldAdminId] = "member";
                members[newAdminId] = "admin";

                transaction.Update(groupRef, new Dictionary<string, object>
        {
                    { "members", members },
                    { "createdBy", newAdminId },
                    { "memberCount", members.Count }
                });
            });
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


        private Dictionary<string, FirestoreChangeListener> _individualGroupListeners = new Dictionary<string, FirestoreChangeListener>();

        public async Task ListenToEachUserGroupAsync(List<string> groupIds, Action<GroupData> onGroupChanged)
        {
            // Stop previous listeners
            foreach (var listener in _individualGroupListeners.Values)
                await listener.StopAsync();

            _individualGroupListeners.Clear();

            foreach (var groupId in groupIds)
            {
                var docRef = _firestoreDb.Collection("groups").Document(groupId);
                var listener = docRef.Listen(snapshot =>
                {
                    if (!snapshot.Exists) return;

                    var data = snapshot.ToDictionary();

                    var members = snapshot.ContainsField("members")
                        ? snapshot.GetValue<Dictionary<string, object>>("members")
                        : new Dictionary<string, object>();

                    var pending = snapshot.ContainsField("pending members")
                        ? snapshot.GetValue<Dictionary<string, object>>("pending members")
                        : new Dictionary<string, object>();

                    Timestamp createdAt = Timestamp.FromDateTime(DateTime.UtcNow);
                    if (data.ContainsKey("createdAt") && data["createdAt"] is Timestamp ts)
                        createdAt = ts;

                    var group = new GroupData
                    {
                        GroupId = snapshot.Id,
                        Name = data.ContainsKey("name") ? data["name"]?.ToString() : "[No Name]",
                        Avatar = data.ContainsKey("avatar") ? data["avatar"]?.ToString() : "Icons/group.png",
                        CreatedBy = data.ContainsKey("createdBy") ? data["createdBy"]?.ToString() : "",
                        CreatedAt = createdAt,
                        MemberCount = data.ContainsKey("memberCount") ? Convert.ToInt32(data["memberCount"]) : 0,
                        Members = members.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()),
                        PendingMembers = pending.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                    };

                    onGroupChanged?.Invoke(group);
                });

                _individualGroupListeners[groupId] = listener;
            }
        }

        public async Task StopListeningToEachUserGroupAsync()
        {
            foreach (var listener in _individualGroupListeners.Values)
                await listener.StopAsync();
            _individualGroupListeners.Clear();
        }


        private FirestoreChangeListener _groupChangeListener;

        public async Task ListenToUserRelatedGroupsAsync(string userId, Action<List<GroupData>> onGroupsChanged)
        {
            // Ngừng listener cũ nếu có
            if (_groupChangeListener != null)
            {
                await _groupChangeListener.StopAsync();
                _groupChangeListener = null;
            }

            var groupsRef = _firestoreDb.Collection("groups");

            _groupChangeListener = groupsRef.Listen(async snapshot =>
            {
                var updatedGroups = new List<GroupData>();

                foreach (var doc in snapshot.Documents)
                {
                    if (!doc.Exists) continue;

                    var data = doc.ToDictionary();

                    // Lọc nhóm có liên quan đến userId (member hoặc pending)
                    var members = doc.ContainsField("members")
                        ? doc.GetValue<Dictionary<string, object>>("members")
                        : new Dictionary<string, object>();

                    var pending = doc.ContainsField("pendingMembers")
                        ? doc.GetValue<Dictionary<string, object>>("pendingMembers")
                        : new Dictionary<string, object>();

                    bool isRelevant = members.ContainsKey(userId) || pending.ContainsKey(userId);
                    if (!isRelevant) continue;

                    // Parse GroupData
                    Timestamp createdAt = Timestamp.FromDateTime(DateTime.UtcNow); // fallback mặc định

                    if (data.ContainsKey("createdAt") && data["createdAt"] is Timestamp ts)
                    {
                        createdAt = ts;
                    }

                    var group = new GroupData
                    {
                        GroupId = doc.Id,
                        Name = data.ContainsKey("name") ? data["name"]?.ToString() : "[No Name]",
                        Avatar = data.ContainsKey("avatar") ? data["avatar"]?.ToString() : "Icons/group.png",
                        CreatedBy = data.ContainsKey("createdBy") ? data["createdBy"]?.ToString() : "",
                        CreatedAt = createdAt,
                        MemberCount = data.ContainsKey("memberCount") ? Convert.ToInt32(data["memberCount"]) : 0,
                        Members = members.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()),
                        PendingMembers = pending.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString())
                    };


                    updatedGroups.Add(group);
                }

                onGroupsChanged(updatedGroups);
            });
        }

        public async Task StopListeningToUserRelatedGroupsAsync()
        {
            if (_groupChangeListener != null)
            {
                await _groupChangeListener.StopAsync();
                _groupChangeListener = null;
            }
        }




        public FirestoreDb GetDb() => _firestoreDb;

        // =======================================================
        // CÁC PHƯƠNG THỨC CHO TÍNH NĂNG GỌI ĐIỆN (AGORA)
        // =======================================================

        private FirestoreChangeListener _callListener;

        public async Task InitiateCallAsync(CallData callData)
        {
            try
            {
                var callDoc = _firestoreDb.Collection("calls").Document(callData.ChannelName);
                await callDoc.SetAsync(callData);
                Console.WriteLine($"[Firebase] Cuộc gọi {callData.ChannelName} đã được khởi tạo thành công.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] LỖI khi khởi tạo cuộc gọi: {ex.Message}");
                // Ném lại lỗi để nơi gọi (StartCallButton_Click) có thể bắt và thông báo cho người dùng
                throw;
            }
        }

        public async Task UpdateCallStatusAsync(string channelName, string status)
        {
            try
            {
                DocumentReference callRef = _firestoreDb.Collection("calls").Document(channelName);
                await callRef.UpdateAsync("Status", status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not update call status (might be normal if call ended): {ex.Message}");
            }
        }

        public async Task EndCallAsync(string channelName)
        {
            try
            {
                // Xóa document cuộc gọi khi kết thúc để dọn dẹp
                DocumentReference callRef = _firestoreDb.Collection("calls").Document(channelName);
                await callRef.DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not delete call document (might be normal if call ended): {ex.Message}");
            }
        }

        public void ListenForIncomingCall(string userId, Action<CallData> onIncomingCall)
        {
            var callRef = _firestoreDb.Collection("calls")
                .WhereEqualTo("ReceiverId", userId)
                .WhereEqualTo("Status", "calling");

            _callListener = callRef.Listen(snapshot =>
            {
                foreach (var change in snapshot.Changes)
                {
                    if (change.Document.Exists)
                    {
                        var callData = change.Document.ConvertTo<CallData>();
                        if (callData.Status == "calling")
                        {
                            onIncomingCall?.Invoke(callData);
                        }
                    }
                }
            });
        }

        // Lắng nghe thay đổi của một cuộc gọi cụ thể (ví dụ: đối phương cúp máy)
        public FirestoreChangeListener ListenForCallStatusChange(string channelName, Action<CallData> onStatusChanged)
        {
            DocumentReference callRef = _firestoreDb.Collection("calls").Document(channelName);
            return callRef.Listen(snapshot =>
            {
                if (snapshot.Exists)
                {
                    var call = snapshot.ConvertTo<CallData>();
                    onStatusChanged?.Invoke(call);
                }
                else
                {
                    // Document không còn tồn tại -> cuộc gọi đã kết thúc
                    onStatusChanged?.Invoke(null);
                }
            });
        }

        public async Task<CallData> GetCallAsync(string channelName)
        {
            try
            {
                DocumentReference callRef = _firestoreDb.Collection("calls").Document(channelName);
                DocumentSnapshot snapshot = await callRef.GetSnapshotAsync();
                if (snapshot.Exists)
                {
                    return snapshot.ConvertTo<CallData>();
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get call data: {ex.Message}");
                return null;
            }
        }

        public async Task StopListeningForCalls()
        {
            if (_callListener != null)
            {
                await _callListener.StopAsync();
                _callListener = null;
            }
        }
    }
}