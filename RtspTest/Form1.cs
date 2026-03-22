using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Size = OpenCvSharp.Size;

namespace RtspTest
{
    public partial class Form1 : Form
    {
        private VideoCapture? capture;
        private Net? net;
        private bool isRunning = false;
        private CancellationTokenSource? cts;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        private float currentConfidence = 0f;
        private readonly string modelPath = @"C:\Users\umdom\source\repos\RtspTest\RtspTest\best.onnx";

        public Form1()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            try
            {
                // Загружаем модель
                net = CvDnn.ReadNetFromOnnx(modelPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка загрузки ONNX: " + ex.Message);
            }
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (isRunning) return;

            string rtspUrl = "rtsp://127.0.0.1:8554/mystream";
            isRunning = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            cts = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                try
                {
                    await semaphore.WaitAsync();
                    try { capture = new VideoCapture(rtspUrl); }
                    finally { semaphore.Release(); }

                    if (capture == null || !capture.IsOpened()) return;

                    while (isRunning && !cts.Token.IsCancellationRequested)
                    {
                        using var frame = new Mat();

                        await semaphore.WaitAsync();
                        bool readSuccess = false;
                        try
                        {
                            if (capture != null && !capture.IsDisposed && capture.IsOpened())
                                readSuccess = capture.Read(frame);
                        }
                        finally { semaphore.Release(); }

                        if (!readSuccess || frame.Empty())
                        {
                            await Task.Delay(10);
                            continue;
                        }

                        // Анализ
                        bool hasAnomaly = DetectAnomaly(frame);

                        // Рисуем статус
                        DrawStatus(frame, hasAnomaly);

                        // Безопасная передача в UI
                        Bitmap bmp = frame.ToBitmap();
                        UpdateUI(bmp);

                        await Task.Delay(5);
                    }
                }
                catch { }
                finally { this.InvokeIfNeeded(() => StopCapture()); }
            }, cts.Token);
        }

        private bool DetectAnomaly(Mat frame)
        {
            // Проверка net на null исправляет CS8602
            if (net == null) return false;

            try
            {
                using var blob = CvDnn.BlobFromImage(frame, 1.0 / 255.0, new Size(640, 640), new Scalar(0, 0, 0), true, false);

                lock (net)
                {
                    net.SetInput(blob);
                    using var output = net.Forward();

                    output.GetArray(out float[] data);

                    int rows = output.Size(1);
                    int cols = output.Size(2);
                    float maxConf = 0;

                    // Пытаемся найти уверенность в данных
                    if (rows >= 5)
                    {
                        for (int i = 0; i < cols; i++)
                        {
                            float conf = data[(4 * cols) + i];
                            if (conf > maxConf && conf <= 1.0f) maxConf = conf;
                        }
                    }

                    // Если в 4-й строке ничего нет, ищем максимум по всему массиву (для некоторых моделей)
                    if (maxConf < 0.001f)
                    {
                        foreach (var v in data) if (v > maxConf && v <= 1.0f) maxConf = v;
                    }

                    currentConfidence = maxConf * 100f;
                    return maxConf > 0.45f;
                }
            }
            catch { return false; }
        }

        private void DrawStatus(Mat frame, bool isAnomaly)
        {
            Scalar color = isAnomaly ? new Scalar(0, 0, 255) : new Scalar(0, 255, 0);
            Cv2.Rectangle(frame, new OpenCvSharp.Point(10, 10), new OpenCvSharp.Point(550, 150), new Scalar(0, 0, 0), -1);
            Cv2.Circle(frame, new OpenCvSharp.Point(60, 80), 35, color, -1);
            Cv2.PutText(frame, isAnomaly ? "ANOMALY!" : "STABLE", new OpenCvSharp.Point(110, 75),
                HersheyFonts.HersheyComplex, 1.9, color, 3, LineTypes.AntiAlias);
            Cv2.PutText(frame, $"CONF: {currentConfidence:F1}%", new OpenCvSharp.Point(110, 125),
                HersheyFonts.HersheySimplex, 1.2, new Scalar(255, 255, 255), 2, LineTypes.AntiAlias);
        }

        private void UpdateUI(Bitmap bmp)
        {
            this.InvokeIfNeeded(() =>
            {
                this.Text = $"DETECTOR [{currentConfidence:F1}%]";
                var old = pictureBox1.Image;
                pictureBox1.Image = bmp;
                old?.Dispose();
            });
        }

        private void StopCapture()
        {
            isRunning = false;
            cts?.Cancel();

            semaphore.Wait(500);
            try
            {
                if (capture != null)
                {
                    capture.Release();
                    capture.Dispose();
                    capture = null;
                }
            }
            finally { semaphore.Release(); }

            this.InvokeIfNeeded(() => {
                pictureBox1.Image?.Dispose();
                pictureBox1.Image = null;
                btnStart.Enabled = true;
                btnStop.Enabled = false;
            });
        }

        private void btnStop_Click(object sender, EventArgs e) => StopCapture();

        private void InvokeIfNeeded(Action action)
        {
            if (!this.IsDisposed && this.IsHandleCreated)
            {
                if (this.InvokeRequired) this.BeginInvoke(action);
                else action();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopCapture();
            Thread.Sleep(100);
            if (net != null)
            {
                lock (net) { net.Dispose(); net = null; }
            }
            base.OnFormClosing(e);
        }
    }
}