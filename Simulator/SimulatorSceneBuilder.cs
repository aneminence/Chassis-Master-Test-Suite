using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// Helix 场景：封闭跑道 + 简易车模。世界系 Z-up，与车体 X 前 / Y 左 / Z 上一致。
/// </summary>
public static class SimulatorSceneBuilder
{
    public const double VehicleLength = 4.60;
    public const double VehicleWidth = 1.90;
    public const double WheelRadius = 0.39;
    public const double WheelWidth = 0.28;
    public const double WheelHalfTrack = 0.86;
    public const double FrontAxleX = 1.35;
    public const double RearAxleX = -1.35;

    /// <summary>跑道半直道长度（沿 X）。</summary>
    public const double TrackHalfStraight = 45.0;

    /// <summary>跑道弯心半径（中心线）。</summary>
    public const double TrackCenterRadius = 28.0;

    public const double TrackWidth = 8.0;

    public static (double X, double Y, double HeadingDeg) GetStartPose()
    {
        // 底直道中点附近，朝 +X。
        return (0.0, -TrackCenterRadius, 0.0);
    }

    public static Model3DGroup BuildEnvironment()
    {
        var root = new Model3DGroup();

        var grass = CreateMaterial(Color.FromRgb(42, 78, 48));
        root.Children.Add(CreateBox(220, 180, 0.08, new Point3D(0, 0, -0.04), grass));

        AddStadiumTrack(root);
        AddCones(root);

        return root;
    }

    public static Model3DGroup BuildVehicle(
        out AxisAngleRotation3D headingRotation,
        out TranslateTransform3D translation,
        out AxisAngleRotation3D frontLeftSteer,
        out AxisAngleRotation3D frontRightSteer,
        out AxisAngleRotation3D frontLeftRoll,
        out AxisAngleRotation3D frontRightRoll,
        out AxisAngleRotation3D rearLeftRoll,
        out AxisAngleRotation3D rearRightRoll)
    {
        var group = new Model3DGroup();

        var body = CreateMaterial(Color.FromRgb(35, 100, 190));
        var dark = CreateMaterial(Color.FromRgb(22, 24, 28));
        var glass = CreateGlassMaterial();
        var light = CreateMaterial(Color.FromRgb(230, 240, 255));
        var rearLight = CreateMaterial(Color.FromRgb(180, 30, 30));

        // 车体尺寸：X 长、Y 宽、Z 高
        group.Children.Add(CreateBox(VehicleLength, VehicleWidth, 0.48, new Point3D(0, 0, 0.65), body));
        group.Children.Add(CreateBox(2.55, 1.72, 0.48, new Point3D(-0.10, 0, 1.08), body));
        group.Children.Add(CreateBox(1.65, 1.60, 0.10, new Point3D(-0.10, 0, 1.40), dark));
        group.Children.Add(CreateBox(1.05, 1.70, 0.18, new Point3D(1.40, 0, 1.10), body));
        group.Children.Add(CreateBox(0.70, 1.52, 0.06, new Point3D(0.75, 0, 1.32), glass));
        group.Children.Add(CreateBox(0.55, 1.50, 0.06, new Point3D(-1.20, 0, 1.28), glass));
        group.Children.Add(CreateBox(0.08, 0.55, 0.18, new Point3D(2.28, 0.55, 0.78), light));
        group.Children.Add(CreateBox(0.08, 0.55, 0.18, new Point3D(2.28, -0.55, 0.78), light));
        group.Children.Add(CreateBox(0.06, 0.42, 0.16, new Point3D(-2.34, 0.58, 0.86), rearLight));
        group.Children.Add(CreateBox(0.06, 0.42, 0.16, new Point3D(-2.34, -0.58, 0.86), rearLight));
        group.Children.Add(CreateBox(0.28, 0.12, 0.14, new Point3D(0.65, 0.98, 1.15), dark));
        group.Children.Add(CreateBox(0.28, 0.12, 0.14, new Point3D(0.65, -0.98, 1.15), dark));

        // 车轮：Y+ = 左，Y- = 右
        group.Children.Add(CreateWheelAssembly(
            FrontAxleX, WheelHalfTrack, WheelRadius, true,
            out frontLeftSteer, out frontLeftRoll));
        group.Children.Add(CreateWheelAssembly(
            FrontAxleX, -WheelHalfTrack, WheelRadius, true,
            out frontRightSteer, out frontRightRoll));
        group.Children.Add(CreateWheelAssembly(
            RearAxleX, WheelHalfTrack, WheelRadius, false,
            out _, out rearLeftRoll));
        group.Children.Add(CreateWheelAssembly(
            RearAxleX, -WheelHalfTrack, WheelRadius, false,
            out _, out rearRightRoll));

        headingRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        translation = new TranslateTransform3D(0, 0, 0);

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(headingRotation));
        transform.Children.Add(translation);
        group.Transform = transform;

