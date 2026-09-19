using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    internal sealed class RuntimeControlService
    {
        private static readonly SemaphoreSlim RequestGate = new SemaphoreSlim(1, 1);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None
        };

        public Task<RuntimeStatusSnapshot> GetStatusAsync()
        {
            return SendAsync<RuntimeStatusSnapshot>("getStatus", new { });
        }

        public Task<PoseTelemetrySnapshot> GetTelemetryAsync(int virtualDeviceSlot)
        {
            return SendAsync<PoseTelemetrySnapshot>(
                "getTelemetry",
                new TelemetryRequest { VirtualDeviceSlot = virtualDeviceSlot });
        }

        public Task<CalibrationCaptureResponse> CaptureCalibrationAsync(CalibrationCaptureRequest request)
        {
            return SendAsync<CalibrationCaptureResponse>("captureCalibration", request);
        }

        public Task<CalibrationProfilesSnapshot> ListCalibrationProfilesAsync()
        {
            return SendAsync<CalibrationProfilesSnapshot>("listCalibrationProfiles", new { });
        }

        public async Task DeleteCalibrationProfileAsync(string profileId)
        {
            await SendAsync<object>(
                "deleteCalibrationProfile",
                new CalibrationProfileRequest { ProfileId = profileId });
        }

        public async Task<long> ApplyConfigurationAsync(RuntimeConfiguration configuration)
        {
            MessageEnvelope response = await SendAsync(
                "applyConfiguration",
                JsonConvert.SerializeObject(configuration, JsonSettings));
            if (!string.Equals(response.MessageType, "configurationApplied", StringComparison.Ordinal))
            {
                throw CreateUnexpectedResponseException(response);
            }

            var payload = JsonConvert.DeserializeObject<AppliedRevision>(response.PayloadJson, JsonSettings);
            if (payload == null)
            {
                throw new InvalidDataException("Runtime returned an empty apply response.");
            }

            return payload.Revision;
        }

        public bool TryStartRuntime(out string error)
        {
            error = null;
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(applicationDirectory, "runtime", "TrackSwap.Runtime.exe"),
                Path.Combine(applicationDirectory, "TrackSwap.Runtime.exe"),
                Path.GetFullPath(Path.Combine(
                    applicationDirectory,
                    "..", "..", "..", "..", "TrackSwap.Runtime", "bin", "Release", "net8.0-windows", "TrackSwap.Runtime.exe"))
            };
            string executable = null;
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    executable = candidate;
                    break;
                }
            }
            if (executable == null)
            {
                error = "未在程序目录中找到 TrackSwap.Runtime.exe。请使用完整的 v003 程序包。";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--run",
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is System.ComponentModel.Win32Exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static async Task<T> SendAsync<T>(string messageType, object payload)
        {
            MessageEnvelope response = await SendAsync(
                messageType,
                JsonConvert.SerializeObject(payload, JsonSettings));
            if (string.Equals(response.MessageType, "error", StringComparison.Ordinal))
            {
                throw CreateUnexpectedResponseException(response);
            }

            T result = JsonConvert.DeserializeObject<T>(response.PayloadJson, JsonSettings);
            if (result == null)
            {
                throw new InvalidDataException("Runtime returned an empty response.");
            }

            return result;
        }

        private static async Task<MessageEnvelope> SendAsync(string messageType, string payloadJson)
        {
            await RequestGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string requestId = Guid.NewGuid().ToString("N");
                var request = new MessageEnvelope
                {
                    MessageType = messageType,
                    RequestId = requestId,
                    PayloadJson = payloadJson
                };

                using (var pipe = new NamedPipeClientStream(
                    ".",
                    ProtocolConstants.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous))
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    try
                    {
                        await pipe.ConnectAsync(1000, cancellation.Token).ConfigureAwait(false);
                        byte[] requestBytes = Encoding.UTF8.GetBytes(
                            JsonConvert.SerializeObject(request, JsonSettings) + "\n");
                        await pipe.WriteAsync(requestBytes, 0, requestBytes.Length, cancellation.Token).ConfigureAwait(false);
                        await pipe.FlushAsync(cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new TimeoutException("Runtime control request timed out.", exception);
                    }

                    string responseLine;
                    try
                    {
                        responseLine = await ReadBoundedLineAsync(pipe, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new TimeoutException("Runtime control response timed out.", exception);
                    }
                    MessageEnvelope response = JsonConvert.DeserializeObject<MessageEnvelope>(responseLine, JsonSettings);
                    if (response == null || response.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion ||
                        !string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Runtime returned an invalid control envelope.");
                    }

                    return response;
                }
            }
            finally
            {
                RequestGate.Release();
            }
        }

        private static async Task<string> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            while (bytes.Count < ProtocolConstants.MaximumMessageBytes)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Runtime closed the control connection before replying.");
                }
                if (buffer[0] == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }
                bytes.Add(buffer[0]);
            }

            throw new InvalidDataException("Runtime control response exceeds the configured limit.");
        }

        private static Exception CreateUnexpectedResponseException(MessageEnvelope response)
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<ErrorPayload>(response.PayloadJson, JsonSettings);
                if (!string.IsNullOrWhiteSpace(payload?.Error))
                {
                    return new InvalidDataException(payload.Error);
                }
            }
            catch (JsonException)
            {
            }

            return new InvalidDataException("Unexpected Runtime response: " + response.MessageType);
        }

        private sealed class AppliedRevision
        {
            public long Revision { get; set; }
        }

        private sealed class ErrorPayload
        {
            public string Error { get; set; }
        }
    }
}
