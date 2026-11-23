using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

[Serializable]
public class SaveData
{
    public List<SaveTrack> Tracks;
    public SaveTrack PersonTrack;
}

[Serializable]
public class SaveTrack
{
    public string Name;
    public float MaxSpeedLimit = 2f;
    public float BaseSpeed = 0.2f;
    public List<SaveCurvePoint> SpeedCurve;
    public List<SavePoint> Points;
}

[Serializable]
public class SaveCurvePoint
{
    public float x;
    public float y;
}

[Serializable]
public class SavePoint
{
    public float x;
    public float y;
    public float z;
    public int actId;
}

/// <summary>Unity-independent validation and persistence for orbit save files.</summary>
internal static class OrbitSaveCodec
{
    private const int MaxTracks = 128;
    private const int MaxPointsPerTrack = 4096;
    private const int MaxCurvePoints = 256;
    private const float CurveEpsilon = 0.00001f;

    public static SaveData Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Orbit save is empty.");

        try
        {
            // A concrete DTO rejects array/scalar roots. Keep legacy extra fields compatible.
            var data = JsonConvert.DeserializeObject<SaveData>(json, new JsonSerializerSettings
            {
                MaxDepth = 32,
                CheckAdditionalContent = true,
                TypeNameHandling = TypeNameHandling.None
            });
            return Normalize(data);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Orbit save contains invalid JSON or field types.", ex);
        }
    }

    public static string Serialize(SaveData data)
    {
        return JsonConvert.SerializeObject(Normalize(data), Formatting.Indented);
    }

    public static void SaveAtomic(string path, SaveData data)
    {
        // Validate the entire model before creating files or touching an existing save.
        string json = Serialize(data);
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination);
        Directory.CreateDirectory(directory);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    writer.Write(json);
                    writer.Flush();
                }
                stream.Flush(true);
            }
            // The sibling file stays on the same volume; never delete the old save first.
            File.Move(temporary, destination, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static SaveData Normalize(SaveData data)
    {
        if (data == null) throw new InvalidDataException("Orbit save root must be an object.");
        if (data.Tracks != null && data.Tracks.Count > MaxTracks)
            throw new InvalidDataException("Orbit save exceeds 128 camera tracks.");

        // Build a detached result so rejected saves never partly mutate the caller's model.
        var result = new SaveData { Tracks = new List<SaveTrack>() };
        if (data.Tracks != null)
        {
            foreach (var track in data.Tracks)
                result.Tracks.Add(NormalizeTrack(track));
        }
        result.PersonTrack = NormalizeTrack(data.PersonTrack ?? new SaveTrack { Name = "人物轨道" });
        return result;
    }

    private static SaveTrack NormalizeTrack(SaveTrack track)
    {
        if (track == null) throw new InvalidDataException("Orbit track cannot be null.");
        if (track.Points != null && track.Points.Count > MaxPointsPerTrack)
            throw new InvalidDataException("Orbit track exceeds 4096 points.");
        if (track.SpeedCurve != null && track.SpeedCurve.Count > MaxCurvePoints)
            throw new InvalidDataException("Orbit track exceeds 256 speed curve nodes.");

        var result = new SaveTrack
        {
            Name = track.Name,
            MaxSpeedLimit = NormalizeSpeed(track.MaxSpeedLimit, 2f, "MaxSpeedLimit"),
            BaseSpeed = NormalizeSpeed(track.BaseSpeed, 0.2f, "BaseSpeed"),
            Points = new List<SavePoint>(),
            SpeedCurve = new List<SaveCurvePoint>()
        };
        if (track.Points != null)
        {
            foreach (var point in track.Points)
            {
                if (point == null || !IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z))
                    throw new InvalidDataException("Orbit point coordinates must be finite numbers.");
                result.Points.Add(new SavePoint { x = point.x, y = point.y, z = point.z, actId = point.actId });
            }
        }

        if (track.SpeedCurve != null)
        {
            foreach (var node in track.SpeedCurve)
            {
                if (node == null || !IsFinite(node.x) || !IsFinite(node.y))
                    throw new InvalidDataException("Orbit speed curve coordinates must be finite numbers.");
                float x = Math.Clamp(node.x, 0f, 1f);
                if (x <= CurveEpsilon) x = 0f;
                else if (1f - x <= CurveEpsilon) x = 1f;
                var normalized = new SaveCurvePoint { x = x, y = Math.Clamp(node.y, 0f, result.MaxSpeedLimit) };
                // Remove every nearby node: a new node may bridge two former neighbors.
                result.SpeedCurve.RemoveAll(existing => Math.Abs(existing.x - x) <= CurveEpsilon);
                result.SpeedCurve.Add(normalized);
            }
        }

        result.SpeedCurve.Sort((left, right) => left.x.CompareTo(right.x));
        if (result.SpeedCurve.Count == 0)
        {
            float defaultSpeed = Math.Min(1f, result.MaxSpeedLimit);
            result.SpeedCurve.Add(new SaveCurvePoint { x = 0f, y = defaultSpeed });
            result.SpeedCurve.Add(new SaveCurvePoint { x = 1f, y = defaultSpeed });
        }
        else
        {
            if (result.SpeedCurve[0].x > 0f)
                result.SpeedCurve.Insert(0, new SaveCurvePoint { x = 0f, y = result.SpeedCurve[0].y });
            var last = result.SpeedCurve[result.SpeedCurve.Count - 1];
            if (last.x < 1f) result.SpeedCurve.Add(new SaveCurvePoint { x = 1f, y = last.y });
        }
        // Endpoints are part of the stored curve and must obey the same round-trip limit.
        if (result.SpeedCurve.Count > MaxCurvePoints)
            throw new InvalidDataException("Orbit speed curve including endpoints exceeds 256 nodes.");
        return result;
    }

    private static float NormalizeSpeed(float value, float legacyDefault, string field)
    {
        if (!IsFinite(value) || value < 0f || value > 10f)
            throw new InvalidDataException("Orbit " + field + " must be finite and within (0, 10].");
        return value == 0f ? legacyDefault : value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