        return group;
    }

    private static Model3DGroup CreateWheelAssembly(
        double x,
        double y,
        double z,
        bool isFront,
        out AxisAngleRotation3D steerRotation,
        out AxisAngleRotation3D rollRotation)
    {
        var wheelGroup = new Model3DGroup();
        var tire = CreateTireMaterial();
        var rim = CreateRimMaterial();
        var hub = CreateHubMaterial();
        var spoke = CreateSpokeMaterial();

        // 轮轴沿车体 Y（左）
        wheelGroup.Children.Add(CreateCylinder(WheelRadius, WheelWidth, tire, new Point3D(x, y, z), new Vector3D(0, 1, 0)));
        wheelGroup.Children.Add(CreateCylinder(0.24, WheelWidth + 0.02, rim, new Point3D(x, y, z), new Vector3D(0, 1, 0)));
        wheelGroup.Children.Add(CreateCylinder(0.09, WheelWidth + 0.04, hub, new Point3D(x, y, z), new Vector3D(0, 1, 0)));

        for (var i = 0; i < 6; i++)
        {
            var angle = i * Math.PI / 3.0;
            var spokeBox = CreateBox(0.34, 0.06, 0.06, new Point3D(x, y, z), spoke);
            spokeBox.Transform = new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 1, 0), angle * 180.0 / Math.PI),
                x, y, z);
            wheelGroup.Children.Add(spokeBox);
        }

        // 滚动绕 +Y：前进时轮顶朝 +X，需用负角（与旧版绕 +X 的符号处理对应）
        rollRotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var rollTransform = new RotateTransform3D(rollRotation, x, y, z);

        if (isFront)
        {
            steerRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            var steerTransform = new RotateTransform3D(steerRotation, x, y, z);
            var tg = new Transform3DGroup();
            tg.Children.Add(steerTransform);
            tg.Children.Add(rollTransform);
            wheelGroup.Transform = tg;
        }
        else
        {
            steerRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            wheelGroup.Transform = rollTransform;
        }

        return wheelGroup;
    }

    private static void AddStadiumTrack(Model3DGroup root)
    {
        var asphalt = CreateMaterial(Color.FromRgb(58, 61, 65));
        var line = CreateMaterial(Color.FromRgb(235, 235, 225));
        var kerbLight = CreateMaterial(Color.FromRgb(230, 230, 230));
        var kerbRed = CreateMaterial(Color.FromRgb(200, 45, 40));

        var halfW = TrackWidth * 0.5;
        var samples = SampleCenterline(180);

        for (var i = 0; i < samples.Count; i++)
        {
            var a = samples[i];
            var b = samples[(i + 1) % samples.Count];
            var mid = new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0.02);
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
            {
                continue;
            }

            var yawDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            var segment = CreateBox(len + 0.05, TrackWidth, 0.05, mid, asphalt);
            segment.Transform = new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 0, 1), yawDeg),
                mid);
            root.Children.Add(segment);

            // 边缘白线
            var nx = -dy / len;
            var ny = dx / len;
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var edgeMid = new Point3D(
                    mid.X + nx * halfW * side,
                    mid.Y + ny * halfW * side,
                    0.05);
                var edge = CreateBox(len + 0.05, 0.14, 0.04, edgeMid, line);
                edge.Transform = new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), yawDeg),
                    edgeMid);
                root.Children.Add(edge);
            }

            // 红白路缘块（弯道更密时仍按段交替）
            if (i % 2 == 0)
            {
                var kerbMat = (i / 2) % 2 == 0 ? kerbRed : kerbLight;
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    var kerbMid = new Point3D(
                        mid.X + nx * (halfW + 0.35) * side,
                        mid.Y + ny * (halfW + 0.35) * side,
                        0.08);
                    var kerb = CreateBox(Math.Min(len, 1.8), 0.45, 0.12, kerbMid, kerbMat);
                    kerb.Transform = new RotateTransform3D(
                        new AxisAngleRotation3D(new Vector3D(0, 0, 1), yawDeg),
                        kerbMid);
                    root.Children.Add(kerb);
                }
            }
        }

        // 中心虚线
        for (var i = 0; i < samples.Count; i += 2)
        {
            var a = samples[i];
            var b = samples[(i + 1) % samples.Count];
            var mid = new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, 0.06);
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.2)
            {
                continue;
            }

            var yawDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            var dash = CreateBox(Math.Min(len, 2.5), 0.12, 0.03, mid, line);
            dash.Transform = new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 0, 1), yawDeg),
                mid);
            root.Children.Add(dash);
        }
    }

    private static void AddCones(Model3DGroup root)
    {
        var orange = CreateMaterial(Color.FromRgb(230, 120, 30));
        var white = CreateMaterial(Color.FromRgb(240, 240, 240));
        var samples = SampleCenterline(48);
        var halfW = TrackWidth * 0.5;

        for (var i = 0; i < samples.Count; i++)
        {
            var p = samples[i];
            var next = samples[(i + 1) % samples.Count];
            var dx = next.X - p.X;
            var dy = next.Y - p.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
            {
                continue;
            }

            var nx = -dy / len;
            var ny = dx / len;
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var cx = p.X + nx * (halfW + 0.9) * side;
                var cy = p.Y + ny * (halfW + 0.9) * side;
                root.Children.Add(CreateBox(0.28, 0.28, 0.55, new Point3D(cx, cy, 0.28), orange));
                root.Children.Add(CreateBox(0.22, 0.22, 0.12, new Point3D(cx, cy, 0.58), white));
            }
        }
    }

    private static List<Point3D> SampleCenterline(int arcSegmentsPerHalf)
    {
        var points = new List<Point3D>();
        var hs = TrackHalfStraight;
        var r = TrackCenterRadius;

        // 底直道：-hs → +hs，y = -r
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0;
            points.Add(new Point3D(-hs + 2 * hs * t, -r, 0));
        }

        // 右半圆：中心 (hs, 0)，从 -90° 到 +90°
        for (var i = 1; i <= arcSegmentsPerHalf; i++)
        {
            var ang = -Math.PI / 2 + Math.PI * (i / (double)arcSegmentsPerHalf);
            points.Add(new Point3D(hs + r * Math.Cos(ang), r * Math.Sin(ang), 0));
        }

        // 顶直道：+hs → -hs，y = +r
        for (var i = 1; i <= 20; i++)
        {
            var t = i / 20.0;
            points.Add(new Point3D(hs - 2 * hs * t, r, 0));
        }

        // 左半圆：中心 (-hs, 0)，从 +90° 到 +270°
        for (var i = 1; i < arcSegmentsPerHalf; i++)
        {
            var ang = Math.PI / 2 + Math.PI * (i / (double)arcSegmentsPerHalf);
            points.Add(new Point3D(-hs + r * Math.Cos(ang), r * Math.Sin(ang), 0));
        }

        return points;
    }

    private static GeometryModel3D CreateBox(
        double sizeX,
        double sizeY,
        double sizeZ,
        Point3D center,
        Material material)
    {
        var mesh = new MeshGeometry3D();
        var hx = sizeX * 0.5;
        var hy = sizeY * 0.5;
        var hz = sizeZ * 0.5;

        var p0 = new Point3D(center.X - hx, center.Y - hy, center.Z - hz);
        var p1 = new Point3D(center.X + hx, center.Y - hy, center.Z - hz);
        var p2 = new Point3D(center.X + hx, center.Y + hy, center.Z - hz);
        var p3 = new Point3D(center.X - hx, center.Y + hy, center.Z - hz);
        var p4 = new Point3D(center.X - hx, center.Y - hy, center.Z + hz);
        var p5 = new Point3D(center.X + hx, center.Y - hy, center.Z + hz);
        var p6 = new Point3D(center.X + hx, center.Y + hy, center.Z + hz);
        var p7 = new Point3D(center.X - hx, center.Y + hy, center.Z + hz);

        void AddQuad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }

        AddQuad(p0, p1, p5, p4); // -Y
        AddQuad(p1, p2, p6, p5); // +X
        AddQuad(p2, p3, p7, p6); // +Y
        AddQuad(p3, p0, p4, p7); // -X
        AddQuad(p4, p5, p6, p7); // +Z
        AddQuad(p3, p2, p1, p0); // -Z

        return new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
    }

    private static GeometryModel3D CreateCylinder(
        double radius,
        double height,
        Material material,
        Point3D center,
        Vector3D axis)
    {
        axis.Normalize();
        // 造一组与 axis 正交的基向量
        var up = Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        var u = Vector3D.CrossProduct(axis, up);
        u.Normalize();
        var v = Vector3D.CrossProduct(axis, u);
        v.Normalize();

        var mesh = new MeshGeometry3D();
        const int segments = 20;
        var p0 = center - axis * (height * 0.5);
        var p1 = center + axis * (height * 0.5);

        for (var i = 0; i < segments; i++)
        {
            var a0 = 2 * Math.PI * i / segments;
            var a1 = 2 * Math.PI * (i + 1) / segments;
            var r0 = u * (radius * Math.Cos(a0)) + v * (radius * Math.Sin(a0));
            var r1 = u * (radius * Math.Cos(a1)) + v * (radius * Math.Sin(a1));

            var b0 = p0 + r0;
            var b1 = p0 + r1;
            var t0 = p1 + r0;
            var t1 = p1 + r1;

            var idx = mesh.Positions.Count;
            mesh.Positions.Add(b0);
            mesh.Positions.Add(b1);
            mesh.Positions.Add(t1);
            mesh.Positions.Add(t0);
            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 1);
            mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx + 3);
        }

        return new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
    }

    private static Material CreateMaterial(Color color)
    {
        return new DiffuseMaterial(new SolidColorBrush(color));
    }

    private static Material CreateGlassMaterial()
    {
        return new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(160, 160, 200, 230)));
    }

    private static Material CreateTireMaterial() => CreateMaterial(Color.FromRgb(28, 28, 30));
    private static Material CreateRimMaterial() => CreateMaterial(Color.FromRgb(180, 180, 185));
    private static Material CreateHubMaterial() => CreateMaterial(Color.FromRgb(90, 90, 95));
    private static Material CreateSpokeMaterial() => CreateMaterial(Color.FromRgb(150, 150, 155));
}

