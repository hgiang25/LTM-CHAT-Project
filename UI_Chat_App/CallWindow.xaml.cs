using ChatApp.Models;
using ChatApp.Services;
using Google.Cloud.Firestore;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UI_Chat_App
{
    public partial class CallWindow : Window
    {
        private readonly CallData _callData;
        private readonly FirebaseDatabaseService _databaseService;
        private FirestoreChangeListener _statusListener;

        private readonly Panel _localVideoPanel;
        private readonly Panel _remoteVideoPanel;

        private bool _isMuted = false;
        private bool _isVideoDisabled = false;
        private bool _isCleaningUp = false;
        private DispatcherTimer _callTimeoutTimer;

        public CallWindow(CallData callData, bool isCaller)
        {
            InitializeComponent();
            _callData = callData;
            _databaseService = new FirebaseDatabaseService();

            _localVideoPanel = new Panel();
            LocalVideoHost.Child = _localVideoPanel;
            _remoteVideoPanel = new Panel();
            RemoteVideoHost.Child = _remoteVideoPanel;

            UpdateCallerInfo(isCaller, callData);

            if (isCaller)
            {
                SetupCallTimeout();
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _statusListener = _databaseService.ListenForCallStatusChange(_callData.ChannelName, OnCallStatusChanged);

                AgoraService.Instance.OnRemoteUserJoined = (remoteUid, elapsed) =>
                {
                    Dispatcher.Invoke(() => AgoraService.Instance.SetupRemoteVideo(remoteUid, _remoteVideoPanel));
                };

                AgoraService.Instance.OnRemoteUserOffline = (remoteUid, reason) =>
                {
                    _databaseService.UpdateCallStatusAsync(_callData.ChannelName, "ended").FireAndForget();
                };

                AgoraService.Instance.SetupLocalVideo(_localVideoPanel);

                uint myUid = AgoraService.GenerateUid(App.CurrentUser.Id);
                int joinResult = AgoraService.Instance.JoinChannel(_callData.ChannelName, null, myUid);
                if (joinResult != 0)
                {
                    System.Windows.MessageBox.Show($"Không thể tham gia kênh, mã lỗi: {joinResult}", "Lỗi Agora");
                    EndCallCleanup();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi tải cửa sổ gọi: {ex.Message}");
                EndCallCleanup();
            }
        }

        private void OnCallStatusChanged(CallData updatedCall)
        {
            Dispatcher.Invoke(() =>
            {
                if (!this.IsLoaded || _isCleaningUp) return;

                if (updatedCall?.Status != "calling")
                {
                    _callTimeoutTimer?.Stop();
                }

                if (updatedCall == null || updatedCall.Status == "ended" || updatedCall.Status == "rejected" || updatedCall.Status == "missed")
                {
                    StatusText.Text = $"Cuộc gọi đã {updatedCall?.Status ?? "kết thúc"}.";
                    EndCallCleanup();
                }
                else if (updatedCall.Status == "ongoing")
                {
                    StatusText.Text = "Đã kết nối";
                }
            });
        }

        private void HangUpButton_Click(object sender, RoutedEventArgs e)
        {
            _databaseService.UpdateCallStatusAsync(_callData.ChannelName, "ended").FireAndForget();
        }

        private void EndCallCleanup()
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;

            try
            {
                _callTimeoutTimer?.Stop();
                _statusListener?.StopAsync();

                if (AgoraService.Instance.IsInCall)
                {
                    AgoraService.Instance.StopPreview();
                    AgoraService.Instance.LeaveChannel();
                }

                AgoraService.Instance.OnRemoteUserJoined = null;
                AgoraService.Instance.OnRemoteUserOffline = null;

                _databaseService.EndCallAsync(_callData.ChannelName).FireAndForget();
            }
            finally
            {
                this.Close();
            }
        }

        private void SetupCallTimeout()
        {
            _callTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _callTimeoutTimer.Tick += async (s, args) =>
            {
                _callTimeoutTimer.Stop();
                var currentCall = await _databaseService.GetCallAsync(_callData.ChannelName);
                if (currentCall != null && currentCall.Status == "calling")
                {
                    await _databaseService.UpdateCallStatusAsync(_callData.ChannelName, "missed");
                }
            };
            _callTimeoutTimer.Start();
        }

        private async void UpdateCallerInfo(bool isCaller, CallData callData)
        {
            if (isCaller)
            {
                var receiver = await _databaseService.GetUserAsync(callData.ReceiverId);
                Dispatcher.Invoke(() => {
                    CallerNameText.Text = receiver?.DisplayName ?? "Unknown User";
                    StatusText.Text = "Đang gọi...";
                });
            }
            else
            {
                CallerNameText.Text = callData.CallerName;
                StatusText.Text = "Cuộc gọi đến";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => EndCallCleanup();
        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            AgoraService.Instance.MuteLocalAudio(_isMuted);
            MuteIcon.Source = new BitmapImage(new Uri($"pack://application:,,,/Icons/{(_isMuted ? "mic_off" : "mic_on")}.png", UriKind.Relative));
        }
        private void VideoButton_Click(object sender, RoutedEventArgs e)
        {
            _isVideoDisabled = !_isVideoDisabled;
            AgoraService.Instance.EnableLocalVideo(!_isVideoDisabled);
            VideoIcon.Source = new BitmapImage(new Uri($"pack://application:,,,/Icons/{(!_isVideoDisabled ? "video_on" : "video_off")}.png", UriKind.Relative));
        }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }
    }

    public static class TaskExtensions
    {
        public static void FireAndForget(this Task task) { }
    }
}