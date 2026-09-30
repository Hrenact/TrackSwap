using TrackSwap.Protocol;
using TrackSwap.Localization;
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
        public string Name => string.IsNullOrWhiteSpace(Route.Name) ? Tr.Get("route.unnamed") : Route.Name;
        public string ProxyName => Route.Mode == RouteMode.Unspecified
            ? Tr.Get("route.select_mode")
            : Route.Mode == RouteMode.VirtualController
                ? ProtocolConstants.GetControllerSerial(Route.ControllerHand)
                : Route.Mode == RouteMode.VirtualHmd
                    ? ProtocolConstants.VirtualHmdSerial
                : ProtocolConstants.GetOutputSerial(Route.Mode, Route.VirtualDeviceSlot);
        public string StatusText { get; }
        public Brush StatusBrush { get; }
    }
}
