using System;
using System.Windows.Forms;

namespace RtspTest
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Отключаем проблемную инициализацию DPI, чтобы программа просто запустилась
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Запуск основной формы
            Application.Run(new Form1());
        }
    }
}