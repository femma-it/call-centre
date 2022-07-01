using System;
using System.Threading;
using System.Windows.Forms;

namespace CallCentre
{
    static class Program
    {
        private static bool _alreadyRunning;

        private static readonly Mutex Mutex = new Mutex(true, "CTI-8F6F0AC4-B9A1-45fd-A8CF-72F04E6BDE8F", out _alreadyRunning);

        [STAThread]
        static void Main()
        {
            if (!Mutex.WaitOne(TimeSpan.Zero, true)) 
                return;
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Main());
            Mutex.ReleaseMutex();
        }
    }
}