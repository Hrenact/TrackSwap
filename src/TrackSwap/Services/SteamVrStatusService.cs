using System.Diagnostics;

namespace TrackSwap.Services
{
    public sealed class SteamVrStatusService
    {
        private static readonly string[] ProcessNames =
        {
            "vrserver",
            "vrmonitor",
            "vrcompositor"
        };

        public bool IsRunning()
        {
            foreach (string name in ProcessNames)
            {
                Process[] processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }
    }
}
