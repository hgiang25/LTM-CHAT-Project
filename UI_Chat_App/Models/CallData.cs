using Google.Cloud.Firestore;
namespace ChatApp.Models
{
    [FirestoreData]
    public class CallData
    {
        [FirestoreDocumentId]
        public string ChannelName { get; set; }
        [FirestoreProperty]
        public string CallerId { get; set; }
        [FirestoreProperty]
        public string CallerName { get; set; }
        [FirestoreProperty]
        public string ReceiverId { get; set; }
        [FirestoreProperty]
        public string Status { get; set; }
        [FirestoreProperty]
        public string Token { get; set; }
    }
}