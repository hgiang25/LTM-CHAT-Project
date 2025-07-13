using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class FriendRequest
    {
        [FirestoreProperty]
        public string RequestId { get; set; }

        [FirestoreProperty]
        public string FromUserId { get; set; }

        [FirestoreProperty]
        public string ToUserId { get; set; }

        [FirestoreProperty]
        public string Status { get; set; } // "pending", "accepted", "rejected"

        [FirestoreProperty]
        public Timestamp? CreatedAt { get; set; }
    }
}