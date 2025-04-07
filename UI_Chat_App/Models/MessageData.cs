using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class MessageData
    {
        [FirestoreProperty]
        public string MessageId { get; set; } // Thêm MessageId để lưu ID của document

        [FirestoreProperty]
        public string SenderId { get; set; }

        [FirestoreProperty]
        public string ReceiverId { get; set; }

        [FirestoreProperty]
        public string Content { get; set; }

        [FirestoreProperty]
        public string Timestamp { get; set; }

        [FirestoreProperty]
        public string MessageType { get; set; } // "Text", "Image", "File", "Voice"

        [FirestoreProperty]
        public string FileUrl { get; set; }

        [FirestoreProperty]
        public string FileName { get; set; }

        [FirestoreProperty]
        public bool IsSeen { get; set; } // Thêm thuộc tính IsSeen

        // Constructor không tham số
        public MessageData()
        {
        }
    }
}