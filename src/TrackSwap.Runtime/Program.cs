using Newtonsoft.Json;
using System.Globalization;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            RuntimePreferenceReader.RuntimePreferences preferences = RuntimePreferenceReader.Read();
            bool neededForSteamVrSession =
                preferences.RuntimeLifecycleMode == RuntimeLifecycleMode.FollowSteamVr ||
                preferences.FollowSteamVrWithTrackSwap;
            TrackSwapUiLauncher.WriteDiagnostic(
                "Runtime no-argument launch; lifecycle=" + preferences.RuntimeLifecycleMode +
                ", followUi=" + preferences.FollowSteamVrWithTrackSwap +
                ", needed=" + neededForSteamVrSession +
                ", localAppData=" + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            return neededForSteamVrSession
                ? RunHost(
                    GetDefaultConfigurationPath(),
                    RuntimeLifecycleMode.FollowSteamVr,
                    null,
                    preferences.FollowSteamVrWithTrackSwap)
                : 0;
        }

        if (string.Equals(args[0], "--run", StringComparison.Ordinal))
        {
            return ParseAndRunHost(args);
        }

        if (args.Length == 1 && string.Equals(args[0], "--runtime-status", StringComparison.Ordinal))
        {
            return SendRuntimeRequest("getStatus", "{}");
        }

        if (args.Length == 1 && string.Equals(args[0], "--driver-telemetry", StringComparison.Ordinal))
        {
            return PrintDriverTelemetry();
        }

        if (args.Length == 1 && string.Equals(args[0], "--runtime-telemetry", StringComparison.Ordinal))
        {
            return SendRuntimeRequest(
                "getTelemetry",
                JsonConvert.SerializeObject(new TelemetryRequest { VirtualDeviceSlot = 0 }, RuntimeJson.Settings));
        }

        if (args.Length == 2 && string.Equals(args[0], "--apply-single-route", StringComparison.Ordinal))
        {
            return ApplySingleRoute(args[1]);
        }

        if (args.Length == 1 && string.Equals(args[0], "--print-contract", StringComparison.Ordinal))
        {
            Console.WriteLine($"Protocol version: {ProtocolConstants.CurrentProtocolVersion}");
            Console.WriteLine($"Configuration schema: {ProtocolConstants.CurrentConfigurationSchemaVersion}");
            Console.WriteLine($"Named pipe: {ProtocolConstants.PipeName}");
            Console.WriteLine($"Maximum message bytes: {ProtocolConstants.MaximumMessageBytes}");
            return 0;
        }

        if (args.Length == 2 && string.Equals(args[0], "--validate-config", StringComparison.Ordinal))
        {
            return ValidateConfiguration(args[1]);
        }

        if (args.Length == 2 && string.Equals(args[0], "--switch-source", StringComparison.Ordinal))
        {
            return SwitchSource(args[1]);
        }

        if (args.Length == 1 && string.Equals(args[0], "--reset-offset", StringComparison.Ordinal))
        {
            return SetOffset(PoseOffset.Identity());
        }

        if (args.Length == 8 && string.Equals(args[0], "--set-offset", StringComparison.Ordinal))
        {
            return ParseAndSetOffset(args);
        }

        Console.WriteLine("TrackSwap Runtime v005");
        Console.WriteLine("Use --run [config-path] [--lifecycle FollowTrackSwap|FollowSteamVr] [--owner-pid PID]");
        Console.WriteLine("to start the runtime host and IPC service. A no-argument launch is reserved for SteamVR.");
        Console.WriteLine("Use --print-contract, --validate-config <path>, --switch-source <exact-device-path>,");
        Console.WriteLine("--set-offset <tx> <ty> <tz> <qx> <qy> <qz> <qw>, --reset-offset,");
        Console.WriteLine("--runtime-status, --runtime-telemetry, --driver-telemetry, or --run [config-path].");
        return 0;
    }

    private static int ParseAndRunHost(string[] args)
    {
        string configurationPath = GetDefaultConfigurationPath();
        RuntimeLifecycleMode? lifecycleMode = null;
        int? ownerProcessId = null;
        bool configurationPathSet = false;
        for (int index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--lifecycle", StringComparison.Ordinal))
            {
                if (++index >= args.Length || !Enum.TryParse(args[index], true, out RuntimeLifecycleMode parsedMode))
                {
                    Console.Error.WriteLine("--lifecycle must be FollowTrackSwap or FollowSteamVr.");
                    return 2;
                }
                lifecycleMode = parsedMode;
            }
            else if (string.Equals(args[index], "--owner-pid", StringComparison.Ordinal))
            {
                if (++index >= args.Length || !int.TryParse(args[index], out int parsedProcessId) || parsedProcessId <= 0)
                {
                    Console.Error.WriteLine("--owner-pid must be a positive process id.");
                    return 2;
                }
                ownerProcessId = parsedProcessId;
            }
            else if (!configurationPathSet)
            {
                configurationPath = args[index];
                configurationPathSet = true;
            }
            else
            {
                Console.Error.WriteLine("Unexpected Runtime argument: " + args[index]);
                return 2;
            }
        }

        return RunHost(configurationPath, lifecycleMode, ownerProcessId, launchTrackSwapUi: false);
    }

    private static int RunHost(
        string configurationPath,
        RuntimeLifecycleMode? lifecycleMode,
        int? ownerProcessId,
        bool launchTrackSwapUi)
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\TrackSwap.Runtime.v1",
            createdNew: out bool createdNew);
        if (!createdNew)
        {
            return 0;
        }

        RuntimeHost.RunAsync(
            configurationPath,
            lifecycleMode,
            ownerProcessId,
            launchTrackSwapUi).GetAwaiter().GetResult();
        return 0;
    }

    private static string GetDefaultConfigurationPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrackSwap",
            "runtime-config.json");
    }

    private static int PrintDriverTelemetry()
    {
        try
        {
            IReadOnlyList<PoseTelemetrySnapshot> snapshots =
                DriverControlClient.GetTelemetry(TimeSpan.FromSeconds(2));
            Console.WriteLine(JsonConvert.SerializeObject(snapshots, RuntimeJson.Settings));
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ApplySingleRoute(string sourceDevicePath)
    {
        var configuration = new RuntimeConfiguration
        {
            Revision = DateTime.UtcNow.Ticks,
            Routes =
            {
                new RouteConfiguration
                {
                    RouteId = "primary",
                    VirtualDeviceSlot = 0,
                    SourceDevicePath = sourceDevicePath,
                    TargetDevicePath = ProtocolConstants.RightHandRolePath,
                    Offset = PoseOffset.Identity()
                }
            }
        };
        string payload = JsonConvert.SerializeObject(configuration, RuntimeJson.Settings);
        return SendRuntimeRequest("applyConfiguration", payload);
    }

    private static int SendRuntimeRequest(string messageType, string payloadJson)
    {
        try
        {
            MessageEnvelope response = RuntimeControlClient.Send(new MessageEnvelope
            {
                MessageType = messageType,
                RequestId = Guid.NewGuid().ToString("N"),
                PayloadJson = payloadJson
            }, TimeSpan.FromSeconds(2));
            Console.WriteLine(response.PayloadJson);
            return 0;
        }
        catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int SwitchSource(string sourceDevicePath)
    {
        if (!sourceDevicePath.StartsWith("/devices/", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The source must be an exact /devices/ path returned by OpenVR.");
            return 2;
        }

        try
        {
            string response = DriverControlClient.SwitchSource(sourceDevicePath, TimeSpan.FromSeconds(2));
            Console.WriteLine(response);
            return 0;
        }
        catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ParseAndSetOffset(string[] args)
    {
        var values = new double[7];
        for (int index = 0; index < values.Length; ++index)
        {
            if (!double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
            {
                Console.Error.WriteLine($"Offset value {index + 1} is not a valid invariant-culture number.");
                return 2;
            }
        }

        return SetOffset(new PoseOffset
        {
            TranslationX = values[0],
            TranslationY = values[1],
            TranslationZ = values[2],
            RotationX = values[3],
            RotationY = values[4],
            RotationZ = values[5],
            RotationW = values[6]
        });
    }

    private static int SetOffset(PoseOffset offset)
    {
        var configuration = new RuntimeConfiguration
        {
            Routes =
            {
                new RouteConfiguration
                {
                    RouteId = "offset-validation",
                    SourceDevicePath = "/devices/validation/source",
                    TargetDevicePath = ProtocolConstants.HeadRolePath,
                    Offset = offset
                }
            }
        };
        IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
        if (errors.Count != 0)
        {
            foreach (string error in errors)
            {
                Console.Error.WriteLine(error);
            }
            return 2;
        }

        try
        {
            string response = DriverControlClient.SetOffset(offset, TimeSpan.FromSeconds(2));
            Console.WriteLine(response);
            return 0;
        }
        catch (Exception exception) when (exception is IOException || exception is TimeoutException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ValidateConfiguration(string path)
    {
        try
        {
            string json = File.ReadAllText(Path.GetFullPath(path));
            RuntimeConfiguration? configuration = JsonConvert.DeserializeObject<RuntimeConfiguration>(json);
            IReadOnlyList<string> errors = ConfigurationValidator.Validate(configuration);
            if (errors.Count == 0)
            {
                Console.WriteLine("Configuration is valid.");
                return 0;
            }

            foreach (string error in errors)
            {
                Console.Error.WriteLine(error);
            }

            return 2;
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException || exception is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
