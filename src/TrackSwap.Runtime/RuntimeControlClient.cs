using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class RuntimeControlClient
{
    public static MessageEnvelope Send(MessageEnvelope request, TimeSpan timeout, string? pipeName = null)
    {
        return SendAsync(request, timeout, pipeName ?? ProtocolConstants.PipeName).GetAwaiter().GetResult();
    }

    private static async Task<MessageEnvelope> SendAsync(MessageEnvelope request, TimeSpan timeout, string pipeName)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException("连接 TrackSwap Runtime 超时。", exception);
        }
        string requestJson = JsonConvert.SerializeObject(request, RuntimeJson.Settings) + "\n";
        byte[] requestBytes = Encoding.UTF8.GetBytes(requestJson);
        if (requestBytes.Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new IOException("Runtime 控制请求超过允许的大小限制。");
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
            throw new TimeoutException("等待 TrackSwap Runtime 响应超时。", exception);
        }
        if (responseJson == null)
        {
            throw new IOException("Runtime 未返回响应便关闭了控制连接。");
        }
        MessageEnvelope? response = JsonConvert.DeserializeObject<MessageEnvelope>(responseJson, RuntimeJson.Settings);
        if (response == null || response.RequestId != request.RequestId ||
            response.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            throw new IOException("Runtime 返回了无效响应。");
        }
        return response;
    }
}
