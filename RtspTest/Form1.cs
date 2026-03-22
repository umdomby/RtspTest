using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RtspTest
{
    public partial class Form1 : Form
    {
        private VideoCapture? capture;
        private bool isRunning = false;
        private CancellationTokenSource? cts;
        private readonly object sync = new();

        public Form1()
        {
            InitializeComponent();
            this.DoubleBuffered = true;

            // Устанавливаем таймауты для ffmpeg (очень помогает при проблемах с RTSP)
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "timeout;5000000;stimeout;3000000");

            btnStart.Enabled = true;
            btnStop.Enabled = false;
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (isRunning) return;

            string rtspUrl = "rtsp://127.0.0.1:8554/mystream";
            // string rtspUrl = "rtsp://192.168.1.121:8554/mystream";

            isRunning = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            this.Text = "RTSP — подключаемся...";

            cts = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                try
                {
                    capture = new VideoCapture(rtspUrl);

                    if (!capture.IsOpened())
                    {
                        this.InvokeIfNeeded(() =>
                        {
                            MessageBox.Show("Не удалось открыть RTSP-поток.\nЗапущен ли VLC? Правильный ли адрес?");
                            StopCapture();
                        });
                        return;
                    }

                    this.InvokeIfNeeded(() => this.Text = "RTSP — поток идёт");

                    using var frame = new Mat();

                    while (isRunning && !cts.Token.IsCancellationRequested && !this.IsDisposed)
                    {
                        if (!capture.Read(frame) || frame.Empty())
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        using var bmp = frame.ToBitmap();

                        this.InvokeIfNeeded(() =>
                        {
                            if (pictureBox1.Image != null)
                            {
                                pictureBox1.Image.Dispose();
                                pictureBox1.Image = null;  // important to avoid race conditions
                            }

                            try
                            {
                                using var bmp = frame.ToBitmap();
                                if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                                {
                                    // skip bad frame
                                    return;
                                }

                                // Clone is safer after checks
                                pictureBox1.Image = new Bitmap(bmp);  // or bmp.Clone(new Rectangle(0,0,bmp.Width,bmp.Height), bmp.PixelFormat);
                            }
                            catch (Exception ex)
                            {
                                // log or ignore - don't crash whole loop
                                System.Diagnostics.Debug.WriteLine("Bitmap conversion failed: " + ex.Message);
                            }
                        });

                        await Task.Delay(40);   // ≈ 25 fps


                        //this.InvokeIfNeeded(() =>
                        //{
                        //    var old = pictureBox1.Image as Bitmap;           // сохраняем ссылку
                        //    pictureBox1.Image = bmp.Clone() as Bitmap;       // новый клон
                        //    old?.Dispose();                                  // старый убираем после присваивания
                        //});

                        //await Task.Delay(40);   // ≈ 25 fps
                    }
                }
                catch (Exception ex)
                {
                    this.InvokeIfNeeded(() =>
                    {
                        MessageBox.Show("Ошибка при работе с видео:\n" + ex.Message);
                        StopCapture();
                    });
                }
                finally
                {
                    this.InvokeIfNeeded(StopCapture);
                }
            }, cts.Token);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            if (!isRunning) return;

            isRunning = false;
            cts?.Cancel();
            cts?.Dispose();
            cts = null;

            lock (sync)
            {
                if (capture != null)
                {
                    capture.Release();
                    capture.Dispose();
                    capture = null;
                }
            }

            this.InvokeIfNeeded(() =>
            {
                if (pictureBox1.Image != null)
                {
                    pictureBox1.Image.Dispose();
                    pictureBox1.Image = null;
                }

                btnStart.Enabled = true;
                btnStop.Enabled = false;
                this.Text = "RTSP просмотр";
            });
        }

        private void InvokeIfNeeded(Action action)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                if (this.InvokeRequired)
                    this.BeginInvoke(action);
                else
                    action();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();
            base.OnFormClosing(e);
        }
    }
}