using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class NotificationData
    {
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty]
        public string Type { get; set; }

        [FirestoreProperty]
        public string From { get; set; }   // Người gửi (user ID)

        [FirestoreProperty]
        public string To { get; set; }     // Người nhận (user ID)

        [FirestoreProperty]
        public string Content { get; set; }

        [FirestoreProperty]
        public string Timestamp { get; set; }

        [FirestoreProperty]
        public bool IsRead { get; set; }

        // 👇 Thêm 2 thuộc tính mới để hỗ trợ nhóm
        [FirestoreProperty]
        public string GroupId { get; set; } // Nếu thông báo từ nhóm

        [FirestoreProperty]
        public bool IsGroup { get; set; }   // Phân biệt thông báo từ nhóm hay cá nhân
    }

    public class NotificationSummary
    {
        public string SenderId { get; set; }
        public string SenderName { get; set; } // Nếu bạn có tên
        public int UnreadCount { get; set; }
    }
}
