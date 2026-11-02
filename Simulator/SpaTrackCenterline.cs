using System.Windows.Media.Media3D;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// Spa-Francorchamps inspired closed track centerline (original approximate layout, not a licensed map).
/// Roughly multi-km lap feel; Eau Rouge/Raidillon climb + long Kemmel straight with real Z elevation.
/// World: X/Y horizontal, Z up.
/// </summary>
public static class SpaTrackCenterline
{
    public const double TrackWidth = 12.0;
    public const double BarrierOffset = 1.8;

    private static readonly Point3D[] KeyPoints =
    {
        new(0, 0, 42),
        new(180, -20, 42),
        new(320, -80, 41),
        new(360, -160, 40),
        new(300, -240, 39),
        new(220, -260, 38),
        new(280, -200, 37),
        new(420, -140, 36),
        new(560, -100, 35),
        new(700, -60, 34),
        new(820, -10, 48),
        new(940, 80, 68),
        new(1040, 160, 82),
        new(1200, 220, 88),
        new(1450, 280, 92),
        new(1750, 330, 94),
        new(2050, 360, 95),
        new(2300, 340, 96),
        new(2480, 280, 98),
        new(2550, 180, 97),
        new(2520, 80, 94),
        new(2420, 20, 90),
        new(2350, -120, 78),
        new(2280, -280, 65),
        new(2180, -420, 55),
        new(2000, -520, 52),
        new(1780, -580, 50),
        new(1550, -640, 48),
        new(1320, -700, 47),
        new(1100, -720, 46),
        new(880, -680, 45),
        new(700, -600, 44),
        new(520, -480, 43),
        new(380, -360, 42),
        new(280, -260, 42),
        new(220, -180, 42),
        new(160, -100, 42),
        new(80, -40, 42),
    };

    private static List<Point3D>? _dense;
    private static double[]? _cumulative;

    public static IReadOnlyList<Point3D> DenseCenterline => EnsureDense();

    public static (double X, double Y, double Z, double HeadingDeg) GetStartPose()
    {
        var pts = EnsureDense();
        var a = pts[0];
        var b = pts[Math.Min(8, pts.Count - 1)];
        var heading = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
        return (a.X, a.Y, a.Z, heading);
    }

    public static double ApproximateLapLengthMeters()
    {
        EnsureDense();
        return _cumulative![^1];
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
        var bestZ = pts[0].Z;
        var bestT = new Vector3D(1, 0, 0);
        var bestS = 0.0;

        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var abz = b.Z - a.Z;
            var abLen2 = abx * abx + aby * aby;
            if (abLen2 < 1e-6)
            {
                continue;
            }

            var t = Math.Clamp(((x - a.X) * abx + (y - a.Y) * aby) / abLen2, 0.0, 1.0);
            var px = a.X + abx * t;
            var py = a.Y + aby * t;
            var dx = x - px;
            var dy = y - py;
            var d2 = dx * dx + dy * dy;
            if (d2 >= bestDist2)
            {
                continue;
            }

            bestDist2 = d2;
            bestZ = a.Z + abz * t;
            var len = Math.Sqrt(abx * abx + aby * aby + abz * abz);
            bestT = len > 1e-6
                ? new Vector3D(abx / len, aby / len, abz / len)
                : new Vector3D(1, 0, 0);
            bestS = _cumulative![i] + Math.Sqrt(abLen2) * t;
        }

        z = bestZ;
        tangent = bestT;
        distanceAlong = bestS;
        return bestDist2 < 80 * 80;
    }

    private static List<Point3D> EnsureDense()
    {
        if (_dense != null)
        {
            return _dense;
        }

        var dense = new List<Point3D>();
        var keys = KeyPoints;
        const int subdiv = 14;
        for (var i = 0; i < keys.Length; i++)
        {
            var p0 = keys[(i - 1 + keys.Length) % keys.Length];
            var p1 = keys[i];
            var p2 = keys[(i + 1) % keys.Length];
            var p3 = keys[(i + 2) % keys.Length];
            for (var s = 0; s < subdiv; s++)
            {
                dense.Add(CatmullRom(p0, p1, p2, p3, s / (double)subdiv));
            }
        }

        _dense = dense;
        _cumulative = new double[dense.Count + 1];
        _cumulative[0] = 0;
        for (var i = 0; i < dense.Count; i++)
        {
            var a = dense[i];
            var b = dense[(i + 1) % dense.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var dz = b.Z - a.Z;
            _cumulative[i + 1] = _cumulative[i] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        return _dense;
    }

    private static Point3D CatmullRom(Point3D p0, Point3D p1, Point3D p2, Point3D p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        double F(double a, double b, double c, double d) =>
            0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
        return new Point3D(F(p0.X, p1.X, p2.X, p3.X), F(p0.Y, p1.Y, p2.Y, p3.Y), F(p0.Z, p1.Z, p2.Z, p3.Z));
    }
}
