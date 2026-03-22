using OpenCvSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Size = System.Drawing.Size;

namespace RtspTest
{
    public partial class Form1 : Form
    {
        private VideoCapture? capture;
        private bool isRunning = false;
        private CancellationTokenSource? cts;
        private readonly object sync = new();
        private Bitmap? reusableBitmap = null;
        private bool stretchToFill = false;

        public Form1()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(640, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Resize += (s, e) => RefreshDisplay();

            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS",
                "rtsp_transport;tcp;timeout;15000000;stimeout;8000000;reconnect;1;reconnect_streamed;1;" +
                "reconnect_delay_max;4;analyzeduration;3000000;probesize;12000000;fflags;nobuffer;flags;low_delay;" +
                "strict;experimental;color_range;pc;colorspace;bt709;color_primaries;bt709;color_trc;bt709;pix_fmt;bgr24");

            btnStart.Enabled = true;
            btnStop.Enabled = false;
            pictureBox1.Cursor = Cursors.Hand;
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (isRunning) return;

            string rtspUrl = "rtsp://127.0.0.1:8554/mystream";

            isRunning = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            this.Text = "RTSP — подключаемся...";

            cts = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                try
                {
                    capture = new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);

                    if (!capture.IsOpened())
                    {
                        Log("Не удалось открыть поток");
                        this.InvokeIfNeeded(() => MessageBox.Show("Не удалось открыть RTSP-поток."));
                        return;
                    }

                    Log($"Открыт: {capture.FrameWidth}×{capture.FrameHeight} @ ~{capture.Fps:F1} fps");

                    this.InvokeIfNeeded(() => this.Text = "RTSP — поток идёт");

                    using var frame = new Mat();

                    while (isRunning && !cts!.Token.IsCancellationRequested && !IsDisposed)
                    {
                        if (!capture.Read(frame) || frame.Empty())
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        // ✅ ИСПРАВЛЕНО: простая и надёжная проверка
                        if (frame.Type() != MatType.CV_8UC3)
                        {
                            Log($"Пропущен кадр — формат {frame.Type()}");
                            continue;
                        }

                        this.InvokeIfNeeded(() => UpdateDisplayWithMat(frame));

                        await Task.Delay(33);
                    }
                }
                catch (Exception ex)
                {
                    Log("Критическая ошибка: " + ex.Message);
                    this.InvokeIfNeeded(() => MessageBox.Show(ex.Message));
                }
                finally
                {
                    this.InvokeIfNeeded(StopCapture);
                }
            }, cts.Token);
        }

        private void UpdateDisplayWithMat(Mat mat)
        {
            if (pictureBox1.IsDisposed) return;

            int pw = pictureBox1.ClientSize.Width;
            int ph = pictureBox1.ClientSize.Height;
            if (pw <= 0 || ph <= 0) return;

            if (reusableBitmap == null || reusableBitmap.Width != pw || reusableBitmap.Height != ph)
            {
                reusableBitmap?.Dispose();
                reusableBitmap = new Bitmap(pw, ph, PixelFormat.Format24bppRgb);
            }

            try
            {
                var bmpData = reusableBitmap.LockBits(
                    new Rectangle(0, 0, pw, ph),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    using var target = Mat.FromPixelData(ph, pw, MatType.CV_8UC3, bmpData.Scan0, bmpData.Stride);

                    if (stretchToFill)
                    {
                        // Растягиваем на всё окно (игнорируем пропорции)
                        Cv2.Resize(mat, target, new OpenCvSharp.Size(pw, ph), 0, 0, InterpolationFlags.Linear);
                    }
                    else
                    {
                        // Сохраняем пропорции + центрируем с чёрными полосами
                        double ratio = Math.Min((double)pw / mat.Width, (double)ph / mat.Height);
                        int drawW = (int)(mat.Width * ratio);
                        int drawH = (int)(mat.Height * ratio);
                        int offsetX = (pw - drawW) / 2;
                        int offsetY = (ph - drawH) / 2;

                        target.SetTo(Scalar.Black);  // чёрный фон

                        using var resized = new Mat();
                        Cv2.Resize(mat, resized, new OpenCvSharp.Size(drawW, drawH), 0, 0, InterpolationFlags.Linear);

                        // Копируем в центр
                        var roi = new Rect(offsetX, offsetY, drawW, drawH);
                        resized.CopyTo(target[roi]);
                    }

                    // Если цвета перевёрнуты (синий/красный) — добавь здесь:
                    // Cv2.CvtColor(target, target, ColorConversionCodes.BGR2RGB);
                }
                finally
                {
                    reusableBitmap.UnlockBits(bmpData);
                }

                if (pictureBox1.Image != reusableBitmap)
                {
                    pictureBox1.Image?.Dispose();
                    pictureBox1.Image = reusableBitmap;
                }

                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                Log("Ошибка отрисовки: " + ex.Message);
            }
        }

        private void RefreshDisplay()
        {
            if (reusableBitmap != null)
            {
                pictureBox1.Image = reusableBitmap;
                pictureBox1.Invalidate();
            }
        }

        private void pictureBox1_DoubleClick(object sender, EventArgs e)
        {
            stretchToFill = !stretchToFill;
            this.Text = stretchToFill ? "RTSP — растянуто" : "RTSP — пропорции";
            RefreshDisplay();
        }

        private void btnStop_Click(object sender, EventArgs e) => StopCapture();

        private void StopCapture()
        {
            isRunning = false;
            cts?.Cancel();
            cts?.Dispose();
            cts = null;

            lock (sync)
            {
                capture?.Release();
                capture?.Dispose();
                capture = null;
            }

            this.InvokeIfNeeded(() =>
            {
                reusableBitmap?.Dispose();
                reusableBitmap = null;
                pictureBox1.Image?.Dispose();
                pictureBox1.Image = null;

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                this.Text = "RTSP просмотр";
            });
        }

        private void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {msg}");
        }

        private void InvokeIfNeeded(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();
            base.OnFormClosing(e);
        }
    }
}