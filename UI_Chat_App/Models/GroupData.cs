using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace ChatApp.Models
{
    [FirestoreData]
    public class GroupData
    {
        [FirestoreProperty]
        public string GroupId { get; set; }

        [FirestoreProperty]
        public string Name { get; set; }

        [FirestoreProperty]
        public string CreatedBy { get; set; }

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }

        [FirestoreProperty]
        public string Avatar { get; set; }

        [FirestoreProperty]
        public int MemberCount { get; set; }

        [FirestoreProperty]
        public Dictionary<string, string> Members { get; set; } = new Dictionary<string, string>();        // userId => role
    }
}
