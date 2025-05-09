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
        public string From { get; set; }

        [FirestoreProperty]
        public string To { get; set; }

        [FirestoreProperty]
        public string Content { get; set; }

        [FirestoreProperty]
        public string Timestamp { get; set; }

        [FirestoreProperty]
        public bool IsRead { get; set; }
    }
    public class NotificationSummary
    {
        public string SenderId { get; set; }
        public string SenderName { get; set; } // Nếu bạn có tên
        public int UnreadCount { get; set; }
    }

}