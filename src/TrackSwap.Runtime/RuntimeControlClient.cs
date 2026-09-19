using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeControlClient
{
    public static MessageEnvelope Send(MessageEnvelope request, TimeSpan timeout)
    {
        return SendAsync(request, timeout).GetAwaiter().GetResult();
    }

    private static async Task<MessageEnvelope> SendAsync(MessageEnvelope request, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            ProtocolConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("Timed out connecting to TrackSwap Runtime.", exception);
        }
        string requestJson = JsonConvert.SerializeObject(request, RuntimeJson.Settings) + "\n";
        byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);
        if (requestBytes.Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new IOException("Runtime control request is too large.");
        }
        string? responseJson;
        try
        {
            await pipe.WriteAsync(requestBytes, cancellation.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cancellation.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
            responseJson = await reader.ReadLineAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("Timed out waiting for TrackSwap Runtime.", exception);
        }
        if (responseJson == null)
        {
            throw new IOException("Runtime closed the control connection without a response.");
        }
        MessageEnvelope? response = JsonConvert.DeserializeObject<MessageEnvelope>(responseJson, RuntimeJson.Settings);
        if (response == null || response.RequestId != request.RequestId ||
            response.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            throw new IOException("Runtime returned an invalid response.");
        }
        return response;
    }
}
