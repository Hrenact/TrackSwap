using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class OpenVrCalibrationService
{
    internal const string SystemInterfaceVersion = "FnTable:IVRSystem_026";
    internal const int GetDeviceToAbsoluteTrackingPoseIndex = 12;
    internal const int IsTrackedDeviceConnectedIndex = 21;
    internal const int GetStringTrackedDevicePropertyIndex = 28;
    private const int MaximumTrackedDeviceCount = 64;
    private const double MaximumTranslationRmsMetres = 0.015;
    private const double MaximumRotationRmsDegrees = 2.0;

    internal static int TrackedDevicePoseBytes => Marshal.SizeOf<TrackedDevicePose>();

    public CalibrationProfile Capture(
        CalibrationCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        string runtimePath = FindOpenVrRuntimePath();
        string libraryPath = Path.Combine(runtimePath, "bin", "win64", "openvr_api.dll");
        if (!File.Exists(libraryPath))
        {
            throw new InvalidDataException("OpenVR runtime library was not found.");
        }

        IntPtr module = NativeLibrary.Load(libraryPath);
        IntPtr poseBuffer = IntPtr.Zero;
        bool initialized = false;
        try
        {
            VRInitInternal init = GetExport<VRInitInternal>(module, "VR_InitInternal2");
            VRGetGenericInterface getInterface = GetExport<VRGetGenericInterface>(module, "VR_GetGenericInterface");
            VRShutdownInternal shutdown = GetExport<VRShutdownInternal>(module, "VR_ShutdownInternal");
            EVRInitError initError = EVRInitError.None;
            init(ref initError, EVRApplicationType.Background, null);
            if (initError != EVRInitError.None)
            {
                throw new InvalidDataException($"OpenVR initialization failed with code {(int)initError}.");
            }
            initialized = true;
            Console.WriteLine("Calibration OpenVR background client initialized.");

            IntPtr systemTable = getInterface(SystemInterfaceVersion, ref initError);
            if (systemTable == IntPtr.Zero || initError != EVRInitError.None)
            {
                throw new InvalidDataException($"OpenVR system interface failed with code {(int)initError}.");
            }
            Console.WriteLine($"Calibration acquired {SystemInterfaceVersion}.");

            GetDeviceToAbsoluteTrackingPose getPoses =
                GetTableFunction<GetDeviceToAbsoluteTrackingPose>(
                    systemTable, GetDeviceToAbsoluteTrackingPoseIndex);
            GetStringTrackedDeviceProperty getStringProperty =
                GetTableFunction<GetStringTrackedDeviceProperty>(
                    systemTable, GetStringTrackedDevicePropertyIndex);
            IsTrackedDeviceConnected isTrackedDeviceConnected =
                GetTableFunction<IsTrackedDeviceConnected>(
                    systemTable, IsTrackedDeviceConnectedIndex);
            Console.WriteLine("Calibration resolved OpenVR function-table entries.");
            uint sourceIndex = FindDeviceIndex(getStringProperty, request.SourceDevicePath);
            Console.WriteLine($"Calibration resolved source index {sourceIndex}.");
            uint targetIndex = FindDeviceIndex(getStringProperty, request.TargetDevicePath);
            Console.WriteLine($"Calibration resolved target index {targetIndex}.");
            if (sourceIndex == targetIndex)
            {
                throw new InvalidDataException("Calibration source and target must be different physical devices.");
            }

            var relativeSamples = new List<CalibrationPose>(request.RequestedSampleCount);
            int poseBytes = Marshal.SizeOf<TrackedDevicePose>();
            int bufferBytes = checked(poseBytes * MaximumTrackedDeviceCount);
            poseBuffer = Marshal.AllocHGlobal(bufferBytes);
            Marshal.Copy(new byte[bufferBytes], 0, poseBuffer, bufferBytes);
            var sourceDiagnostics = new PoseCaptureDiagnostics(request.SourceDevicePath, sourceIndex);
            var targetDiagnostics = new PoseCaptureDiagnostics(request.TargetDevicePath, targetIndex);
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            bool firstPoseCall = true;
            while (relativeSamples.Count < request.RequestedSampleCount && DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (firstPoseCall)
                    {
                        Console.WriteLine("Calibration pose capture started.");
                    }
                    getPoses(ETrackingUniverseOrigin.Standing, 0, poseBuffer, MaximumTrackedDeviceCount);
                    if (firstPoseCall)
                    {
                        Console.WriteLine("Calibration received its first OpenVR pose frame.");
                        firstPoseCall = false;
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Calibration pose call failed: {exception}");
                    throw new InvalidDataException(
                        $"OpenVR pose capture failed ({exception.GetType().Name}: {exception.Message}).",
                        exception);
                }
                TrackedDevicePose source = ReadPose(poseBuffer, poseBytes, sourceIndex);
                TrackedDevicePose target = ReadPose(poseBuffer, poseBytes, targetIndex);
                sourceDiagnostics.Observe(source, isTrackedDeviceConnected(sourceIndex));
                targetDiagnostics.Observe(target, isTrackedDeviceConnected(targetIndex));
                if (source.DeviceIsConnected && source.PoseIsValid &&
                    target.DeviceIsConnected && target.PoseIsValid)
                {
                    relativeSamples.Add(CalibrationMath.Relative(ToPose(source), ToPose(target)));
                }
                if (cancellationToken.WaitHandle.WaitOne(16))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (relativeSamples.Count < request.RequestedSampleCount)
            {
                string sourceSummary = sourceDiagnostics.Format("Source");
                string targetSummary = targetDiagnostics.Format("Target");
                throw new InvalidDataException(
                    $"Only {relativeSamples.Count} valid simultaneous samples were captured; " +
                    $"{request.RequestedSampleCount} are required. " +
                    sourceSummary + " " + targetSummary + " " +
                    "Keep both devices awake and visible.");
            }

            CalibrationComputation result = CalibrationMath.Average(relativeSamples);
            if (result.TranslationRmsMetres > MaximumTranslationRmsMetres ||
                result.RotationRmsDegrees > MaximumRotationRmsDegrees)
            {
                throw new InvalidDataException(
                    $"Calibration was unstable (translation RMS {result.TranslationRmsMetres:F4} m, " +
                    $"rotation RMS {result.RotationRmsDegrees:F2}°). Hold the devices rigidly together and retry.");
            }

            return new CalibrationProfile
            {
                ProfileId = Guid.NewGuid().ToString("N"),
                Name = request.ProfileName.Trim(),
                SourceDevicePath = request.SourceDevicePath,
                TargetDevicePath = request.TargetDevicePath,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                SampleCount = relativeSamples.Count,
                TranslationRmsMetres = result.TranslationRmsMetres,
                RotationRmsDegrees = result.RotationRmsDegrees,
                Offset = result.Offset
            };
        }
        finally
        {
            if (poseBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(poseBuffer);
            }
            if (initialized)
            {
                try
                {
                    GetExport<VRShutdownInternal>(module, "VR_ShutdownInternal")();
                }
                catch
                {
                }
            }
            NativeLibrary.Free(module);
        }
    }

    private static void ValidateRequest(CalibrationCaptureRequest request)
    {
        if (!request.OriginalTargetPoseVisible)
        {
            throw new InvalidDataException(
                "Calibration requires the target's original pose to remain visible. Disable the virtual-device override before capture.");
        }
        if (string.IsNullOrWhiteSpace(request.ProfileName) || request.ProfileName.Trim().Length > 80)
        {
            throw new InvalidDataException("Calibration profile name must contain 1-80 characters.");
        }
        if (!IsExactDevicePath(request.SourceDevicePath) || !IsExactDevicePath(request.TargetDevicePath))
        {
            throw new InvalidDataException("Calibration source and target must be exact /devices/ paths returned by OpenVR.");
        }
        if (request.RequestedSampleCount < ProtocolConstants.MinimumCalibrationSamples ||
            request.RequestedSampleCount > ProtocolConstants.MaximumCalibrationSamples)
        {
            throw new InvalidDataException(
                $"Calibration sample count must be {ProtocolConstants.MinimumCalibrationSamples}-" +
                $"{ProtocolConstants.MaximumCalibrationSamples}.");
        }
    }

    private static bool IsExactDevicePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && path.StartsWith("/devices/", StringComparison.Ordinal);
    }

    private static uint FindDeviceIndex(GetStringTrackedDeviceProperty getter, string expectedPath)
    {
        for (uint index = 0; index < MaximumTrackedDeviceCount; index++)
        {
            string registeredType = ReadStringProperty(getter, index, ETrackedDeviceProperty.RegisteredDeviceType);
            if (string.IsNullOrWhiteSpace(registeredType))
            {
                continue;
            }
            string path = registeredType.StartsWith("/devices/", StringComparison.Ordinal)
                ? registeredType
                : "/devices/" + registeredType;
            if (string.Equals(path, expectedPath, StringComparison.Ordinal))
            {
                return index;
            }
        }
        throw new InvalidDataException($"OpenVR device '{expectedPath}' is not currently available.");
    }

    private static string ReadStringProperty(
        GetStringTrackedDeviceProperty getter,
        uint index,
        ETrackedDeviceProperty property)
    {
        ETrackedPropertyError error = ETrackedPropertyError.Success;
        uint requiredLength = getter(index, property, null, 0, ref error);
        if (requiredLength == 0)
        {
            return string.Empty;
        }
        var value = new StringBuilder((int)requiredLength);
        error = ETrackedPropertyError.Success;
        getter(index, property, value, requiredLength, ref error);
        return error == ETrackedPropertyError.Success ? value.ToString() : string.Empty;
    }

    private static CalibrationPose ToPose(TrackedDevicePose pose)
    {
        HmdMatrix34 matrix = pose.DeviceToAbsoluteTracking;
        (double qx, double qy, double qz, double qw) = QuaternionFromMatrix(matrix);
        return new CalibrationPose(matrix.M03, matrix.M13, matrix.M23, qx, qy, qz, qw);
    }

    private static TrackedDevicePose ReadPose(IntPtr buffer, int poseBytes, uint deviceIndex)
    {
        IntPtr address = IntPtr.Add(buffer, checked((int)deviceIndex * poseBytes));
        return Marshal.PtrToStructure<TrackedDevicePose>(address);
    }

    private static (double X, double Y, double Z, double W) QuaternionFromMatrix(HmdMatrix34 matrix)
    {
        double x;
        double y;
        double z;
        double w;
        double trace = matrix.M00 + matrix.M11 + matrix.M22;
        if (trace > 0)
        {
            double scale = Math.Sqrt(trace + 1) * 2;
            w = 0.25 * scale;
            x = (matrix.M21 - matrix.M12) / scale;
            y = (matrix.M02 - matrix.M20) / scale;
            z = (matrix.M10 - matrix.M01) / scale;
        }
        else if (matrix.M00 > matrix.M11 && matrix.M00 > matrix.M22)
        {
            double scale = Math.Sqrt(1 + matrix.M00 - matrix.M11 - matrix.M22) * 2;
            w = (matrix.M21 - matrix.M12) / scale;
            x = 0.25 * scale;
            y = (matrix.M01 + matrix.M10) / scale;
            z = (matrix.M02 + matrix.M20) / scale;
        }
        else if (matrix.M11 > matrix.M22)
        {
            double scale = Math.Sqrt(1 + matrix.M11 - matrix.M00 - matrix.M22) * 2;
            w = (matrix.M02 - matrix.M20) / scale;
            x = (matrix.M01 + matrix.M10) / scale;
            y = 0.25 * scale;
            z = (matrix.M12 + matrix.M21) / scale;
        }
        else
        {
            double scale = Math.Sqrt(1 + matrix.M22 - matrix.M00 - matrix.M11) * 2;
            w = (matrix.M10 - matrix.M01) / scale;
            x = (matrix.M02 + matrix.M20) / scale;
            y = (matrix.M12 + matrix.M21) / scale;
            z = 0.25 * scale;
        }
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        return (x / length, y / length, z / length, w / length);
    }

    private sealed class PoseCaptureDiagnostics
    {
        private readonly string devicePath;
        private readonly uint deviceIndex;
        private int observedFrames;
        private int systemConnectedFrames;
        private int connectedFrames;
        private int validFrames;
        private int lastTrackingResult;

        public PoseCaptureDiagnostics(string devicePath, uint deviceIndex)
        {
            this.devicePath = devicePath;
            this.deviceIndex = deviceIndex;
        }

        public void Observe(TrackedDevicePose pose, bool systemConnected)
        {
            observedFrames++;
            if (systemConnected)
            {
                systemConnectedFrames++;
            }
            if (pose.DeviceIsConnected)
            {
                connectedFrames++;
            }
            if (pose.PoseIsValid)
            {
                validFrames++;
            }
            lastTrackingResult = pose.TrackingResult;
        }

        public string Format(string endpoint)
        {
            return $"{endpoint} '{devicePath}' (index {deviceIndex}): " +
                $"system connected {systemConnectedFrames}/{observedFrames}, " +
                $"pose connected {connectedFrames}/{observedFrames}, valid {validFrames}/{observedFrames}, " +
                $"last tracking result {lastTrackingResult}.";
        }
    }

    private static string FindOpenVrRuntimePath()
    {
        string registryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr",
            "openvrpaths.vrpath");
        if (!File.Exists(registryPath))
        {
            throw new InvalidDataException("OpenVR path registry was not found.");
        }
        JObject root = JObject.Parse(File.ReadAllText(registryPath));
        string? path = (root["runtime"] as JArray)?.Values<string>()
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("SteamVR runtime path is not registered.");
        }
        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static T GetExport<T>(IntPtr module, string name) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    }

    private static T GetTableFunction<T>(IntPtr table, int index) where T : Delegate
    {
        IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
        if (address == IntPtr.Zero)
        {
            throw new InvalidDataException("OpenVR system function table is incomplete.");
        }
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate uint VRInitInternal(ref EVRInitError error, EVRApplicationType applicationType, string? startupInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr VRGetGenericInterface(string interfaceVersion, ref EVRInitError error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VRShutdownInternal();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDeviceToAbsoluteTrackingPose(
        ETrackingUniverseOrigin origin,
        float predictedSeconds,
        IntPtr poses,
        uint poseCount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate uint GetStringTrackedDeviceProperty(
        uint deviceIndex,
        ETrackedDeviceProperty property,
        StringBuilder? value,
        uint valueCapacity,
        ref ETrackedPropertyError error);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsTrackedDeviceConnected(uint deviceIndex);

    private enum EVRInitError { None = 0 }
    private enum EVRApplicationType { Background = 3 }
    private enum ETrackingUniverseOrigin { Standing = 1 }
    private enum ETrackedPropertyError { Success = 0 }
    private enum ETrackedDeviceProperty { RegisteredDeviceType = 1036 }

    [StructLayout(LayoutKind.Sequential)]
    private struct HmdMatrix34
    {
        public float M00, M01, M02, M03;
        public float M10, M11, M12, M13;
        public float M20, M21, M22, M23;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HmdVector3
    {
        public float X, Y, Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackedDevicePose
    {
        public HmdMatrix34 DeviceToAbsoluteTracking;
        public HmdVector3 Velocity;
        public HmdVector3 AngularVelocity;
        public int TrackingResult;
        public byte PoseIsValidRaw;
        public byte DeviceIsConnectedRaw;

        public readonly bool PoseIsValid => PoseIsValidRaw != 0;
        public readonly bool DeviceIsConnected => DeviceIsConnectedRaw != 0;
    }
}
