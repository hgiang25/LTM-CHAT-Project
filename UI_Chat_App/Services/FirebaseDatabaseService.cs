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
                await docRef.SetAsync(message, SetOptions.Overwrite); // Cập nhật lại document với MessageId
                Console.WriteLine($"Saved message with ID {message.MessageId} to chat room {chatRoomId}");
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save message to Firestore: " + ex.Message, ex);
            }
        }

        public async Task<List<MessageData>> GetMessagesAsync(string chatRoomId)
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
                        message.MessageId = doc.Id; // Gán MessageId từ ID của document
                        return message;
                    })
                    .OrderBy(m => DateTime.Parse(m.Timestamp))
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


        

    }
}