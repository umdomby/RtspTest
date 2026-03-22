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
            if (net == null) return false;

            try
            {
                // 1. Подготовка изображения (Preprocessing)
                // Модель обучена на 224x224. Делаем letterbox, чтобы сохранить пропорции.
                using var letterboxedFrame = Letterbox(frame, new Size(224, 224));

                // Создаем blob с правильным размером 224x224 и нормализацией
                using var blob = CvDnn.BlobFromImage(letterboxedFrame, 1.0 / 255.0, new Size(224, 224), new Scalar(0, 0, 0), true, false);

                lock (net)
                {
                    // 2. Инференс (Inference)
                    net.SetInput(blob);
                    using var output = net.Forward();

                    // 3. Разбор выходных данных (Postprocessing)
                    // Для классификатора выход — это плоский массив вероятностей классов.
                    output.GetArray(out float[] probabilities);

                    // probabilities[0] — это 'anomaly', т.к. папка идет первой по алфавиту
                    // probabilities[1] — это 'normal'

                    float anomalyScore = probabilities[0];
                    float normalScore = probabilities[1];

                    // Обновляем уверенность для отображения (берем вероятность аномалии)
                    currentConfidence = anomalyScore * 100f;

                    // Если вероятность аномалии выше 0.5 (или 50%), бьем тревогу
                    // Вы можете настроить этот порог (например, 0.7 или 0.8) для уменьшения ложных срабатываний.
                    return anomalyScore > 0.5f;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка классификации: " + ex.Message);
                return false;
            }
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

            this.InvokeIfNeeded(() =>
            {
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

        private Mat Letterbox(Mat source, Size targetSize)
        {
            float scale = Math.Min((float)targetSize.Width / source.Width, (float)targetSize.Height / source.Height);
            int newWidth = (int)(source.Width * scale);
            int newHeight = (int)(source.Height * scale);

            using var resized = new Mat();
            Cv2.Resize(source, resized, new Size(newWidth, newHeight));

            var padded = new Mat(targetSize, source.Type(), Scalar.All(0)); // Черные полосы
            int x = (targetSize.Width - newWidth) / 2;
            int y = (targetSize.Height - newHeight) / 2;

            resized.CopyTo(new Mat(padded, new Rect(x, y, newWidth, newHeight)));
            return padded;
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

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}