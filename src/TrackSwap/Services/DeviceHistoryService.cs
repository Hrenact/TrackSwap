using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TrackSwap.Models;
using TrackSwap.Protocol;

namespace TrackSwap.Services
{
    public sealed class DeviceHistoryService
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly string _filePath;

        public DeviceHistoryService(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TrackSwap",
                "device-history.json");
        }

        public string FilePath => _filePath;

        public IReadOnlyList<DeviceOption> Load()
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<DeviceOption>();
            }

            try
            {
                var records = JsonConvert.DeserializeObject<List<DeviceHistoryRecord>>(
                    File.ReadAllText(_filePath),
                    JsonSettings) ?? new List<DeviceHistoryRecord>();
                return records
                    .Where(record => IsPhysicalDevicePath(record.DevicePath))
                    .GroupBy(record => record.DevicePath, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .Select(record => record.ToDeviceOption())
                    .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<DeviceOption>();
            }
            catch (IOException)
            {
                return Array.Empty<DeviceOption>();
            }
        }

        public void Remember(IEnumerable<DeviceOption> onlineDevices)
        {
            var records = Load()
                .ToDictionary(
                    device => device.DevicePath,
                    DeviceHistoryRecord.FromDevice,
                    StringComparer.Ordinal);
            bool changed = false;
            foreach (DeviceOption device in onlineDevices ?? Enumerable.Empty<DeviceOption>())
            {
                if (!IsPhysicalDevicePath(device.DevicePath))
                {
                    continue;
                }

                DeviceHistoryRecord current = DeviceHistoryRecord.FromDevice(device);
                if (!records.TryGetValue(device.DevicePath, out DeviceHistoryRecord previous) ||
                    !previous.HasSameMetadata(current))
                {
                    records[device.DevicePath] = current;
                    changed = true;
                }
            }

            if (changed)
            {
                Write(records.Values.OrderBy(record => record.DisplayName, StringComparer.CurrentCultureIgnoreCase));
            }
        }

        public void Clear()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        private void Write(IEnumerable<DeviceHistoryRecord> records)
        {
            string directory = Path.GetDirectoryName(_filePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(records, JsonSettings));
            if (File.Exists(_filePath))
            {
                File.Replace(temporaryPath, _filePath, null);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }

        private static bool IsPhysicalDevicePath(string devicePath)
        {
            return !string.IsNullOrWhiteSpace(devicePath) &&
                devicePath.StartsWith("/devices/", StringComparison.Ordinal) &&
                !ProtocolConstants.IsTrackSwapVirtualDevicePath(devicePath);
        }

        private sealed class DeviceHistoryRecord
        {
            public string DisplayName { get; set; }
            public string DevicePath { get; set; }
            public string SerialNumber { get; set; }
            public string RoleTargetPath { get; set; }
            public string RenderModelName { get; set; }
            public TrackedDeviceKind DeviceKind { get; set; }

            public static DeviceHistoryRecord FromDevice(DeviceOption device)
            {
                return new DeviceHistoryRecord
                {
                    DisplayName = DeviceOption.BaseDisplayName(device.DisplayName),
                    DevicePath = device.DevicePath,
                    SerialNumber = device.SerialNumber,
                    RoleTargetPath = device.RoleTargetPath,
                    RenderModelName = device.RenderModelName,
                    DeviceKind = device.DeviceKind
                };
            }

            public DeviceOption ToDeviceOption()
            {
                return new DeviceOption(
                    DeviceOption.BaseDisplayName(DisplayName),
                    DevicePath,
                    false,
                    null,
                    SerialNumber,
                    RoleTargetPath,
                    RenderModelName,
                    DeviceKind);
            }

            public bool HasSameMetadata(DeviceHistoryRecord other)
            {
                return string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
                    string.Equals(SerialNumber, other.SerialNumber, StringComparison.Ordinal) &&
                    string.Equals(RoleTargetPath, other.RoleTargetPath, StringComparison.Ordinal) &&
                    string.Equals(RenderModelName, other.RenderModelName, StringComparison.Ordinal) &&
                    DeviceKind == other.DeviceKind;
            }
        }
    }
}
