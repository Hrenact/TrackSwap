using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Runtime;

internal sealed class CalibrationProfileStore
{
    private readonly object syncRoot = new();

    public CalibrationProfileStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public CalibrationProfilesSnapshot Load()
    {
        lock (syncRoot)
        {
            if (!File.Exists(Path))
            {
                return new CalibrationProfilesSnapshot();
            }
            return JsonConvert.DeserializeObject<CalibrationProfilesSnapshot>(
                File.ReadAllText(Path), RuntimeJson.Settings) ?? new CalibrationProfilesSnapshot();
        }
    }

    public void Add(CalibrationProfile profile)
    {
        lock (syncRoot)
        {
            CalibrationProfilesSnapshot snapshot = LoadUnlocked();
            snapshot.Profiles.RemoveAll(candidate =>
                string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            snapshot.Profiles.Add(profile);
            snapshot.Profiles.Sort((left, right) => right.CapturedAtUtc.CompareTo(left.CapturedAtUtc));
            SaveUnlocked(snapshot);
        }
    }

    public bool Delete(string profileId)
    {
        lock (syncRoot)
        {
            CalibrationProfilesSnapshot snapshot = LoadUnlocked();
            int removed = snapshot.Profiles.RemoveAll(candidate =>
                string.Equals(candidate.ProfileId, profileId, StringComparison.Ordinal));
            if (removed != 0)
            {
                SaveUnlocked(snapshot);
            }
            return removed != 0;
        }
    }

    private CalibrationProfilesSnapshot LoadUnlocked()
    {
        if (!File.Exists(Path))
        {
            return new CalibrationProfilesSnapshot();
        }
        return JsonConvert.DeserializeObject<CalibrationProfilesSnapshot>(
            File.ReadAllText(Path), RuntimeJson.Settings) ?? new CalibrationProfilesSnapshot();
    }

    private void SaveUnlocked(CalibrationProfilesSnapshot snapshot)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("校准档案路径没有上级目录。");
        }
        Directory.CreateDirectory(directory);
        string temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(snapshot, RuntimeJson.Settings));
            if (File.Exists(Path))
            {
                File.Replace(temporaryPath, Path, Path + ".previous", true);
            }
            else
            {
                File.Move(temporaryPath, Path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
