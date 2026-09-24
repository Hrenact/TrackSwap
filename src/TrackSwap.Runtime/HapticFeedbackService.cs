using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class HapticFeedbackService
{
    private const int PollIntervalMilliseconds = 16;
    private readonly XInputInputService xInput;
    private readonly OscInputService oscInput;

    public HapticFeedbackService(XInputInputService xInput, OscInputService oscInput)
    {
        this.xInput = xInput;
        this.oscInput = oscInput;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<HapticFeedbackEvent> events;
            try
            {
                events = DriverControlClient.GetHapticEvents(TimeSpan.FromMilliseconds(100));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException)
            {
                events = Array.Empty<HapticFeedbackEvent>();
            }

            if (events.Count != 0)
            {
                xInput.ApplyHapticEvents(events);
                await oscInput.SendHapticEventsAsync(events, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }
}
