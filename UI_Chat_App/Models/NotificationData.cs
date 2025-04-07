using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class NotificationData
    {
        [FirestoreProperty]
        public string Type { get; set; }

        [FirestoreProperty]
        public string From { get; set; }

        [FirestoreProperty]
        public string Content { get; set; }

        [FirestoreProperty]
        public string Timestamp { get; set; }

        [FirestoreProperty]
        public bool IsRead { get; set; }
    }
}