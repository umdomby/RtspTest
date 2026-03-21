using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Windows.Forms;

namespace RtspTest
{
    public partial class Form1 : Form
    {
        private VideoCapture? capture;
        private bool running = false;

        public Form1()
        {
            InitializeComponent();
            this.DoubleBuffered = true;           // smoother repaint
            // pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;  // or StretchImage
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (running) return;

            string rtspUrl = "rtsp://username:password@192.168.1.100:554/stream1";  // ← your url

            capture = new VideoCapture(rtspUrl);
            if (!capture.IsOpened())
            {
                MessageBox.Show("Cannot open RTSP stream");
                return;
            }

            running = true;
            btnStart.Enabled = false;

            using var mat = new Mat();
            while (running && !IsDisposed)
            {
                if (!capture.Read(mat) || mat.Empty())
                    break;

                // Optional: resize if too big
                // Cv2.Resize(mat, mat, new Size(pictureBox1.Width, pictureBox1.Height));

                pictureBox1.Image?.Dispose();           // important to avoid memory leak
                pictureBox1.Image = mat.ToBitmap();

                await Task.Delay(10);                   // ~100 fps cap, adjust as needed
            }

            capture.Release();
            running = false;
            btnStart.Enabled = true;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            running = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            running = false;
            capture?.Release();
            base.OnFormClosing(e);
        }
    }
}