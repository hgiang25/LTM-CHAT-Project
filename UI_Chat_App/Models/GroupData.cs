using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Google.Cloud.Firestore;

[FirestoreData]
public class GroupData : INotifyPropertyChanged
{
    [FirestoreProperty]
    public string GroupId { get; set; }

    private string _name;
    [FirestoreProperty]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _avatar;
    [FirestoreProperty]
    public string Avatar
    {
        get => _avatar;
        set { _avatar = value; OnPropertyChanged(); }
    }

    [FirestoreProperty]
    public string CreatedBy { get; set; }

    [FirestoreProperty]
    public Timestamp? CreatedAt { get; set; }

    [FirestoreProperty]
    public int MemberCount { get; set; }

    [FirestoreProperty]
    public Dictionary<string, string> Members { get; set; } = new Dictionary<string, string>();

    [FirestoreProperty]
    public Dictionary<string, string> PendingMembers { get; set; } = new Dictionary<string, string>();

    
    public BitmapImage AvatarBitmap { get; set; }


    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
