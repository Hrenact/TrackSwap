using TrackSwap.Protocol;
using System.Windows.Media;

namespace TrackSwap.Models
{
    internal sealed class RouteListItem
    {
        public RouteListItem(RouteConfiguration route, string statusText, Brush statusBrush)
        {
            Route = route;
            StatusText = statusText;
            StatusBrush = statusBrush;
        }

        public RouteConfiguration Route { get; }
        public string Name => string.IsNullOrWhiteSpace(Route.Name) ? "未命名配置" : Route.Name;
        public string ProxyName => ProtocolConstants.GetVirtualSerial(Route.VirtualDeviceSlot);
        public string StatusText { get; }
        public Brush StatusBrush { get; }
    }
}
