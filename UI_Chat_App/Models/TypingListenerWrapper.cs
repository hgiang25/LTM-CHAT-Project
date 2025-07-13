using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace UI_Chat_App.Models
{
    public class TypingListenerWrapper
    {
        public FirestoreChangeListener Listener { get; set; }
        public bool IsStopped { get; private set; }

        public async Task StopAsync()
        {
            if (!IsStopped && Listener != null)
            {
                await Listener.StopAsync();
                IsStopped = true;
            }
        }
    }

}
