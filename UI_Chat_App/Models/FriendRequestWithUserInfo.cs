namespace ChatApp.Models
{
    /// <summary>
    /// Lớp này chứa thông tin về lời mời kết bạn cùng với thông tin người gửi
    /// </summary>
    public class FriendRequestWithUserInfo
    {
        /// <summary>
        /// Thông tin lời mời kết bạn
        /// </summary>
        public FriendRequest FriendRequest { get; set; }

        /// <summary>
        /// Thông tin người gửi lời mời
        /// </summary>
        public UserData Sender { get; set; }
    }
}