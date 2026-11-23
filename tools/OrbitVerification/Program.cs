using Newtonsoft.Json;

internal static class Program
{
    private static int _passed;

    private static int Main()
    {
        try
        {
            Run("legacy defaults and null collections", LegacyDefaults);
            Run("camera and person BaseSpeed round trip", RoundTrip);
            Run("curve sorting clamping duplicate last wins", NormalizeCurve);
            Run("curve missing endpoints and one node", CurveEndpoints);
            Run("invalid JSON roots and field shapes", InvalidJson);
            Run("nonfinite numbers and null entries", InvalidNumbers);
            Run("positive bounded speeds and legacy zero", SpeedBounds);
            Run("collection bounds and exact boundaries", CollectionBounds);
            Run("normalization leaves caller model unchanged", DetachedNormalization);
            Run("atomic save and failed-save preservation", AtomicSave);
            Run("shot duration bounds and nonfinite values", ShotDurationBounds);
            Run("shot radius bounds and nonfinite values", ShotRadiusBounds);
            Run("shot height bounds and nonfinite values", ShotHeightBounds);
            Run("shot signed sweep bounds and nonfinite values", ShotSweepBounds);
            Run("shot finite center angle and sample time", ShotFiniteInputs);
            Run("shot endpoints clamped time and repeatability", ShotEndpoints);
            Run("shot positive and negative sweep direction", ShotDirection);
            Run("shot constant radius and height", ShotGeometry);
            Run("shot smooth endpoints monotonic progress and no overshoot", ShotEasing);
            Console.WriteLine($"PASS: {_passed} orbit verification groups (10 persistence, 9 shot plan).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void LegacyDefaults()
    {
        var data = OrbitSaveCodec.Deserialize("{\"Tracks\":[{\"Name\":\"legacy\",\"Points\":[{\"x\":1,\"y\":2,\"z\":3,\"actId\":17}]}]}");
        Equal(1, data.Tracks.Count);
        Near(0.2f, data.Tracks[0].BaseSpeed);
        Near(2f, data.Tracks[0].MaxSpeedLimit);
        Equal(17, data.Tracks[0].Points[0].actId);
        DefaultCurve(data.Tracks[0], 1f);
        Check(data.PersonTrack != null, "Missing person track must become a default track.");
        Equal(0, data.PersonTrack.Points.Count);
        Near(0.2f, data.PersonTrack.BaseSpeed);
        DefaultCurve(data.PersonTrack, 1f);

        var empty = OrbitSaveCodec.Deserialize("{}");
        Equal(0, empty.Tracks.Count);
        var nulls = OrbitSaveCodec.Deserialize("{\"Tracks\":null,\"PersonTrack\":{\"Points\":null,\"SpeedCurve\":null}}");
        Equal(0, nulls.Tracks.Count);
        Equal(0, nulls.PersonTrack.Points.Count);
        DefaultCurve(nulls.PersonTrack, 1f);
    }

    private static void RoundTrip()
    {
        var source = Model(new SaveTrack
        {
            Name = "镜头 α",
            BaseSpeed = 0.75f,
            MaxSpeedLimit = 3f,
            Points = new List<SavePoint>
            {
                new SavePoint { x = 1.25f, y = -2.5f, z = 3.75f, actId = 7 },
                new SavePoint { x = 9f, y = 8f, z = 7f, actId = 8 }
            },
            SpeedCurve = Curve((0f, 0.25f), (0.3f, 2f), (1f, 1f))
        });
        source.PersonTrack = new SaveTrack
        {
            Name = "人物",
            BaseSpeed = 0.45f,
            MaxSpeedLimit = 0.5f,
            Points = new List<SavePoint> { new SavePoint { x = -7f, y = 2f, z = 4f, actId = 99 } },
            SpeedCurve = Curve((0f, 0.1f), (1f, 0.4f))
        };
        string json = OrbitSaveCodec.Serialize(source);
        Check(json.Contains(Environment.NewLine) || json.Contains('\n'), "Save must be indented.");
        var saved = OrbitSaveCodec.Deserialize(json);
        Equal("镜头 α", saved.Tracks[0].Name);
        Near(0.75f, saved.Tracks[0].BaseSpeed);
        Near(3f, saved.Tracks[0].MaxSpeedLimit);
        Equal(2, saved.Tracks[0].Points.Count);
        Near(-2.5f, saved.Tracks[0].Points[0].y);
        Equal(8, saved.Tracks[0].Points[1].actId);
        Near(0.45f, saved.PersonTrack.BaseSpeed);
        Near(0.5f, saved.PersonTrack.MaxSpeedLimit);
        Equal(99, saved.PersonTrack.Points[0].actId);
        Near(0.4f, saved.PersonTrack.SpeedCurve[1].y);
        Equal(json, OrbitSaveCodec.Serialize(saved));
    }

    private static void NormalizeCurve()
    {
        var track = new SaveTrack
        {
            MaxSpeedLimit = 2f,
            SpeedCurve = Curve((1.2f, 7f), (0.6f, -5f), (-0.2f, 0.5f), (0.6f, 1.5f), (0.600004f, 1.75f))
        };
        var curve = OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0].SpeedCurve;
        Equal(3, curve.Count);
        Near(0f, curve[0].x);
        Near(0.5f, curve[0].y);
        Near(0.600004f, curve[1].x);
        Near(1.75f, curve[1].y);
        Near(1f, curve[2].x);
        Near(2f, curve[2].y);
        var bridge = new SaveTrack { SpeedCurve = Curve((0.4f, 1f), (0.400018f, 1.2f), (0.400009f, 1.8f)) };
        var merged = OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(bridge))).Tracks[0].SpeedCurve;
        Equal(3, merged.Count); // inserted endpoints plus the last node
        Near(1.8f, merged[1].y);
    }

