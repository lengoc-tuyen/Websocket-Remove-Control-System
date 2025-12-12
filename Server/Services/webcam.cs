using System;
using System.Collections.Generic; 
using System.Diagnostics; 
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading; 
using System.Threading.Tasks; 
using OpenCvSharp;
using Server.helper; 
using static System.Runtime.InteropServices.RuntimeInformation;

namespace Server.Services
{
    public class WebcamService
    {
        // Đưa P/Invoke vào lớp riêng để tránh lỗi runtime trên Mac/Linux
        private static class Win32Native
        {
            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int nIndex);
            public const int SM_CXSCREEN = 0;
            public const int SM_CYSCREEN = 1;
        }

        private VideoCapture? _webcamCapture; // Thêm ? để tránh cảnh báo null

        // --- PUBLIC API (Các hàm được gọi từ bên ngoài, ví dụ ControlHub) ---

        // Hàm chụp ảnh màn hình (Snapshot)
        public byte[] captureScreen()
        {   
            return CaptureScreenInternal();
        }

        // Hàm tắt webcam
        public void closeWebcam()
        {
            CloseWebcamInternal();
        }

        // Hàm mở webcam (nếu cần gọi riêng)
        public bool OpenWebcam()
        {
            return OpenWebcamInternal();
        }

        // Hàm yêu cầu quay video bằng chứng (Mở cam -> Quay 3s -> Giữ cam mở)
        public async Task<List<byte[]>> RequestWebcamProof(int frameRate, CancellationToken cancellationToken) 
        {
            return await videoMakerManager(frameRate, cancellationToken);
        }


        // --- PRIVATE HELPERS (Logic thực thi nội bộ) ---

        // Logic thực sự của việc chụp màn hình
        private byte[] CaptureScreenInternal()
        {
            try
            {
                if (IsOSPlatform(OSPlatform.Windows))
                {
                    // Lấy kích thước qua lớp Win32Native an toàn
                    int screenWidth = Win32Native.GetSystemMetrics(Win32Native.SM_CXSCREEN);
                    int screenHeight = Win32Native.GetSystemMetrics(Win32Native.SM_CYSCREEN);

                    using (Bitmap bmp = new Bitmap(screenWidth, screenHeight))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        // SỬA LỖI SIZE: Chỉ định rõ System.Drawing.Size
                        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight), CopyPixelOperation.SourceCopy);
                        
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Jpeg);
                            return ms.ToArray();
                        }
                    }
                }
                else if (IsOSPlatform(OSPlatform.OSX) || IsOSPlatform(OSPlatform.Linux))
                {
                    string tempFileName = Path.Combine(Path.GetTempPath(), $"screenshot_{Guid.NewGuid()}.jpg");
                    string shellCommand;

                    if (IsOSPlatform(OSPlatform.OSX))
                    {
                        shellCommand = $"screencapture -t jpg \"{tempFileName}\"";
                    }
                    else
                    {
                        shellCommand = $"gnome-screenshot -f \"{tempFileName}\"";
                    }

                    // Gọi ShellUtils (Cần đảm bảo file ShellUtils.cs có namespace Server.helper)
                    // Đã sửa: Chỉ truyền 1 tham số command
                    ShellUtils.ExecuteShellCommand(shellCommand); 
                    
                    if (File.Exists(tempFileName))
                    {
                        byte[] imageBytes = File.ReadAllBytes(tempFileName);
                        File.Delete(tempFileName);
                        return imageBytes;
                    }
                    else
                    {
                        Console.WriteLine($"Error: Shell command failed to create screenshot file: {tempFileName}");
                        return Encoding.UTF8.GetBytes("SCREEN_CAPTURE_FAILED");
                    }
                }
                else
                {
                    return Encoding.UTF8.GetBytes("OS_NOT_SUPPORTED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error capturing screen: {ex.Message}");
            }
            return Array.Empty<byte>();
        }

        // Logic đóng webcam
        private void CloseWebcamInternal()
        {
            if (_webcamCapture != null)
            {
                if (_webcamCapture.IsOpened())
                {
                    _webcamCapture.Release();
                }
                
                _webcamCapture.Dispose();
                _webcamCapture = null;
                Console.WriteLine("Webcam closed successfully.");
            }
        }

        // Logic mở webcam
        private bool OpenWebcamInternal()
        {
            // Nếu đã mở thì dùng luôn
            if (_webcamCapture != null && _webcamCapture.IsOpened())
            {
                return true;
            }
            
            // Dispose cái cũ nếu có
            _webcamCapture?.Dispose();

            _webcamCapture = new VideoCapture(0);   

            if (!_webcamCapture.IsOpened())
            {
                _webcamCapture.Dispose();
                _webcamCapture = null;
                return false;
            }

            _webcamCapture.Set(VideoCaptureProperties.FrameWidth, 640);
            _webcamCapture.Set(VideoCaptureProperties.FrameHeight, 480);
            
            return true;
        }

        // Hàm hỗ trợ chụp 1 frame từ webcam
        private byte[]? captureForVideo()
        {
            if (_webcamCapture == null || !_webcamCapture.IsOpened())
            {
                return null;
            }
            
            using (Mat frame = new Mat())
            {
                if (!_webcamCapture.Read(frame) || frame.Empty())
                {
                    Console.WriteLine("Failed to read frame from webcam.");
                    return null;
                }

                // --- SỬA LỖI TẠI ĐÂY ---
                // Thay vì dùng Mat encodedFrame, ta dùng out byte[] trực tiếp
                // Hàm ImEncode trả về byte[] thông qua tham số out
                
                byte[] encodedBytes;
                if (Cv2.ImEncode(".jpg", frame, out encodedBytes))
                {
                    return encodedBytes;
                }
                
                return null;
            }
        }

        // Logic quản lý quy trình quay video bằng chứng
        private async Task<List<byte[]>> videoMakerManager(int frameRate, CancellationToken cancellationToken)
        {
            if (!OpenWebcamInternal())
            {
                Console.WriteLine("Failed to open webcam for proof.");
                return new List<byte[]>();
            }

            int durationMs = 3000; // 3 giây
            List<byte[]> proofFrames = await VideoMakerLoop(durationMs, frameRate, cancellationToken);            
            return proofFrames;
        }

        // Vòng lặp quay video
        private async Task<List<byte[]>> VideoMakerLoop(int durationMs, int frameRate, CancellationToken cancellationToken)
        {
            List<byte[]> frames = new List<byte[]>();
            
            if (_webcamCapture == null || !_webcamCapture.IsOpened()) return frames;

            int delayMs = 1000 / frameRate; 
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                while (stopwatch.ElapsedMilliseconds < durationMs && !cancellationToken.IsCancellationRequested)
                {
                    byte[]? frameData = captureForVideo();
                    if (frameData != null && frameData.Length > 0) frames.Add(frameData);
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            finally
            {
                stopwatch.Stop();
            }

            return frames;
        }
    }
}