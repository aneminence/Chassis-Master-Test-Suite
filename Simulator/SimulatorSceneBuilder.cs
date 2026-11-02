using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// Helix scene: Spa-inspired elevated closed track + procedural car. World Z-up.
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

    public static (double X, double Y, double Z, double HeadingDeg) GetStartPose()
        => SpaTrackCenterline.GetStartPose();

    public static Model3DGroup BuildEnvironment()
    {
        var root = new Model3DGroup();
        var pts = SpaTrackCenterline.DenseCenterline;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }

        var cx = (minX + maxX) * 0.5;
        var cy = (minY + maxY) * 0.5;
        var spanX = maxX - minX + 400;
        var spanY = maxY - minY + 400;
        var grassZ = minZ - 8;
        var grass = CreateMaterial(Color.FromRgb(38, 72, 44));
        root.Children.Add(CreateBox(spanX, spanY, 0.4, new Point3D(cx, cy, grassZ), grass));

        AddElevatedTrack(root, pts);
        return root;
    }

    private static void AddElevatedTrack(Model3DGroup root, IReadOnlyList<Point3D> pts)
    {
        var asphalt = CreateMaterial(Color.FromRgb(56, 58, 62));
        var line = CreateMaterial(Color.FromRgb(235, 235, 225));
        var kerbA = CreateMaterial(Color.FromRgb(210, 50, 45));
        var kerbB = CreateMaterial(Color.FromRgb(235, 235, 235));
        var barrier = CreateMaterial(Color.FromRgb(150, 155, 160));

        var halfW = SpaTrackCenterline.TrackWidth * 0.5;
        var n = pts.Count;

        for (var i = 0; i < n; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % n];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var horiz = Math.Sqrt(dx * dx + dy * dy);
            if (horiz < 0.05)
            {
                continue;
            }

            var tx = dx / horiz;
            var ty = dy / horiz;
            // Left normal in XY (Y is left when facing +tangent horizontally)
            var nx = -ty;
            var ny = tx;

            var aL = new Point3D(a.X + nx * halfW, a.Y + ny * halfW, a.Z);
            var aR = new Point3D(a.X - nx * halfW, a.Y - ny * halfW, a.Z);
            var bL = new Point3D(b.X + nx * halfW, b.Y + ny * halfW, b.Z);
            var bR = new Point3D(b.X - nx * halfW, b.Y - ny * halfW, b.Z);

            root.Children.Add(CreateQuad(aR, bR, bL, aL, asphalt));

            // Edge lines
            const double lineW = 0.18;
            var aLe = new Point3D(a.X + nx * (halfW - lineW), a.Y + ny * (halfW - lineW), a.Z + 0.03);
            var aLo = new Point3D(a.X + nx * halfW, a.Y + ny * halfW, a.Z + 0.03);
            var bLe = new Point3D(b.X + nx * (halfW - lineW), b.Y + ny * (halfW - lineW), b.Z + 0.03);
            var bLo = new Point3D(b.X + nx * halfW, b.Y + ny * halfW, b.Z + 0.03);
            root.Children.Add(CreateQuad(aLe, bLe, bLo, aLo, line));

            var aRi = new Point3D(a.X - nx * (halfW - lineW), a.Y - ny * (halfW - lineW), a.Z + 0.03);
            var aRo = new Point3D(a.X - nx * halfW, a.Y - ny * halfW, a.Z + 0.03);
            var bRi = new Point3D(b.X - nx * (halfW - lineW), b.Y - ny * (halfW - lineW), b.Z + 0.03);
            var bRo = new Point3D(b.X - nx * halfW, b.Y - ny * halfW, b.Z + 0.03);
            root.Children.Add(CreateQuad(aRo, bRo, bRi, aRi, line));

            // Kerbs outside edges
            var kerbMat = (i / 2) % 2 == 0 ? kerbA : kerbB;
            const double kerbW = 0.55;
            var aLk0 = new Point3D(a.X + nx * halfW, a.Y + ny * halfW, a.Z);
            var aLk1 = new Point3D(a.X + nx * (halfW + kerbW), a.Y + ny * (halfW + kerbW), a.Z + 0.08);
            var bLk0 = new Point3D(b.X + nx * halfW, b.Y + ny * halfW, b.Z);
            var bLk1 = new Point3D(b.X + nx * (halfW + kerbW), b.Y + ny * (halfW + kerbW), b.Z + 0.08);
            root.Children.Add(CreateQuad(aLk0, bLk0, bLk1, aLk1, kerbMat));

            var aRk0 = new Point3D(a.X - nx * halfW, a.Y - ny * halfW, a.Z);
            var aRk1 = new Point3D(a.X - nx * (halfW + kerbW), a.Y - ny * (halfW + kerbW), a.Z + 0.08);
            var bRk0 = new Point3D(b.X - nx * halfW, b.Y - ny * halfW, b.Z);
            var bRk1 = new Point3D(b.X - nx * (halfW + kerbW), b.Y - ny * (halfW + kerbW), b.Z + 0.08);
            root.Children.Add(CreateQuad(aRk1, bRk1, bRk0, aRk0, kerbMat));

            // Barriers
            if (i % 2 == 0)
            {
                var off = halfW + SpaTrackCenterline.BarrierOffset;
                foreach (var side in new[] { 1.0, -1.0 })
                {
                    var p0 = new Point3D(a.X + nx * off * side, a.Y + ny * off * side, a.Z);
                    var p1 = new Point3D(b.X + nx * off * side, b.Y + ny * off * side, b.Z);
                    var p0t = new Point3D(p0.X, p0.Y, p0.Z + 1.1);
                    var p1t = new Point3D(p1.X, p1.Y, p1.Z + 1.1);
                    root.Children.Add(CreateQuad(p0, p1, p1t, p0t, barrier));
                }
            }
        }

        // Center dashes every other segment
        for (var i = 0; i < n; i += 3)
        {
            var a = pts[i];
            var b = pts[(i + 1) % n];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var horiz = Math.Sqrt(dx * dx + dy * dy);
            if (horiz < 0.2) continue;
            var tx = dx / horiz;
            var ty = dy / horiz;
            var nx = -ty;
            var ny = tx;
            const double hw = 0.08;
            var a0 = new Point3D(a.X - nx * hw, a.Y - ny * hw, a.Z + 0.04);
            var a1 = new Point3D(a.X + nx * hw, a.Y + ny * hw, a.Z + 0.04);
            var b0 = new Point3D(b.X - nx * hw, b.Y - ny * hw, b.Z + 0.04);
            var b1 = new Point3D(b.X + nx * hw, b.Y + ny * hw, b.Z + 0.04);
            root.Children.Add(CreateQuad(a0, b0, b1, a1, line));
        }
    }

    public static Model3DGroup BuildVehicle(
        out AxisAngleRotation3D headingRotation,
        out AxisAngleRotation3D pitchRotation,
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

        group.Children.Add(CreateWheelAssembly(FrontAxleX, WheelHalfTrack, WheelRadius, true, out frontLeftSteer, out frontLeftRoll));
        group.Children.Add(CreateWheelAssembly(FrontAxleX, -WheelHalfTrack, WheelRadius, true, out frontRightSteer, out frontRightRoll));
        group.Children.Add(CreateWheelAssembly(RearAxleX, WheelHalfTrack, WheelRadius, false, out _, out rearLeftRoll));
        group.Children.Add(CreateWheelAssembly(RearAxleX, -WheelHalfTrack, WheelRadius, false, out _, out rearRightRoll));

        // Pitch about +Y (left), then yaw about +Z (up), then translate.
        pitchRotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        headingRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        translation = new TranslateTransform3D(0, 0, 0);

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(pitchRotation));
        transform.Children.Add(new RotateTransform3D(headingRotation));
        transform.Children.Add(translation);
        group.Transform = transform;
        return group;
    }

    private static Model3DGroup CreateWheelAssembly(
        double x, double y, double z, bool isFront,
        out AxisAngleRotation3D steerRotation,
        out AxisAngleRotation3D rollRotation)
    {
        var wheelGroup = new Model3DGroup();
        var tire = CreateTireMaterial();
        var rim = CreateRimMaterial();
        var hub = CreateHubMaterial();
        wheelGroup.Children.Add(CreateCylinder(WheelRadius, WheelWidth, tire, new Point3D(x, y, z), new Vector3D(0, 1, 0)));
        wheelGroup.Children.Add(CreateCylinder(0.24, WheelWidth + 0.02, rim, new Point3D(x, y, z), new Vector3D(0, 1, 0)));
        wheelGroup.Children.Add(CreateCylinder(0.09, WheelWidth + 0.04, hub, new Point3D(x, y, z), new Vector3D(0, 1, 0)));

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

    private static GeometryModel3D CreateQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Material material)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);
        // backface
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D CreateBox(double sizeX, double sizeY, double sizeZ, Point3D center, Material material)
    {
        var hx = sizeX * 0.5; var hy = sizeY * 0.5; var hz = sizeZ * 0.5;
        var p0 = new Point3D(center.X - hx, center.Y - hy, center.Z - hz);
        var p1 = new Point3D(center.X + hx, center.Y - hy, center.Z - hz);
        var p2 = new Point3D(center.X + hx, center.Y + hy, center.Z - hz);
        var p3 = new Point3D(center.X - hx, center.Y + hy, center.Z - hz);
        var p4 = new Point3D(center.X - hx, center.Y - hy, center.Z + hz);
        var p5 = new Point3D(center.X + hx, center.Y - hy, center.Z + hz);
        var p6 = new Point3D(center.X + hx, center.Y + hy, center.Z + hz);
        var p7 = new Point3D(center.X - hx, center.Y + hy, center.Z + hz);
        var group = new Model3DGroup();
        // Use quads via CreateQuad merged - simpler single mesh:
        var mesh = new MeshGeometry3D();
        void AddQuad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var i = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
        }
        AddQuad(p0, p1, p5, p4);
        AddQuad(p1, p2, p6, p5);
        AddQuad(p2, p3, p7, p6);
        AddQuad(p3, p0, p4, p7);
        AddQuad(p4, p5, p6, p7);
        AddQuad(p3, p2, p1, p0);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D CreateCylinder(double radius, double height, Material material, Point3D center, Vector3D axis)
    {
        axis.Normalize();
        var up = Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        var u = Vector3D.CrossProduct(axis, up); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u); v.Normalize();
        var mesh = new MeshGeometry3D();
        const int segments = 18;
        var p0 = center - axis * (height * 0.5);
        var p1 = center + axis * (height * 0.5);
        for (var i = 0; i < segments; i++)
        {
            var a0 = 2 * Math.PI * i / segments;
            var a1 = 2 * Math.PI * (i + 1) / segments;
            var r0 = u * (radius * Math.Cos(a0)) + v * (radius * Math.Sin(a0));
            var r1 = u * (radius * Math.Cos(a1)) + v * (radius * Math.Sin(a1));
            var idx = mesh.Positions.Count;
            mesh.Positions.Add(p0 + r0); mesh.Positions.Add(p0 + r1);
            mesh.Positions.Add(p1 + r1); mesh.Positions.Add(p1 + r0);
            mesh.TriangleIndices.Add(idx); mesh.TriangleIndices.Add(idx + 1); mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx); mesh.TriangleIndices.Add(idx + 2); mesh.TriangleIndices.Add(idx + 3);
        }
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static Material CreateMaterial(Color color) => new DiffuseMaterial(new SolidColorBrush(color));
    private static Material CreateGlassMaterial() => new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(160, 160, 200, 230)));
    private static Material CreateTireMaterial() => CreateMaterial(Color.FromRgb(28, 28, 30));
    private static Material CreateRimMaterial() => CreateMaterial(Color.FromRgb(180, 180, 185));
    private static Material CreateHubMaterial() => CreateMaterial(Color.FromRgb(90, 90, 95));
}
