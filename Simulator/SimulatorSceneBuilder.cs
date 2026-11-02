using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// Helix scene: Spa-inspired elevated track on matching terrain + procedural car. World Z-up.
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
        root.Children.Add(BuildTerrainMesh());
        AddContinuousTrack(root);
        return root;
    }

    private static GeometryModel3D BuildTerrainMesh()
    {
        var bounds = SpaTrackCenterline.GetBounds();
        var pad = 280.0;
        var minX = bounds.MinX - pad;
        var maxX = bounds.MaxX + pad;
        var minY = bounds.MinY - pad;
        var maxY = bounds.MaxY + pad;

        const int nx = 96;
        const int ny = 72;
        var dx = (maxX - minX) / nx;
        var dy = (maxY - minY) / ny;

        var mesh = new MeshGeometry3D();
        var grass = CreateMaterial(Color.FromRgb(42, 78, 48));

        // Vertices
        for (var j = 0; j <= ny; j++)
        {
            var y = minY + j * dy;
            for (var i = 0; i <= nx; i++)
            {
                var x = minX + i * dx;
                // Background terrain stays below the fitted track corridor.
                var z = bounds.MinZ - 8.0;
                mesh.Positions.Add(new Point3D(x, y, z));
            }
        }

        // Quads as triangles
        var stride = nx + 1;
        for (var j = 0; j < ny; j++)
        {
            for (var i = 0; i < nx; i++)
            {
                var i0 = j * stride + i;
                var i1 = i0 + 1;
                var i2 = i0 + stride;
                var i3 = i2 + 1;
                mesh.TriangleIndices.Add(i0);
                mesh.TriangleIndices.Add(i1);
                mesh.TriangleIndices.Add(i3);
                mesh.TriangleIndices.Add(i0);
                mesh.TriangleIndices.Add(i3);
                mesh.TriangleIndices.Add(i2);
            }
        }

        return new GeometryModel3D(mesh, grass) { BackMaterial = grass };
    }

    private static void AddContinuousTrack(Model3DGroup root)
    {
        var pts = SpaTrackCenterline.DenseCenterline;
        var lefts = SpaTrackCenterline.LeftNormals;
        var n = pts.Count;
        var halfW = SpaTrackCenterline.TrackWidth * 0.5;
        var lift = SpaTrackCenterline.AsphaltLift;

        var asphaltMat = CreateMaterial(Color.FromRgb(56, 58, 62));
        var lineMat = CreateMaterial(Color.FromRgb(235, 235, 225));
        var kerbA = CreateMaterial(Color.FromRgb(210, 50, 45));
        var kerbB = CreateMaterial(Color.FromRgb(235, 235, 235));
        var barrierMat = CreateMaterial(Color.FromRgb(150, 155, 160));

        var asphalt = new MeshGeometry3D();
        var edgeL = new MeshGeometry3D();
        var edgeR = new MeshGeometry3D();
        var barrierMesh = new MeshGeometry3D();
        var valleyZ = SpaTrackCenterline.GetBounds().MinZ - 8.0;

        // Fit the verge to exactly the same cross sections as the road. A
        // coarse world grid cannot represent a twelve-metre-wide hillside road.
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var corridor = new MeshGeometry3D();
            var offsets = new[] { halfW, halfW + 5.0, halfW + 18.0, halfW + 45.0 };
            for (var band = 0; band < offsets.Length - 1; band++)
            {
                var inner = new Point3D[n];
                var outer = new Point3D[n];
                for (var i = 0; i < n; i++)
                {
                    Point3D At(double offset)
                    {
                        var blend = Math.Clamp((offset - halfW - 5.0) / 40.0, 0, 1);
                        blend = blend * blend * (3 - 2 * blend);
                        var z = pts[i].Z * (1 - blend) + valleyZ * blend;
                        return new Point3D(pts[i].X + lefts[i].X * offset * side,
                            pts[i].Y + lefts[i].Y * offset * side, z);
                    }
                    inner[i] = At(offsets[band]);
                    outer[i] = At(offsets[band + 1]);
                }
                AppendRibbon(corridor, side > 0 ? inner : outer, side > 0 ? outer : inner);
            }
            var grass = CreateMaterial(Color.FromRgb(42, 78, 48));
            root.Children.Add(new GeometryModel3D(corridor, grass) { BackMaterial = grass });
        }

        // Continuous ribbon vertices: for each centerline point, left/right at asphalt height.
        var leftEdge = new Point3D[n];
        var rightEdge = new Point3D[n];
        for (var i = 0; i < n; i++)
        {
            var p = pts[i];
            var ln = lefts[i];
            leftEdge[i] = new Point3D(p.X + ln.X * halfW, p.Y + ln.Y * halfW, p.Z + lift);
            rightEdge[i] = new Point3D(p.X - ln.X * halfW, p.Y - ln.Y * halfW, p.Z + lift);
        }

        AppendRibbon(asphalt, rightEdge, leftEdge);

        // Edge lines: inset, slight extra lift to avoid z-fight with asphalt (unique strip, shared verts).
        const double lineInset = 0.22;
        var lineLift = lift + 0.025;
        var leftInner = new Point3D[n];
        var leftOuter = new Point3D[n];
        var rightInner = new Point3D[n];
        var rightOuter = new Point3D[n];
        for (var i = 0; i < n; i++)
        {
            var p = pts[i];
            var ln = lefts[i];
            leftOuter[i] = new Point3D(p.X + ln.X * halfW, p.Y + ln.Y * halfW, p.Z + lineLift);
            leftInner[i] = new Point3D(p.X + ln.X * (halfW - lineInset), p.Y + ln.Y * (halfW - lineInset), p.Z + lineLift);
            rightOuter[i] = new Point3D(p.X - ln.X * halfW, p.Y - ln.Y * halfW, p.Z + lineLift);
            rightInner[i] = new Point3D(p.X - ln.X * (halfW - lineInset), p.Y - ln.Y * (halfW - lineInset), p.Z + lineLift);
        }

        AppendRibbon(edgeL, leftInner, leftOuter);
        AppendRibbon(edgeR, rightOuter, rightInner);

        // Kerbs: outward strip, alternating materials via two meshes every other segment pair.
        var kerbAMesh = new MeshGeometry3D();
        var kerbBMesh = new MeshGeometry3D();
        const double kerbW = 0.6;
        for (var i = 0; i < n; i++)
        {
            var i1 = (i + 1) % n;
            var useA = (i / 3) % 2 == 0;
            var mesh = useA ? kerbAMesh : kerbBMesh;
            AddKerbSegment(mesh, pts[i], pts[i1], lefts[i], lefts[i1], halfW, kerbW, lift);
        }

        // Barriers as short posts beside the ribbon only (no spanning walls that can chord across bends).
        var barrierOff = halfW + SpaTrackCenterline.BarrierOffset;
        for (var i = 0; i < n; i += 3)
        {
            var p = pts[i];
            var ln = lefts[i];
            foreach (var side in new[] { 1.0, -1.0 })
            {
                var cx = p.X + ln.X * barrierOff * side;
                var cy = p.Y + ln.Y * barrierOff * side;
                var cz = p.Z + lift;
                // Small vertical box post ~0.25m square, 1.1m tall — never crosses driving line.
                AppendAxisAlignedPost(barrierMesh, cx, cy, cz, 0.22, 1.1);
            }
        }

        root.Children.Add(new GeometryModel3D(asphalt, asphaltMat) { BackMaterial = asphaltMat });
        root.Children.Add(new GeometryModel3D(edgeL, lineMat) { BackMaterial = lineMat });
        root.Children.Add(new GeometryModel3D(edgeR, lineMat) { BackMaterial = lineMat });
        root.Children.Add(new GeometryModel3D(kerbAMesh, kerbA) { BackMaterial = kerbA });
        root.Children.Add(new GeometryModel3D(kerbBMesh, kerbB) { BackMaterial = kerbB });
        root.Children.Add(new GeometryModel3D(barrierMesh, barrierMat) { BackMaterial = barrierMat });
    }


    private static void AddKerbSegment(
        MeshGeometry3D mesh,
        Point3D a,
        Point3D b,
        Vector3D na,
        Vector3D nb,
        double halfW,
        double kerbW,
        double lift)
    {
        foreach (var side in new[] { 1.0, -1.0 })
        {
            var a0 = new Point3D(a.X + na.X * halfW * side, a.Y + na.Y * halfW * side, a.Z + lift);
            var a1 = new Point3D(a.X + na.X * (halfW + kerbW) * side, a.Y + na.Y * (halfW + kerbW) * side, a.Z + lift + 0.07);
            var b0 = new Point3D(b.X + nb.X * halfW * side, b.Y + nb.Y * halfW * side, b.Z + lift);
            var b1 = new Point3D(b.X + nb.X * (halfW + kerbW) * side, b.Y + nb.Y * (halfW + kerbW) * side, b.Z + lift + 0.07);
            if (side > 0)
            {
                AppendQuad(mesh, a0, b0, b1, a1);
            }
            else
            {
                AppendQuad(mesh, a1, b1, b0, a0);
            }
        }
    }


    private static void AppendAxisAlignedPost(MeshGeometry3D mesh, double x, double y, double zBottom, double size, double height)
    {
        var h = size * 0.5;
        var z0 = zBottom;
        var z1 = zBottom + height;
        var p0 = new Point3D(x - h, y - h, z0);
        var p1 = new Point3D(x + h, y - h, z0);
        var p2 = new Point3D(x + h, y + h, z0);
        var p3 = new Point3D(x - h, y + h, z0);
        var p4 = new Point3D(x - h, y - h, z1);
        var p5 = new Point3D(x + h, y - h, z1);
        var p6 = new Point3D(x + h, y + h, z1);
        var p7 = new Point3D(x - h, y + h, z1);
        AppendQuad(mesh, p0, p1, p5, p4);
        AppendQuad(mesh, p1, p2, p6, p5);
        AppendQuad(mesh, p2, p3, p7, p6);
        AppendQuad(mesh, p3, p0, p4, p7);
        AppendQuad(mesh, p4, p5, p6, p7);
    }

    private static void AppendRibbon(MeshGeometry3D mesh, Point3D[] sideA, Point3D[] sideB)
    {
        var n = sideA.Length;
        var baseIndex = mesh.Positions.Count;
        for (var i = 0; i < n; i++)
        {
            mesh.Positions.Add(sideA[i]);
            mesh.Positions.Add(sideB[i]);
        }

        for (var i = 0; i < n; i++)
        {
            var i0 = baseIndex + i * 2;
            var i1 = i0 + 1;
            var j = (i + 1) % n;
            var j0 = baseIndex + j * 2;
            var j1 = j0 + 1;
            mesh.TriangleIndices.Add(i0);
            mesh.TriangleIndices.Add(j0);
            mesh.TriangleIndices.Add(j1);
            mesh.TriangleIndices.Add(i0);
            mesh.TriangleIndices.Add(j1);
            mesh.TriangleIndices.Add(i1);
        }
    }

    private static void AppendQuad(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i + 3);
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

        group.Children.Add(CreateLoft(new[] {
            (-2.30, .78, .44, .79), (-1.95, .94, .38, .98),
            (-1.25, .95, .38, 1.02), (.75, .94, .38, 1.00),
            (1.65, .90, .42, .89), (2.30, .76, .49, .71) }, body));
        group.Children.Add(CreateLoft(new[] {
            (-1.48, .80, .97, 1.01), (-.78, .72, 1.00, 1.45),
            (.35, .71, 1.00, 1.44), (1.03, .80, .96, 1.01) }, glass));
        group.Children.Add(CreateBox(1.15, 1.42, .05, new Point3D(-.22, 0, 1.46), body));
        group.Children.Add(CreateBox(.08, 1.10, .22, new Point3D(2.30, 0, .57), dark));
        group.Children.Add(CreateBox(.15, 1.64, .08, new Point3D(2.25, 0, .43), dark));
        foreach (var side in new[] { -1.0, 1.0 })
        {
            group.Children.Add(CreateBox(3.0, .08, .12, new Point3D(-.1, side * .94, .42), dark));
            group.Children.Add(CreateBox(.23, .25, .13, new Point3D(.65, side * .98, 1.05), body));
            group.Children.Add(CreateBox(.07, .05, .40, new Point3D(-.25, side * .74, 1.22), dark));
            group.Children.Add(CreateBox(.18, .04, .04, new Point3D(-.48, side * .95, .94), light));
        }
        group.Children.Add(CreateBox(0.08, 0.55, 0.18, new Point3D(2.28, 0.55, 0.78), light));
        group.Children.Add(CreateBox(0.08, 0.55, 0.18, new Point3D(2.28, -0.55, 0.78), light));
        group.Children.Add(CreateBox(0.06, 0.42, 0.16, new Point3D(-2.34, 0.58, 0.86), rearLight));
        group.Children.Add(CreateBox(0.06, 0.42, 0.16, new Point3D(-2.34, -0.58, 0.86), rearLight));

        group.Children.Add(CreateWheelAssembly(FrontAxleX, WheelHalfTrack, WheelRadius, true, out frontLeftSteer, out frontLeftRoll));
        group.Children.Add(CreateWheelAssembly(FrontAxleX, -WheelHalfTrack, WheelRadius, true, out frontRightSteer, out frontRightRoll));
        group.Children.Add(CreateWheelAssembly(RearAxleX, WheelHalfTrack, WheelRadius, false, out _, out rearLeftRoll));
        group.Children.Add(CreateWheelAssembly(RearAxleX, -WheelHalfTrack, WheelRadius, false, out _, out rearRightRoll));

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
            tg.Children.Add(rollTransform);
            tg.Children.Add(steerTransform);
            wheelGroup.Transform = tg;
        }
        else
        {
            steerRotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            wheelGroup.Transform = rollTransform;
        }

        return wheelGroup;
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
        const int segments = 48;
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
            var cap = mesh.Positions.Count;
            mesh.Positions.Add(p0); mesh.Positions.Add(p0 + r1); mesh.Positions.Add(p0 + r0);
            mesh.Positions.Add(p1); mesh.Positions.Add(p1 + r0); mesh.Positions.Add(p1 + r1);
            for (var k = 0; k < 6; k++) mesh.TriangleIndices.Add(cap + k);
        }
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static Material CreateMaterial(Color color) => new DiffuseMaterial(new SolidColorBrush(color));
    private static GeometryModel3D CreateLoft((double X, double Width, double Bottom, double Top)[] sections, Material material)
    {
        var mesh = new MeshGeometry3D();
        Point3D[] Ring((double X, double Width, double Bottom, double Top) s) => new[] {
            new Point3D(s.X, -s.Width, s.Bottom), new Point3D(s.X, s.Width, s.Bottom),
            new Point3D(s.X, s.Width * .93, s.Top), new Point3D(s.X, -s.Width * .93, s.Top) };
        for (var i = 0; i < sections.Length - 1; i++)
        {
            var a = Ring(sections[i]); var b = Ring(sections[i + 1]);
            for (var j = 0; j < 4; j++) AppendQuad(mesh, a[j], b[j], b[(j+1)%4], a[(j+1)%4]);
        }
        var first = Ring(sections[0]); var last = Ring(sections[^1]);
        AppendQuad(mesh, first[3], first[2], first[1], first[0]);
        AppendQuad(mesh, last[0], last[1], last[2], last[3]);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
    private static Material CreateGlassMaterial() => new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(160, 160, 200, 230)));
    private static Material CreateTireMaterial() => CreateMaterial(Color.FromRgb(28, 28, 30));
    private static Material CreateRimMaterial() => CreateMaterial(Color.FromRgb(180, 180, 185));
    private static Material CreateHubMaterial() => CreateMaterial(Color.FromRgb(90, 90, 95));
}
