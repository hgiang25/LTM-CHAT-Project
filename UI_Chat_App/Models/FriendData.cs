using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class FriendData
    {
        [FirestoreProperty]
        public string FriendId { get; set; }

        [FirestoreProperty]
        public string Status { get; set; } // "accepted", "pending"

        [FirestoreProperty]
        public Timestamp AddedAt { get; set; }

        [FirestoreProperty]
        public int Priority { get; set; } = 0; // Mặc định là 0, cao hơn thì ưu tiên hơn

        [FirestoreProperty]
        public bool Blocked { get; set; } = false; // ✅ Thêm trường Blocked
    }

}