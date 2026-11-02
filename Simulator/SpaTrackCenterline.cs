using System.Windows.Media.Media3D;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// Spa-Francorchamps inspired closed track (original approximate layout, not licensed).
/// Multi-km feel with Eau Rouge/Raidillon climb and long Kemmel; Z is elevation.
/// Provides densified centerline, ribbon edge frames, and terrain height sampling.
/// </summary>
public static class SpaTrackCenterline
{
    public const double TrackWidth = 12.0;
    public const double BarrierOffset = 2.0;
    public const double AsphaltLift = 0.06;

    // Simple non-crossing elongated loop (plan-view ring) with Spa-like elevation:
    // south pit → SE Eau Rouge/Raidillon climb → north Kemmel → west La Source.
    // Verified: no segment crossings; nonlocal clearance >> track width.
    private static readonly Point3D[] KeyPoints =
    {
        new(0, -220, 42),
        new(400, -220, 42),
        new(900, -210, 41),
        new(1400, -200, 40),
        new(1800, -180, 36),
        new(2100, -100, 34),
        new(2300, 40, 55),
        new(2400, 200, 78),
        new(2350, 360, 92),
        new(2100, 480, 96),
        new(1600, 520, 97),
        new(1100, 530, 96),
        new(600, 500, 94),
        new(200, 450, 88),
        new(-50, 320, 70),
        new(-180, 160, 52),
        new(-220, 20, 44),
        new(-200, -100, 42),
        new(-80, -180, 42),
        new(-20, -210, 42),
    };

    private static List<Point3D>? _dense;
    private static double[]? _cumulative;
    private static Vector3D[]? _leftNormals;
    private static double _minZ;
    private static double _maxZ;
    public static IReadOnlyList<Point3D> DenseCenterline => EnsureDense();

    public static IReadOnlyList<Vector3D> LeftNormals
    {
        get
        {
            EnsureDense();
            return _leftNormals!;
        }
    }

    public static (double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ) GetBounds()
    {
        var pts = EnsureDense();
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        return (minX, maxX, minY, maxY, _minZ, _maxZ);
    }

    public static (double X, double Y, double Z, double HeadingDeg) GetStartPose()
    {
        var pts = EnsureDense();
        var a = pts[0];
        var b = pts[Math.Min(10, pts.Count - 1)];
        var heading = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
        return (a.X, a.Y, a.Z + AsphaltLift, heading);
    }

    public static double ApproximateLapLengthMeters()
    {
        EnsureDense();
        return _cumulative![^1];
    }

    /// <summary>
    /// Terrain height under (x,y): IDW from centerline, sits just under asphalt near the ribbon.
    /// </summary>
    public static double SampleTerrainZ(double x, double y)
    {
        var pts = EnsureDense();
        double wSum = 0;
        double zSum = 0;
        var nearest = double.MaxValue;

        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var dx = x - p.X;
            var dy = y - p.Y;
            var d2 = dx * dx + dy * dy;
            nearest = Math.Min(nearest, d2);
            var w = 1.0 / (d2 + 25.0); // 5 m softening
            wSum += w;
            zSum += w * p.Z;
        }

        var idw = zSum / Math.Max(wSum, 1e-9);
        var nearestDist = Math.Sqrt(nearest);
        var halfW = TrackWidth * 0.5;

        // Far from track → settle toward valley floor slightly below min track Z.
        var valley = _minZ - 6.0;
        var farBlend = Math.Clamp((nearestDist - 40.0) / 220.0, 0.0, 1.0);
        var ground = idw * (1.0 - farBlend) + valley * farBlend;

        // Under the ribbon corridor, keep terrain a few cm below asphalt.
        if (nearestDist < halfW + 8.0)
        {
            // Pull strongly toward nearest track Z so the ribbon does not float.
            ground = idw - 0.04;
        }

