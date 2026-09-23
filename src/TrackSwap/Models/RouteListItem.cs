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
        public string ProxyName => Route.Mode == RouteMode.Unspecified
            ? "请选择运行模式"
            : Route.Mode == RouteMode.VirtualController
                ? ProtocolConstants.GetControllerSerial(Route.ControllerHand)
                : ProtocolConstants.GetOutputSerial(Route.Mode, Route.VirtualDeviceSlot);
        public string StatusText { get; }
        public Brush StatusBrush { get; }
    }
}
