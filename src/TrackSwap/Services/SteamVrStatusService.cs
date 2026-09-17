using System.Diagnostics;
using System.Linq;

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
            return ProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);
        }
    }
}