    private static void CurveEndpoints()
    {
        var track = new SaveTrack { MaxSpeedLimit = 0.25f };
        DefaultCurve(OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0], 0.25f);
        track.SpeedCurve = Curve((0.5f, 0.2f));
        var curve = OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0].SpeedCurve;
        Equal(3, curve.Count);
        Near(0f, curve[0].x);
        Near(1f, curve[2].x);
        Near(0.2f, curve[0].y);
        Near(0.2f, curve[2].y);
        track.SpeedCurve = Curve((0.000004f, 0.1f), (0.999996f, 0.2f));
        var snapped = OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0].SpeedCurve;
        Equal(2, snapped.Count);
        Near(0f, snapped[0].x);
        Near(1f, snapped[1].x);
    }

    private static void InvalidJson()
    {
        foreach (string json in new[] { "", " ", "null", "[]", "42", "true", "\"text\"", "{", "{}{}", "{\"Tracks\":{}}", "{\"Tracks\":[null]}" })
            Throws<InvalidDataException>(() => OrbitSaveCodec.Deserialize(json));
        Throws<InvalidDataException>(() => OrbitSaveCodec.Deserialize(null));
        Throws<InvalidDataException>(() => OrbitSaveCodec.Serialize(null));
    }

    private static void InvalidNumbers()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (string field in new[] { "x", "y", "z" })
            {
                var point = new SavePoint();
                if (field == "x") point.x = invalid;
                if (field == "y") point.y = invalid;
                if (field == "z") point.z = invalid;
                RejectBoth(Model(new SaveTrack { Points = new List<SavePoint> { point } }));
            }
            RejectBoth(Model(new SaveTrack { SpeedCurve = Curve((invalid, 1f)) }));
            RejectBoth(Model(new SaveTrack { SpeedCurve = Curve((0.5f, invalid)) }));
            RejectBoth(Model(new SaveTrack { BaseSpeed = invalid }));
            RejectBoth(Model(new SaveTrack { MaxSpeedLimit = invalid }));
        }
        RejectBoth(Model(new SaveTrack { Points = new List<SavePoint> { null } }));
        RejectBoth(Model(new SaveTrack { SpeedCurve = new List<SaveCurvePoint> { null } }));
        var personInvalid = Model(new SaveTrack());
        personInvalid.PersonTrack = new SaveTrack { Points = new List<SavePoint> { new SavePoint { x = float.NaN } } };
        RejectBoth(personInvalid);
    }

    private static void SpeedBounds()
    {
        var legacy = OrbitSaveCodec.Deserialize("{\"Tracks\":[{\"BaseSpeed\":0,\"MaxSpeedLimit\":0}]}");
        Near(0.2f, legacy.Tracks[0].BaseSpeed);
        Near(2f, legacy.Tracks[0].MaxSpeedLimit);
        foreach (float invalid in new[] { -1f, 10.01f })
        {
            RejectBoth(Model(new SaveTrack { BaseSpeed = invalid }));
            RejectBoth(Model(new SaveTrack { MaxSpeedLimit = invalid }));
        }
        var valid = OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(new SaveTrack { BaseSpeed = 10f, MaxSpeedLimit = 10f })));
        Near(10f, valid.Tracks[0].BaseSpeed);
        Near(10f, valid.Tracks[0].MaxSpeedLimit);
    }

    private static void CollectionBounds()
    {
        var model = new SaveData { Tracks = Enumerable.Range(0, 128).Select(_ => new SaveTrack()).ToList() };
        Equal(128, OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(model)).Tracks.Count);
        model.Tracks.Add(new SaveTrack());
        RejectBoth(model);

        var track = new SaveTrack { Points = Enumerable.Range(0, 4096).Select(i => new SavePoint { x = i }).ToList() };
        Equal(4096, OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0].Points.Count);
        track.Points.Add(new SavePoint());
        RejectBoth(Model(track));

        track = new SaveTrack { SpeedCurve = Enumerable.Range(0, 256).Select(i => new SaveCurvePoint { x = i / 255f, y = 1f }).ToList() };
        Equal(256, OrbitSaveCodec.Deserialize(OrbitSaveCodec.Serialize(Model(track))).Tracks[0].SpeedCurve.Count);
        track.SpeedCurve.Add(new SaveCurvePoint { x = 0.5f, y = 1f });
        RejectBoth(Model(track));
        // A serialized model must not exceed the cap after implicit endpoint insertion either.
        track.SpeedCurve = Enumerable.Range(1, 256).Select(i => new SaveCurvePoint { x = i / 257f, y = 1f }).ToList();
        RejectBoth(Model(track));
        var person = new SaveData { PersonTrack = new SaveTrack { Points = Enumerable.Range(0, 4097).Select(_ => new SavePoint()).ToList() } };
        RejectBoth(person);
    }

    private static void DetachedNormalization()
    {
        var original = new SaveTrack { BaseSpeed = 0f, SpeedCurve = Curve((2f, 12f), (-1f, -1f)) };
        var data = Model(original);
        OrbitSaveCodec.Serialize(data);
        Near(0f, original.BaseSpeed);
        Near(2f, original.SpeedCurve[0].x);
        Near(12f, original.SpeedCurve[0].y);
        Check(original.Points == null && data.PersonTrack == null, "Normalization must not mutate the supplied model.");
        data.PersonTrack = new SaveTrack { BaseSpeed = float.NaN };
        Throws<InvalidDataException>(() => OrbitSaveCodec.Serialize(data));
        Near(2f, original.SpeedCurve[0].x);
    }

    private static void AtomicSave()
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "verification-" + Guid.NewGuid().ToString("N")));
        string path = Path.Combine(directory, "orbit_data.json");
        Check(directory.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase), "Test files must stay in the verification output directory.");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "previous save");
            var invalid = Model(new SaveTrack { Points = new List<SavePoint> { new SavePoint { z = float.NaN } } });
            Throws<InvalidDataException>(() => OrbitSaveCodec.SaveAtomic(path, invalid));
            Equal("previous save", File.ReadAllText(path));
            Equal(0, Directory.GetFiles(directory, "*.tmp").Length);

            var valid = Model(new SaveTrack { Name = "atomic", BaseSpeed = 0.6f });
            OrbitSaveCodec.SaveAtomic(path, valid);
            string previous = File.ReadAllText(path);
            Near(0.6f, OrbitSaveCodec.Deserialize(previous).Tracks[0].BaseSpeed);
            Equal(0, Directory.GetFiles(directory, "*.tmp").Length);

            // Windows prevents replacing an exclusively open file. A failed move must retain it.
            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ThrowsFileAccess(() => OrbitSaveCodec.SaveAtomic(path, Model(new SaveTrack { Name = "replacement" })));
                }
                Equal(previous, File.ReadAllText(path));
                Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            string fresh = Path.Combine(directory, "fresh.json");
            OrbitSaveCodec.SaveAtomic(fresh, valid);
            Equal("atomic", OrbitSaveCodec.Deserialize(File.ReadAllText(fresh)).Tracks[0].Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static OrbitShotPlan Shot(float duration = 8f, float radius = 7f, float height = 3f,
        float sweep = 90f, float start = 0f, float x = 0f, float y = 0f, float z = 0f)
        => new OrbitShotPlan(duration, radius, height, sweep, start, x, y, z);

    private static void ShotRange(Action<float> construct, float min, float max)
    {
        construct(min);
        construct(max);
        foreach (float invalid in new[] { MathF.BitDecrement(min), MathF.BitIncrement(max),
            float.MinValue, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws<ArgumentException>(() => construct(invalid));
    }

    private static void ShotDurationBounds()
        => ShotRange(value => Near(value, Shot(duration: value).Duration), 2f, 120f);

    private static void ShotRadiusBounds()
        => ShotRange(value => Near(value, Shot(radius: value).Radius), 1f, 50f);

    private static void ShotHeightBounds()
        => ShotRange(value => Near(value, Shot(height: value).Height), 0.5f, 20f);

    private static void ShotSweepBounds()
    {
        foreach (float valid in new[] { -360f, -1f, 1f, 360f })
            Near(valid, Shot(sweep: valid).SweepDegrees);
        foreach (float invalid in new[] { MathF.BitDecrement(-360f), MathF.BitIncrement(360f),
            MathF.BitIncrement(-1f), -0.5f, 0f, 0.5f, MathF.BitDecrement(1f),
            float.MinValue, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws<ArgumentException>(() => Shot(sweep: invalid));
    }

    private static void ShotFiniteInputs()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Throws<ArgumentException>(() => Shot(start: invalid));
            Throws<ArgumentException>(() => Shot(x: invalid));
            Throws<ArgumentException>(() => Shot(y: invalid));
            Throws<ArgumentException>(() => Shot(z: invalid));
            Throws<ArgumentException>(() => Shot().Sample(invalid));
        }
        foreach (float finite in new[] { float.MinValue, -1f, 0f, 1f, float.MaxValue })
        {
            Equal(finite, Shot(start: finite).StartRadians);
            Equal(finite, Shot(x: finite).CenterX);
            Equal(finite, Shot(y: finite).CenterY);
            Equal(finite, Shot(z: finite).CenterZ);
        }
    }

    private static void ShotEndpoints()
    {
        foreach (float sweep in new[] { -360f, -125f, -1f, 1f, 125f, 360f })
        {
            var plan = Shot(duration: 12f, radius: 4f, height: 2.5f, sweep: sweep,
                start: 0.37f, x: -3f, y: 5f, z: 9f);
            var first = plan.Sample(0f);
            var last = plan.Sample(plan.Duration);
            PositionNear(plan, first, plan.StartRadians);
            PositionNear(plan, last, plan.StartRadians + sweep * Math.PI / 180d);
            foreach (float elapsed in new[] { float.MinValue, -12f, -0.001f })
                Equal(first, plan.Sample(elapsed));
            foreach (float elapsed in new[] { 12.001f, 24f, float.MaxValue })
                Equal(last, plan.Sample(elapsed));
            Equal(plan.Sample(4.123f), plan.Sample(4.123f));
            PositionNear(plan, plan.Sample(3f), plan.StartRadians + sweep * Math.PI / 180d * 0.15625d);
            PositionNear(plan, plan.Sample(9f), plan.StartRadians + sweep * Math.PI / 180d * 0.84375d);
        }
    }

    private static void ShotDirection()
    {
        double diagonal = 4d / Math.Sqrt(2d);
        foreach (float sign in new[] { -1f, 1f })
        {
            var plan = Shot(radius: 4f, sweep: sign * 90f);
            var middle = plan.Sample(plan.Duration / 2f);
            Close(sign * diagonal, middle.x);
            Close(diagonal, middle.z);
            var end = plan.Sample(plan.Duration);
            Close(sign * 4d, end.x);
            Close(0d, end.z);
        }
    }

    private static void ShotGeometry()
    {
        foreach (float radius in new[] { 1f, 7f, 50f })
        foreach (float height in new[] { 0.5f, 3f, 20f })
        foreach (float sweep in new[] { -360f, -90f, 1f, 90f, 360f })
        {
            var plan = Shot(radius: radius, height: height, sweep: sweep, start: -0.8f,
                x: -13.25f, y: 2.5f, z: 17.75f);
            for (int i = 0; i <= 200; i++)
            {
                var point = plan.Sample(plan.Duration * i / 200f);
                double dx = point.x - (double)plan.CenterX;
                double dz = point.z - (double)plan.CenterZ;
                Close(radius, Math.Sqrt(dx * dx + dz * dz));
                Close(plan.CenterY + height, point.y);
            }
        }
    }

    private static void ShotEasing()
    {
        // Unwrap measured positions so full circles and negative sweeps are checked independently
        // of the production easing expression, including the atan2 branch cut.
        const int steps = 200;
        foreach (float sweep in new[] { -360f, -270f, -90f, -1f, 1f, 90f, 270f, 360f })
        foreach (float start in new[] { -2.8f, 0f, 2.8f })
        {
            var plan = Shot(sweep: sweep, start: start);
            var first = plan.Sample(0f);
            double previous = Math.Atan2(first.x, first.z);
            double progress = 0d;
            var increments = new double[steps];
            double total = Math.Abs(sweep) * Math.PI / 180d;
            for (int i = 1; i <= steps; i++)
            {
                var point = plan.Sample(plan.Duration * i / steps);
                double angle = Math.Atan2(point.x, point.z);
                double delta = angle - previous;
                if (delta > Math.PI) delta -= 2d * Math.PI;
                if (delta < -Math.PI) delta += 2d * Math.PI;
                double directed = delta * Math.Sign(sweep);
                Check(directed >= -0.000002d, $"Sweep {sweep} reversed at sample {i}.");
                progress += directed;
                Check(progress >= -0.000002d && progress <= total + 0.000002d,
                    $"Sweep {sweep} overshot at sample {i}: {progress} / {total}.");
                increments[i - 1] = directed;
                previous = angle;
            }
            Close(total, progress);
            Check(increments[0] > 0d && increments[steps - 1] > 0d,
                "The path must begin and end moving in the requested direction.");
            Check(increments[0] < increments[steps / 2] * 0.03d &&
                increments[steps - 1] < increments[steps / 2] * 0.03d,
                "Endpoint angular speed must ease toward zero relative to the middle.");
            Close(increments[0], increments[steps - 1], 0.000002d);
        }
    }

    private static void PositionNear(OrbitShotPlan plan, (float x, float y, float z) point, double angle)
    {
        Close(plan.CenterX + Math.Sin(angle) * plan.Radius, point.x);
        Close(plan.CenterY + plan.Height, point.y);
        Close(plan.CenterZ + Math.Cos(angle) * plan.Radius, point.z);
    }

    private static void Close(double expected, double actual, double tolerance = 0.00002d)
        => Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected}, got {actual} (tolerance {tolerance}).");

    private static SaveData Model(SaveTrack track) => new SaveData { Tracks = new List<SaveTrack> { track } };

    private static List<SaveCurvePoint> Curve(params (float x, float y)[] values)
        => values.Select(value => new SaveCurvePoint { x = value.x, y = value.y }).ToList();

    private static void RejectBoth(SaveData data)
    {
        Throws<InvalidDataException>(() => OrbitSaveCodec.Serialize(data));
        string json = JsonConvert.SerializeObject(data);
        Throws<InvalidDataException>(() => OrbitSaveCodec.Deserialize(json));
    }

    private static void DefaultCurve(SaveTrack track, float speed)
    {
        Equal(2, track.SpeedCurve.Count);
        Near(0f, track.SpeedCurve[0].x);
        Near(1f, track.SpeedCurve[1].x);
        Near(speed, track.SpeedCurve[0].y);
        Near(speed, track.SpeedCurve[1].y);
    }

    private static void Near(float expected, float actual)
        => Check(float.IsFinite(actual) && Math.Abs(expected - actual) < 0.000001f, $"Expected {expected}, got {actual}.");

    private static void Equal<T>(T expected, T actual)
        => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ThrowsFileAccess(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException("Expected a file access failure.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