        return ground;
    }

    public static bool TrySampleAt(
        double x,
        double y,
        out double z,
        out Vector3D tangent,
        out double distanceAlong)
    {
        var pts = EnsureDense();
        var bestDist2 = double.MaxValue;
        var bestIndex = 0;
        var bestT = 0.0;

        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var abLen2 = abx * abx + aby * aby;
            if (abLen2 < 1e-6) continue;

            var t = Math.Clamp(((x - a.X) * abx + (y - a.Y) * aby) / abLen2, 0.0, 1.0);
            var px = a.X + abx * t;
            var py = a.Y + aby * t;
            var dx = x - px;
            var dy = y - py;
            var d2 = dx * dx + dy * dy;
            if (d2 >= bestDist2) continue;
            bestDist2 = d2;
            bestIndex = i;
            bestT = t;
        }

        var p0 = pts[bestIndex];
        var p1 = pts[(bestIndex + 1) % pts.Count];
        z = p0.Z + (p1.Z - p0.Z) * bestT + AsphaltLift;

        // Smooth tangent from neighboring segments to reduce pitch tick noise.
        var tPrev = SegmentTangent(pts, (bestIndex - 1 + pts.Count) % pts.Count);
        var tCur = SegmentTangent(pts, bestIndex);
        var tNext = SegmentTangent(pts, (bestIndex + 1) % pts.Count);
        var blended = tPrev * (1.0 - bestT) * 0.25 + tCur * 0.5 + tNext * bestT * 0.25;
        if (blended.Length < 1e-9) blended = tCur;
        blended.Normalize();
        tangent = blended;

        var abx2 = p1.X - p0.X;
        var aby2 = p1.Y - p0.Y;
        distanceAlong = _cumulative![bestIndex] + Math.Sqrt(abx2 * abx2 + aby2 * aby2) * bestT;
        return bestDist2 < 100 * 100;
    }

    private static Vector3D SegmentTangent(IReadOnlyList<Point3D> pts, int i)
    {
        var a = pts[i];
        var b = pts[(i + 1) % pts.Count];
        var v = new Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        if (v.Length < 1e-9) return new Vector3D(1, 0, 0);
        v.Normalize();
        return v;
    }

    private static List<Point3D> EnsureDense()
    {
        if (_dense != null) return _dense;

        var dense = new List<Point3D>();
        var keys = KeyPoints;
        for (var i = 0; i < keys.Length; i++)
        {
            var p0 = keys[(i - 1 + keys.Length) % keys.Length];
            var p1 = keys[i];
            var p2 = keys[(i + 1) % keys.Length];
            var p3 = keys[(i + 2) % keys.Length];
            var subdiv = Math.Max(16, (int)Math.Ceiling((p2 - p1).Length / 2.0));
            for (var s = 0; s < subdiv; s++)
            {
                dense.Add(CatmullRom(p0, p1, p2, p3, s / (double)subdiv));
            }
        }

        // Moving-average smooth on Z to kill Catmull kinks that jitter the car.
        var smoothed = new Point3D[dense.Count];
        for (var i = 0; i < dense.Count; i++)
        {
            var z = 0.0;
            for (var k = -2; k <= 2; k++)
            {
                var j = (i + k + dense.Count) % dense.Count;
                z += dense[j].Z;
            }

            var p = dense[i];
            smoothed[i] = new Point3D(p.X, p.Y, z / 5.0);
        }

        _dense = smoothed.ToList();
        _minZ = _dense.Min(p => p.Z);
        _maxZ = _dense.Max(p => p.Z);
        _cumulative = new double[_dense.Count + 1];
        _cumulative[0] = 0;
        for (var i = 0; i < _dense.Count; i++)
        {
            var a = _dense[i];
            var b = _dense[(i + 1) % _dense.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var dz = b.Z - a.Z;
            _cumulative[i + 1] = _cumulative[i] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Averaged left normals (XY) per vertex — continuous ribbon without segment seams.
        _leftNormals = new Vector3D[_dense.Count];
        for (var i = 0; i < _dense.Count; i++)
        {
            var prev = _dense[(i - 1 + _dense.Count) % _dense.Count];
            var next = _dense[(i + 1) % _dense.Count];
            var tx = next.X - prev.X;
            var ty = next.Y - prev.Y;
            var len = Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-6)
            {
                _leftNormals[i] = new Vector3D(0, 1, 0);
            }
            else
            {
                _leftNormals[i] = new Vector3D(-ty / len, tx / len, 0);
            }
        }

        // Smooth normals to avoid folding at sharp hairpins.
        var normals = new Vector3D[_dense.Count];
        for (var i = 0; i < _dense.Count; i++)
        {
            var a = _leftNormals[(i - 1 + _dense.Count) % _dense.Count];
            var b = _leftNormals[i];
            var c = _leftNormals[(i + 1) % _dense.Count];
            var n = a + b + b + c;
            if (n.Length < 1e-9) n = b;
            n.Normalize();
            normals[i] = n;
        }

        _leftNormals = normals;
        return _dense;
    }

    private static Point3D CatmullRom(Point3D p0, Point3D p1, Point3D p2, Point3D p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        // Chord-length parameterization prevents short closure segments from
        // inheriting the enormous tangent of an adjacent straight.
        var d01 = (p1 - p0).Length;
        var d12 = (p2 - p1).Length;
        var d23 = (p3 - p2).Length;
        var m1 = ((p1 - p0) / d01 - (p2 - p0) / (d01 + d12) + (p2 - p1) / d12) * d12;
        var m2 = ((p2 - p1) / d12 - (p3 - p1) / (d12 + d23) + (p3 - p2) / d23) * d12;
        return new Point3D(
            (2*t3-3*t2+1)*p1.X + (t3-2*t2+t)*m1.X + (-2*t3+3*t2)*p2.X + (t3-t2)*m2.X,
            (2*t3-3*t2+1)*p1.Y + (t3-2*t2+t)*m1.Y + (-2*t3+3*t2)*p2.Y + (t3-t2)*m2.Y,
            (2*t3-3*t2+1)*p1.Z + (t3-2*t2+t)*m1.Z + (-2*t3+3*t2)*p2.Z + (t3-t2)*m2.Z);
    }
}
