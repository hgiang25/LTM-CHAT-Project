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
    }

}