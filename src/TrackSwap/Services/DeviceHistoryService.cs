using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrackSwap.Models;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    public sealed class DeviceHistoryService
    {
        private const int CurrentSchemaVersion = 2;
        private const int MaximumDisplayNameLength = 64;
        private const int MaximumNoteLength = 256;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly string _filePath;

        public DeviceHistoryService(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                TrackSwapDataPaths.ActiveDataDirectory,
                "device-history.json");
        }

        public string FilePath => _filePath;

        public IReadOnlyList<DeviceOption> Load()
        {
            return LoadCatalog().Devices
                .Select(device => device.ToDeviceOption())
                .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public DeviceManagementCatalog LoadCatalog()
        {
            DeviceHistoryDocument document = LoadDocument(out _);
            IReadOnlyList<ManagedDeviceRecord> devices = document.Devices
                .Where(record => IsPhysicalDevicePath(record.DevicePath))
                .GroupBy(record => record.DevicePath, StringComparer.Ordinal)
                .Select(group => group.Last().ToManagedRecord())
                .OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            IReadOnlyList<ManagedReceiverRecord> receivers = document.Receivers
                .Where(record => !string.IsNullOrWhiteSpace(record.ReceiverId))
                .GroupBy(record => record.ReceiverId, StringComparer.Ordinal)
                .Select(group => group.Last().ToManagedRecord())
                .OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new DeviceManagementCatalog(devices, receivers);
        }

        public IReadOnlyList<DeviceOption> ApplyCustomNames(IEnumerable<DeviceOption> devices)
        {
            var customNames = LoadCatalog().Devices
                .Where(device => !string.IsNullOrWhiteSpace(device.CustomDisplayName))
                .ToDictionary(device => device.DevicePath, device => device.CustomDisplayName, StringComparer.Ordinal);
            return (devices ?? Enumerable.Empty<DeviceOption>())
                .Select(device => CloneWithDisplayName(
                    device,
                    customNames.TryGetValue(device.DevicePath, out string customName)
                        ? customName
                        : device.DisplayName))
                .ToList();
        }

        public void Remember(IEnumerable<DeviceOption> onlineDevices)
        {
            DeviceHistoryDocument document = LoadDocument(out bool wasLegacy);
            var devices = document.Devices
                .Where(record => IsPhysicalDevicePath(record.DevicePath))
                .GroupBy(record => record.DevicePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var receivers = document.Receivers
                .Where(record => !string.IsNullOrWhiteSpace(record.ReceiverId))
                .GroupBy(record => record.ReceiverId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            bool changed = wasLegacy;

            foreach (DeviceOption device in onlineDevices ?? Enumerable.Empty<DeviceOption>())
            {
                if (!IsPhysicalDevicePath(device.DevicePath))
                {
                    continue;
                }

                if (!devices.TryGetValue(device.DevicePath, out DeviceHistoryRecord record))
                {
                    record = DeviceHistoryRecord.FromDevice(device);
                    devices.Add(device.DevicePath, record);
                    changed = true;
                }
                else if (record.UpdateHardwareMetadata(device))
                {
                    changed = true;
                }

                string receiverId = NormalizeIdentifier(device.ConnectedWirelessDongleId);
                if (!string.IsNullOrEmpty(receiverId))
                {
                    if (!string.Equals(record.LastConnectedReceiverId, receiverId, StringComparison.Ordinal))
                    {
                        record.LastConnectedReceiverId = receiverId;
                        changed = true;
                    }
                    if (!receivers.ContainsKey(receiverId))
                    {
                        receivers.Add(receiverId, new ReceiverHistoryRecord { ReceiverId = receiverId });
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                document.Devices = devices.Values
                    .OrderBy(record => record.EffectiveDisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                document.Receivers = receivers.Values
                    .OrderBy(record => record.EffectiveDisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                Write(document);
            }
        }

        public void UpdateDeviceMetadata(string devicePath, string customDisplayName, string note)
        {
            DeviceHistoryDocument document = LoadDocument(out _);
            DeviceHistoryRecord record = document.Devices.LastOrDefault(candidate =>
                string.Equals(candidate.DevicePath, devicePath, StringComparison.Ordinal));
            if (record == null)
            {
                throw new InvalidOperationException("The device record no longer exists.");
            }

            record.CustomDisplayName = NormalizeUserText(customDisplayName, MaximumDisplayNameLength);
            record.Note = NormalizeUserText(note, MaximumNoteLength);
            Write(document);
        }

        public void UpdateReceiverMetadata(string receiverId, string customDisplayName, string note)
        {
            DeviceHistoryDocument document = LoadDocument(out _);
            ReceiverHistoryRecord record = document.Receivers.LastOrDefault(candidate =>
                string.Equals(candidate.ReceiverId, receiverId, StringComparison.Ordinal));
            if (record == null)
            {
                throw new InvalidOperationException("The receiver record no longer exists.");
            }

            record.CustomDisplayName = NormalizeUserText(customDisplayName, MaximumDisplayNameLength);
            record.Note = NormalizeUserText(note, MaximumNoteLength);
            Write(document);
        }

        public void Clear()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        private DeviceHistoryDocument LoadDocument(out bool wasLegacy)
        {
            wasLegacy = false;
            if (!File.Exists(_filePath))
            {
                return new DeviceHistoryDocument();
            }

            try
            {
                JToken root = JToken.Parse(File.ReadAllText(_filePath));
                if (root.Type == JTokenType.Array)
                {
                    wasLegacy = true;
                    return new DeviceHistoryDocument
                    {
                        Devices = root.ToObject<List<DeviceHistoryRecord>>() ?? new List<DeviceHistoryRecord>()
                    };
                }

                DeviceHistoryDocument document = root.ToObject<DeviceHistoryDocument>() ?? new DeviceHistoryDocument();
                document.Devices = document.Devices ?? new List<DeviceHistoryRecord>();
                document.Receivers = document.Receivers ?? new List<ReceiverHistoryRecord>();
                return document;
            }
            catch (JsonException)
            {
                return new DeviceHistoryDocument();
            }
            catch (IOException)
            {
                return new DeviceHistoryDocument();
            }
        }

        private void Write(DeviceHistoryDocument document)
        {
            document.SchemaVersion = CurrentSchemaVersion;
            string directory = Path.GetDirectoryName(_filePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, JsonSettings));
            if (File.Exists(_filePath))
            {
                File.Replace(temporaryPath, _filePath, null);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }

        private static DeviceOption CloneWithDisplayName(DeviceOption device, string displayName)
        {
            return new DeviceOption(
                displayName,
                device.DevicePath,
                device.IsOnline,
                device.DeviceIndex,
                device.SerialNumber,
                device.RoleTargetPath,
                device.RenderModelName,
                device.DeviceKind,
                device.PoseSourceKind,
                device.ConnectedWirelessDongleId,
                device.ManufacturerName,
                device.ModelNumber,
                device.TrackingSystemName,
                device.ControllerType,
                device.HardwareRevision,
                device.TrackingFirmwareVersion);
        }

        private static string NormalizeIdentifier(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeUserText(string value, int maximumLength)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized != null && normalized.Length > maximumLength)
            {
                throw new ArgumentException("The value is too long.");
            }
            return normalized;
        }

        private static bool IsPhysicalDevicePath(string devicePath)
        {
            return !string.IsNullOrWhiteSpace(devicePath) &&
                devicePath.StartsWith("/devices/", StringComparison.Ordinal) &&
                !ProtocolConstants.IsTrackSwapVirtualDevicePath(devicePath);
        }

        private sealed class DeviceHistoryDocument
        {
            public int SchemaVersion { get; set; } = CurrentSchemaVersion;
            public List<DeviceHistoryRecord> Devices { get; set; } = new List<DeviceHistoryRecord>();
            public List<ReceiverHistoryRecord> Receivers { get; set; } = new List<ReceiverHistoryRecord>();
        }

        private sealed class DeviceHistoryRecord
        {
            public string DisplayName { get; set; }
            public string DevicePath { get; set; }
            public string SerialNumber { get; set; }
            public string RoleTargetPath { get; set; }
            public string RenderModelName { get; set; }
            public TrackedDeviceKind DeviceKind { get; set; }
            public string CustomDisplayName { get; set; }
            public string Note { get; set; }
            public string LastConnectedReceiverId { get; set; }

            [JsonIgnore]
            public string EffectiveDisplayName => string.IsNullOrWhiteSpace(CustomDisplayName)
                ? DeviceOption.BaseDisplayName(DisplayName)
                : CustomDisplayName;

            public static DeviceHistoryRecord FromDevice(DeviceOption device)
            {
                return new DeviceHistoryRecord
                {
                    DisplayName = DeviceOption.BaseDisplayName(device.DisplayName),
                    DevicePath = device.DevicePath,
                    SerialNumber = device.SerialNumber,
                    RoleTargetPath = device.RoleTargetPath,
                    RenderModelName = device.RenderModelName,
                    DeviceKind = device.DeviceKind,
                    LastConnectedReceiverId = NormalizeIdentifier(device.ConnectedWirelessDongleId)
                };
            }

            public bool UpdateHardwareMetadata(DeviceOption device)
            {
                string displayName = DeviceOption.BaseDisplayName(device.DisplayName);
                bool changed = !string.Equals(DisplayName, displayName, StringComparison.Ordinal) ||
                    !string.Equals(SerialNumber, device.SerialNumber, StringComparison.Ordinal) ||
                    !string.Equals(RoleTargetPath, device.RoleTargetPath, StringComparison.Ordinal) ||
                    !string.Equals(RenderModelName, device.RenderModelName, StringComparison.Ordinal) ||
                    DeviceKind != device.DeviceKind;
                DisplayName = displayName;
                SerialNumber = device.SerialNumber;
                RoleTargetPath = device.RoleTargetPath;
                RenderModelName = device.RenderModelName;
                DeviceKind = device.DeviceKind;
                return changed;
            }

            public ManagedDeviceRecord ToManagedRecord()
            {
                return new ManagedDeviceRecord(
                    EffectiveDisplayName,
                    DeviceOption.BaseDisplayName(DisplayName),
                    DevicePath,
                    SerialNumber,
                    RoleTargetPath,
                    RenderModelName,
                    DeviceKind,
                    CustomDisplayName,
                    Note,
                    LastConnectedReceiverId);
            }
        }

        private sealed class ReceiverHistoryRecord
        {
            public string ReceiverId { get; set; }
            public string CustomDisplayName { get; set; }
            public string Note { get; set; }

            [JsonIgnore]
            public string EffectiveDisplayName => string.IsNullOrWhiteSpace(CustomDisplayName)
                ? ReceiverId
                : CustomDisplayName;

            public ManagedReceiverRecord ToManagedRecord()
            {
                return new ManagedReceiverRecord(ReceiverId, EffectiveDisplayName, CustomDisplayName, Note);
            }
        }
    }

    public sealed class DeviceManagementCatalog
    {
        public DeviceManagementCatalog(
            IReadOnlyList<ManagedDeviceRecord> devices,
            IReadOnlyList<ManagedReceiverRecord> receivers)
        {
            Devices = devices ?? Array.Empty<ManagedDeviceRecord>();
            Receivers = receivers ?? Array.Empty<ManagedReceiverRecord>();
        }

        public IReadOnlyList<ManagedDeviceRecord> Devices { get; }
        public IReadOnlyList<ManagedReceiverRecord> Receivers { get; }
    }

    public sealed class ManagedDeviceRecord
    {
        public ManagedDeviceRecord(
            string displayName,
            string hardwareDisplayName,
            string devicePath,
            string serialNumber,
            string roleTargetPath,
            string renderModelName,
            TrackedDeviceKind deviceKind,
            string customDisplayName,
            string note,
            string lastConnectedReceiverId)
        {
            DisplayName = displayName;
            HardwareDisplayName = hardwareDisplayName;
            DevicePath = devicePath;
            SerialNumber = serialNumber;
            RoleTargetPath = roleTargetPath;
            RenderModelName = renderModelName;
            DeviceKind = deviceKind;
            CustomDisplayName = customDisplayName;
            Note = note;
            LastConnectedReceiverId = lastConnectedReceiverId;
        }

        public string DisplayName { get; }
        public string HardwareDisplayName { get; }
        public string DevicePath { get; }
        public string SerialNumber { get; }
        public string RoleTargetPath { get; }
        public string RenderModelName { get; }
        public TrackedDeviceKind DeviceKind { get; }
        public string CustomDisplayName { get; }
        public string Note { get; }
        public string LastConnectedReceiverId { get; }

        public DeviceOption ToDeviceOption()
        {
            return new DeviceOption(
                DisplayName,
                DevicePath,
                false,
                null,
                SerialNumber,
                RoleTargetPath,
                RenderModelName,
                DeviceKind);
        }
    }

    public sealed class ManagedReceiverRecord
    {
        public ManagedReceiverRecord(string receiverId, string displayName, string customDisplayName, string note)
        {
            ReceiverId = receiverId;
            DisplayName = displayName;
            CustomDisplayName = customDisplayName;
            Note = note;
        }

        public string ReceiverId { get; }
        public string DisplayName { get; }
        public string CustomDisplayName { get; }
        public string Note { get; }
    }
}
