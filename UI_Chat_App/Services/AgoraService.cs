using System;
using Agora.Rtc;

namespace ChatApp.Services
{
    // Class xử lý sự kiện từ Agora
    internal class AgoraRtcEventHandler : IRtcEngineEventHandler
    {
        private readonly AgoraService _agoraService;

        internal AgoraRtcEventHandler(AgoraService agoraService)
        {
            _agoraService = agoraService;
        }

        public override void OnJoinChannelSuccess(RtcConnection connection, int elapsed)
        {
            Console.WriteLine("Successfully joined channel!");
        }

        public override void OnLeaveChannel(RtcConnection connection, RtcStats stats)
        {
            Console.WriteLine("Successfully left channel!");
        }

        public override void OnUserJoined(RtcConnection connection, uint remoteUid, int elapsed)
        {
            _agoraService.OnRemoteUserJoined?.Invoke(remoteUid, elapsed);
        }

        public override void OnUserOffline(RtcConnection connection, uint remoteUid, USER_OFFLINE_REASON_TYPE reason)
        {
            _agoraService.OnRemoteUserOffline?.Invoke(remoteUid, reason);
        }
    }

    // Class AgoraService chính
    public sealed class AgoraService
    {
        private static readonly AgoraService instance = new AgoraService();
        public static AgoraService Instance => instance;
        private AgoraService() { }

        private IRtcEngine _rtcEngine;
        private IRtcEngineEventHandler _eventHandler;

        private string _appId = "339d1b8c4e6445019455c505b1a4e560"; // App ID của bạn

        public bool IsInCall { get; private set; } = false;

        public Action<uint, int> OnRemoteUserJoined;
        public Action<uint, USER_OFFLINE_REASON_TYPE> OnRemoteUserOffline;

        public static uint GenerateUid(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return 0;
            // Dùng GetHashCode và & 0x7FFFFFFF để đảm bảo UID là số dương 32-bit
            return (uint)(userId.GetHashCode() & 0x7FFFFFFF);
        }

        // Giữ nguyên nội dung gốc của Initialize()
        public bool Initialize()
        {
            if (_rtcEngine != null) return true; // Đã khởi tạo

            try
            {
                _rtcEngine = RtcEngine.CreateAgoraRtcEngine();
                RtcEngineContext context = new RtcEngineContext()
                {
                    appId = _appId,
                    channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING,
                    audioScenario = AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_DEFAULT
                };

                if (_rtcEngine.Initialize(context) != 0)
                {
                    throw new Exception("Agora RTC Engine Initialize failed.");
                }

                _eventHandler = new AgoraRtcEventHandler(this);
                _rtcEngine.InitEventHandler(_eventHandler);

                _rtcEngine.EnableAudio();
                _rtcEngine.EnableVideo();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Agora Initialization failed: {ex.Message}");
                if (_rtcEngine != null)
                {
                    _rtcEngine.Dispose();
                    _rtcEngine = null;
                }
                return false;
            }
        }

        // Giữ nguyên nội dung gốc của HasRequiredDevices()
        public bool HasRequiredDevices()
        {
            if (_rtcEngine == null)
            {
                Console.WriteLine("RTC Engine is null. Initializing now...");
                Initialize();
                if (_rtcEngine == null)
                {
                    Console.WriteLine("Failed to initialize RTC Engine.");
                    return false;
                }
            }

            try
            {
                int videoCount = 0;
                int audioCount = 0;

                // --- BƯỚC 1: KIỂM TRA THIẾT BỊ VIDEO ---
                IVideoDeviceManager videoDeviceManager = _rtcEngine.GetVideoDeviceManager();
                if (videoDeviceManager != null)
                {
                    IDeviceCollection videoDevices = videoDeviceManager.EnumerateVideoDevices();
                    if (videoDevices != null)
                    {
                        videoCount = videoDevices.GetCount();
                        videoDevices.Release();
                    }
                    else
                    {
                        Console.WriteLine("Failed to enumerate video devices.");
                    }
                    videoDeviceManager.Release();
                }
                else
                {
                    Console.WriteLine("VideoDeviceManager is null.");
                }

                // --- BƯỚC 2: KIỂM TRA THIẾT BỊ AUDIO ---
                IAudioDeviceManager audioDeviceManager = _rtcEngine.GetAudioDeviceManager();
                if (audioDeviceManager != null)
                {
                    IDeviceCollection audioRecordingDevices = audioDeviceManager.EnumerateRecordingDevices();
                    if (audioRecordingDevices != null)
                    {
                        audioCount = audioRecordingDevices.GetCount();
                        audioRecordingDevices.Release();
                    }
                    else
                    {
                        Console.WriteLine("Failed to enumerate audio recording devices.");
                    }
                    audioDeviceManager.Release();
                }
                else
                {
                    Console.WriteLine("AudioDeviceManager is null.");
                }

                Console.WriteLine($"[Agora Service] Devices found: {videoCount} video, {audioCount} audio.");
                return videoCount > 0 && audioCount > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agora Service] Error checking devices: {ex.Message} - StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        public void SetupLocalVideo(System.Windows.Forms.Panel videoPanel)
        {
            if (_rtcEngine == null) Initialize();

            var canvas = new VideoCanvas
            {
                view = videoPanel.Handle.ToInt64(),
                renderMode = RENDER_MODE_TYPE.RENDER_MODE_FIT,
                uid = 0
            };

            _rtcEngine.SetupLocalVideo(canvas);
            _rtcEngine.StartPreview();
        }

        public void SetupRemoteVideo(uint remoteUid, System.Windows.Forms.Panel videoPanel)
        {
            if (_rtcEngine == null) return;

            var canvas = new VideoCanvas
            {
                view = videoPanel.Handle.ToInt64(),
                renderMode = RENDER_MODE_TYPE.RENDER_MODE_FIT,
                uid = remoteUid
            };

            _rtcEngine.SetupRemoteVideo(canvas);
        }

        public int JoinChannel(string channelName, string token = null, uint uid = 0)
        {
            if (_rtcEngine == null) Initialize();
            int result = _rtcEngine.JoinChannel(token, channelName, "", uid);
            IsInCall = (result == 0);
            return result;
        }

        public void LeaveChannel()
        {
            if (_rtcEngine != null && IsInCall)
            {
                _rtcEngine.StopPreview();
                _rtcEngine.LeaveChannel();
                IsInCall = false;
            }
        }

        public void MuteLocalAudio(bool muted) => _rtcEngine?.MuteLocalAudioStream(muted);

        public void EnableLocalVideo(bool enabled)
        {
            _rtcEngine?.EnableLocalVideo(enabled);
            if (enabled) _rtcEngine.StartPreview(); else _rtcEngine.StopPreview();
        }

        public void Dispose()
        {
            if (_rtcEngine != null)
            {
                _rtcEngine.Dispose();
                _rtcEngine = null;
            }
        }

        public void StopPreview()
        {
            if (_rtcEngine != null)
            {
                _rtcEngine.StopPreview();
            }
        }
    }
}