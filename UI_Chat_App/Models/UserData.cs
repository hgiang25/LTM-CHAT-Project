﻿using System.ComponentModel;
using Google.Cloud.Firestore;
using UI_Chat_App;

namespace ChatApp.Models
{
    [FirestoreData]
    public class UserData : INotifyPropertyChanged
    {
        private string _id;
        private string _displayName;
        private string _email;
        private string _avatar;
        private bool _isOnline;

        [FirestoreProperty]
        public string Id
        {
            get => _id;
            set
            {
                _id = value;
                RaisePropertyChanged(nameof(Id));
            }
        }

        [FirestoreProperty]
        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                RaisePropertyChanged(nameof(DisplayName));
            }
        }

        [FirestoreProperty]
        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                RaisePropertyChanged(nameof(Email));
            }
        }

        [FirestoreProperty]
        public string Avatar
        {
            get => _avatar;
            set
            {
                _avatar = value;
                RaisePropertyChanged(nameof(Avatar));
            }
        }
        [FirestoreProperty]
        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(nameof(IsOnline)); }
        }       


        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Phương thức để gọi RaisePropertyChanged từ bên ngoài
        public void RaisePropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }

        private object _tag;

        public object Tag
        {
            get => _tag;
            set
            {
                _tag = value;
                RaisePropertyChanged(nameof(Tag));
            }
        }
        private bool _isBlocked;

        public bool IsBlocked
        {
            get => _isBlocked;
            set
            {
                _isBlocked = value;
                OnPropertyChanged(nameof(IsBlocked));
            }
        }


    }
}