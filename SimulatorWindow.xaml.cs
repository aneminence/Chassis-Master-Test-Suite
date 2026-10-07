using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Chassis_Master_Test_Suite.Simulator;

public partial class SimulatorWindow : Window
{
    private readonly VehicleSimulator _simulator;

    private readonly DispatcherTimer _renderTimer;

    private CancellationTokenSource? _simulatorCts;
    private Task? _simulatorTask;

    private Model3DGroup? _vehicleGroup;

    private AxisAngleRotation3D? _vehicleHeadingRotation;

    private TranslateTransform3D? _vehicleTranslation;

    private AxisAngleRotation3D? _frontLeftSteeringRotation;
    private AxisAngleRotation3D? _frontRightSteeringRotation;

    private AxisAngleRotation3D? _frontLeftRollingRotation;
    private AxisAngleRotation3D? _frontRightRollingRotation;
    private AxisAngleRotation3D? _rearLeftRollingRotation;
    private AxisAngleRotation3D? _rearRightRollingRotation;

    private double _wheelRotationAngle;

    private double _lastVehicleX;
    private double _lastVehicleZ;

    private bool _hasPreviousVehiclePosition;

    private readonly PerspectiveCamera _camera;

    private const double VehicleLength = 4.60;
    private const double VehicleWidth = 1.90;

    private const double WheelRadius = 0.39;
    private const double WheelWidth = 0.28;

    private const double WheelX = 0.86;

    private const double FrontWheelZ = 1.35;
    private const double RearWheelZ = -1.35;

    private const double VehicleGroundY = WheelRadius;

    public SimulatorWindow(
        VehicleSimulator simulator)
    {
        InitializeComponent();

        _simulator = simulator;

        _camera = FollowCamera;

        BuildScene();

        _renderTimer =
            new DispatcherTimer(
                DispatcherPriority.Render);

        _renderTimer.Interval =
            TimeSpan.FromMilliseconds(16);

        _renderTimer.Tick +=
            RenderTimer_Tick;

        _renderTimer.Start();

        Loaded +=
            SimulatorWindow_Loaded;

        Closed +=
            SimulatorWindow_Closed;

        Focusable = true;
    }

    private void SimulatorWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        /*
         * 获取键盘焦点。
         */

        Focusable = true;

        Focus();

        Keyboard.Focus(this);

        /*
         * 初始化车辆。
         */

        _simulator.ResetVehicle();

        _wheelRotationAngle = 0.0;

        _hasPreviousVehiclePosition = false;

        /*
         * 初始化摄像机。
         */

        UpdateCamera(
            _simulator.GetState());

        /*
         * =====================================================
         * 关键：
         * 启动车辆模拟器 100 Hz 物理循环。
         *
         * 如果这里不启动 RunAsync，
         * SetControls() 即使收到 W/A/S/D，
         * 车辆也不会真正运动。
         * =====================================================
         */

        if (_simulatorCts == null)
        {
            _simulatorCts =
                new CancellationTokenSource();

            _simulatorTask =
                RunSimulatorAsync(
                    _simulatorCts.Token);
        }
    }

    private async Task RunSimulatorAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _simulator.RunAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            /*
             * 正常关闭，不需要处理。
             */
        }
        catch (Exception ex)
        {
            /*
             * 模拟器后台线程出现异常时，
             * 显示错误信息。
             */

            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    this,
                    $"Vehicle Simulator 运行失败：\n\n{ex.Message}",
                    "CMTS Simulator Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
    }

    private void SimulatorWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _renderTimer.Stop();

        _simulator.ReleaseControls();

        if (_simulatorCts != null)
        {
            _simulatorCts.Cancel();
            _simulatorCts.Dispose();
            _simulatorCts = null;
        }
    }

    private void BuildScene()
    {
        VehicleViewport.Children.Clear();

        AddLighting();

        _vehicleGroup =
            CreateVehicle();

        var vehicleVisual =
            new ModelVisual3D
            {
                Content = _vehicleGroup
            };

        VehicleViewport.Children.Add(
            vehicleVisual);

        AddGround();
    }

    private void AddLighting()
    {
        var group =
            new Model3DGroup();

        group.Children.Add(
            new AmbientLight(
                Color.FromRgb(
                    100,
                    100,
                    100)));

        group.Children.Add(
            new DirectionalLight(
                Color.FromRgb(
                    255,
                    255,
                    255),
                new Vector3D(
                    -0.4,
                    -1.0,
                    -0.6)));

        group.Children.Add(
            new DirectionalLight(
                Color.FromRgb(
                    140,
                    140,
                    140),
                new Vector3D(
                    0.5,
                    -0.4,
                    0.7)));

        VehicleViewport.Children.Add(
            new ModelVisual3D
            {
                Content = group
            });
    }

    private void AddGround()
    {
        // ========================================================
        // 基础地面
        // ========================================================

        var groundMaterial =
            CreateMaterial(
                Color.FromRgb(
                    45,
                    48,
                    52));

        var ground =
            CreateBox(
                200,
                0.10,
                2200,
                new Point3D(
                    0,
                    -0.05,
                    1000),
                groundMaterial);

        VehicleViewport.Children.Add(
            new ModelVisual3D
            {
                Content = ground
            });

        // ========================================================
        // 试验道路
        // ========================================================

        var roadMaterial =
            CreateMaterial(
                Color.FromRgb(
                    58,
                    61,
                    65));

        var road =
            CreateBox(
                8.0,
                0.025,
                2200,
                new Point3D(
                    0,
                    0.0125,
                    1000),
                roadMaterial);

        VehicleViewport.Children.Add(
            new ModelVisual3D
            {
                Content = road
            });

        // ========================================================
        // 道路边界线
        // ========================================================

        var edgeLineMaterial =
            CreateMaterial(
                Color.FromRgb(
                    235,
                    235,
                    225));

        foreach (var x in new[] { -3.8, 3.8 })
        {
            var edgeLine =
                CreateBox(
                    0.12,
                    0.035,
                    2200,
                    new Point3D(
                        x,
                        0.04,
                        1000),
                    edgeLineMaterial);

            VehicleViewport.Children.Add(
                new ModelVisual3D
                {
                    Content = edgeLine
                });
        }

        // ========================================================
        // 中间虚线
        // ========================================================

        var centerLineMaterial =
            CreateMaterial(
                Color.FromRgb(
                    225,
                    225,
                    215));

        for (double z = -20; z <= 2100; z += 8)
        {
            var centerLine =
                CreateBox(
                    0.10,
                    0.04,
                    4.0,
                    new Point3D(
                        0,
                        0.045,
                        z),
                    centerLineMaterial);

            VehicleViewport.Children.Add(
                new ModelVisual3D
                {
                    Content = centerLine
                });
        }

        // ========================================================
        // 每 10 m 一条横向距离参考线
        // ========================================================

        var distanceLineMaterial =
            CreateMaterial(
                Color.FromRgb(
                    150,
                    153,
                    155));

        for (double z = 0; z <= 2100; z += 10)
        {
            var distanceLine =
                CreateBox(
                    8.0,
                    0.035,
                    0.06,
                    new Point3D(
                        0,
                        0.042,
                        z),
                    distanceLineMaterial);

            VehicleViewport.Children.Add(
                new ModelVisual3D
                {
                    Content = distanceLine
                });
        }

        // ========================================================
        // 路边标杆
        // 每 20 m 一个，作为非常明显的运动参照物。
        // ========================================================

        var postMaterial =
            CreateMaterial(
                Color.FromRgb(
                    235,
                    235,
                    235));

        var postTopMaterial =
            CreateMaterial(
                Color.FromRgb(
                    210,
                    55,
                    45));

        for (double z = -20; z <= 2100; z += 20)
        {
            foreach (var x in new[] { -5.0, 5.0 })
            {
                var post =
                    CreateBox(
                        0.16,
                        1.0,
                        0.16,
                        new Point3D(
                            x,
                            0.50,
                            z),
                        postMaterial);

                VehicleViewport.Children.Add(
                    new ModelVisual3D
                    {
                        Content = post
                    });

                var top =
                    CreateBox(
                        0.22,
                        0.20,
                        0.22,
                        new Point3D(
                            x,
                            1.08,
                            z),
                        postTopMaterial);

                VehicleViewport.Children.Add(
                    new ModelVisual3D
                    {
                        Content = top
                    });
            }
        }
    }

    private Model3DGroup CreateVehicle()
    {
        var group =
            new Model3DGroup();

        var bodyMaterial =
            CreateMaterial(
                Color.FromRgb(
                    35,
                    90,
                    180));

        var darkMaterial =
            CreateMaterial(
                Color.FromRgb(
                    20,
                    22,
                    25));

        var glassMaterial =
            CreateGlassMaterial();

        var lightMaterial =
            CreateMaterial(
                Color.FromRgb(
                    230,
                    240,
                    255));

        var rearLightMaterial =
            CreateMaterial(
                Color.FromRgb(
                    180,
                    30,
                    30));

        /*
         * 车身主体
         */

        group.Children.Add(
            CreateBox(
                VehicleWidth,
                0.48,
                VehicleLength,
                new Point3D(
                    0,
                    0.65,
                    0),
                bodyMaterial));

        /*
         * 车身上部
         */

        group.Children.Add(
            CreateBox(
                1.72,
                0.48,
                2.55,
                new Point3D(
                    0,
                    1.08,
                    -0.10),
                bodyMaterial));

        /*
         * 车顶
         */

        group.Children.Add(
            CreateBox(
                1.60,
                0.10,
                1.65,
                new Point3D(
                    0,
                    1.40,
                    -0.10),
                darkMaterial));

        /*
         * 前机舱
         */

        group.Children.Add(
            CreateBox(
                1.70,
                0.18,
                1.05,
                new Point3D(
                    0,
                    1.10,
                    1.40),
                bodyMaterial));

        /*
         * 前风挡
         */

        group.Children.Add(
            CreateBox(
                1.52,
                0.06,
                0.70,
                new Point3D(
                    0,
                    1.32,
                    0.75),
                glassMaterial));

        /*
         * 后风挡
         */

        group.Children.Add(
            CreateBox(
                1.50,
                0.06,
                0.55,
                new Point3D(
                    0,
                    1.32,
                    -0.92),
                glassMaterial));

        /*
         * A 柱
         */

        AddBox(
            group,
            0.08,
            0.38,
            0.08,
            new Point3D(
                -0.76,
                1.30,
                0.45),
            darkMaterial);

        AddBox(
            group,
            0.08,
            0.38,
            0.08,
            new Point3D(
                0.76,
                1.30,
                0.45),
            darkMaterial);

        /*
         * B 柱
         */

        AddBox(
            group,
            0.08,
            0.38,
            0.08,
            new Point3D(
                -0.76,
                1.30,
                -0.55),
            darkMaterial);

        AddBox(
            group,
            0.08,
            0.38,
            0.08,
            new Point3D(
                0.76,
                1.30,
                -0.55),
            darkMaterial);

        /*
         * 前保险杠
         */

        AddBox(
            group,
            1.92,
            0.22,
            0.18,
            new Point3D(
                0,
                0.60,
                2.30),
            darkMaterial);

        /*
         * 后保险杠
         */

        AddBox(
            group,
            1.92,
            0.22,
            0.18,
            new Point3D(
                0,
                0.60,
                -2.30),
            darkMaterial);

        /*
         * 前格栅
         */

        AddBox(
            group,
            0.85,
            0.24,
            0.05,
            new Point3D(
                0,
                0.78,
                2.34),
            darkMaterial);

        /*
         * 前灯
         */

        AddBox(
            group,
            0.42,
            0.16,
            0.06,
            new Point3D(
                -0.58,
                0.86,
                2.34),
            lightMaterial);

        AddBox(
            group,
            0.42,
            0.16,
            0.06,
            new Point3D(
                0.58,
                0.86,
                2.34),
            lightMaterial);

        /*
         * 后灯
         */

        AddBox(
            group,
            0.42,
            0.16,
            0.06,
            new Point3D(
                -0.58,
                0.86,
                -2.34),
            rearLightMaterial);

        AddBox(
            group,
            0.42,
            0.16,
            0.06,
            new Point3D(
                0.58,
                0.86,
                -2.34),
            rearLightMaterial);

        /*
         * 后视镜
         */

        AddBox(
            group,
            0.12,
            0.14,
            0.28,
            new Point3D(
                -0.98,
                1.15,
                0.65),
            darkMaterial);

        AddBox(
            group,
            0.12,
            0.14,
            0.28,
            new Point3D(
                0.98,
                1.15,
                0.65),
            darkMaterial);

        /*
         * 四个车轮
         */

        group.Children.Add(
            CreateWheelAssembly(
                -WheelX,
                VehicleGroundY,
                FrontWheelZ,
                true));

        group.Children.Add(
            CreateWheelAssembly(
                WheelX,
                VehicleGroundY,
                FrontWheelZ,
                true));

        group.Children.Add(
            CreateWheelAssembly(
                -WheelX,
                VehicleGroundY,
                RearWheelZ,
                false));

        group.Children.Add(
            CreateWheelAssembly(
                WheelX,
                VehicleGroundY,
                RearWheelZ,
                false));

        /*
         * 车辆整体航向旋转
         */

        _vehicleHeadingRotation =
            new AxisAngleRotation3D(
                new Vector3D(
                    0,
                    1,
                    0),
                0);

        var vehicleTransform =
            new Transform3DGroup();

        vehicleTransform.Children.Add(
            new RotateTransform3D(
                _vehicleHeadingRotation));

        // 车辆模型本身在局部坐标原点，
        // 每一帧把整个车辆移动到 Simulator 的 X/Z 坐标。
        _vehicleTranslation =
            new TranslateTransform3D(
                0,
                0,
                0);

        vehicleTransform.Children.Add(
            _vehicleTranslation);

        group.Transform =
            vehicleTransform;

        return group;
    }

    private Model3DGroup CreateWheelAssembly(
        double x,
        double y,
        double z,
        bool isFrontWheel)
    {
        var wheelGroup =
            new Model3DGroup();

        var tireMaterial =
            CreateTireMaterial();

        var rimMaterial =
            CreateRimMaterial();

        var hubMaterial =
            CreateHubMaterial();

        var spokeMaterial =
            CreateSpokeMaterial();

        /*
         * 轮胎
         */

        wheelGroup.Children.Add(
            CreateCylinder(
                WheelRadius,
                WheelWidth,
                tireMaterial,
                new Point3D(
                    x,
                    y,
                    z),
                new Vector3D(
                    1,
                    0,
                    0)));

        /*
         * 轮毂
         */

        wheelGroup.Children.Add(
            CreateCylinder(
                0.24,
                WheelWidth + 0.02,
                rimMaterial,
                new Point3D(
                    x,
                    y,
                    z),
                new Vector3D(
                    1,
                    0,
                    0)));

        /*
         * 中心轴
         */

        wheelGroup.Children.Add(
            CreateCylinder(
                0.09,
                WheelWidth + 0.04,
                hubMaterial,
                new Point3D(
                    x,
                    y,
                    z),
                new Vector3D(
                    1,
                    0,
                    0)));

        /*
         * 6 根轮毂辐条
         */

        for (int i = 0; i < 6; i++)
        {
            var angle =
                i *
                Math.PI /
                3.0;

            var spoke =
                CreateBox(
                    0.06,
                    0.06,
                    0.34,
                    new Point3D(
                        x,
                        y,
                        z),
                    spokeMaterial);

            var rotation =
                new AxisAngleRotation3D(
                    new Vector3D(
                        1,
                        0,
                        0),
                    angle *
                    180.0 /
                    Math.PI);

            spoke.Transform =
                new RotateTransform3D(
                    rotation,
                    x,
                    y,
                    z);

            wheelGroup.Children.Add(
                spoke);
        }

        /*
         * 车轮滚动
         */

        var rollingRotation =
            new AxisAngleRotation3D(
                new Vector3D(
                    1,
                    0,
                    0),
                0);

        var rollingTransform =
            new RotateTransform3D(
                rollingRotation,
                x,
                y,
                z);

        /*
         * 前轮转向
         */

        if (isFrontWheel)
        {
            var steeringRotation =
                new AxisAngleRotation3D(
                    new Vector3D(
                        0,
                        1,
                        0),
                    0);

            var steeringTransform =
                new RotateTransform3D(
                    steeringRotation,
                    x,
                    y,
                    z);

            var transformGroup =
                new Transform3DGroup();

            // 顺序很重要：
            //
            // 先施加转向，再施加滚动。
            //
            // 这样滚动始终绕「车轮自己转向后的横轴」进行，
            // 转向时车轮不会跟着歪。
            //
            // 如果写成 rolling 在前、steering 在后，
            // 滚动轴会被转向旋转带偏。
            transformGroup.Children.Add(
                steeringTransform);

            transformGroup.Children.Add(
                rollingTransform);

            wheelGroup.Transform =
                transformGroup;

            if (x < 0)
            {
                _frontLeftRollingRotation =
                    rollingRotation;

                _frontLeftSteeringRotation =
                    steeringRotation;
            }
            else
            {
                _frontRightRollingRotation =
                    rollingRotation;

                _frontRightSteeringRotation =
                    steeringRotation;
            }
        }
        else
        {
            wheelGroup.Transform =
                rollingTransform;

            if (x < 0)
            {
                _rearLeftRollingRotation =
                    rollingRotation;
            }
            else
            {
                _rearRightRollingRotation =
                    rollingRotation;
            }
        }

        return wheelGroup;
    }

    private void RenderTimer_Tick(
        object? sender,
        EventArgs e)
    {
        var state =
            _simulator.GetState();

        UpdateVehicleVisual(
            state);

        UpdateWheelRolling(
            state);

        UpdateCamera(
            state);

        UpdateStatus(
            state);
    }

    private void UpdateVehicleVisual(
        VehicleState state)
    {
        if (_vehicleTranslation != null)
        {
            _vehicleTranslation.OffsetX = state.X;
            _vehicleTranslation.OffsetZ = state.Z;
        }

        if (_vehicleHeadingRotation != null)
        {
            _vehicleHeadingRotation.Angle =
                state.HeadingDeg;
        }

        if (_frontLeftSteeringRotation != null)
        {
            _frontLeftSteeringRotation.Angle =
                state.SteeringAngleDeg;
        }

        if (_frontRightSteeringRotation != null)
        {
            _frontRightSteeringRotation.Angle =
                state.SteeringAngleDeg;
        }
    }

    private void UpdateWheelRolling(
        VehicleState state)
    {
        if (!_hasPreviousVehiclePosition)
        {
            _lastVehicleX =
                state.X;

            _lastVehicleZ =
                state.Z;

            _hasPreviousVehiclePosition =
                true;

            return;
        }

        var deltaX =
            state.X -
            _lastVehicleX;

        var deltaZ =
            state.Z -
            _lastVehicleZ;

        _lastVehicleX =
            state.X;

        _lastVehicleZ =
            state.Z;

        var headingRad =
            state.HeadingDeg *
            Math.PI /
            180.0;

        var forwardX =
            Math.Sin(
                headingRad);

        var forwardZ =
            Math.Cos(
                headingRad);

        var signedDistance =
            deltaX *
            forwardX +
            deltaZ *
            forwardZ;

        if (Math.Abs(
                signedDistance) <
            0.0000001)
        {
            return;
        }

        var deltaAngleRad =
            signedDistance /
            WheelRadius;

        var deltaAngleDeg =
            deltaAngleRad *
            180.0 /
            Math.PI;

        // ========================================================
        // 车轮滚动方向
        //
        // 车轮网格的横轴是车辆局部 +X（已实测：屏幕左侧）。
        // 车辆前进方向是 +Z。
        //
        // 绕局部 +X 轴正向旋转时，
        // 轮子顶部会朝 -Z 方向移动，也就是倒转。
        //
        // 因此这里使用 +，让车轮顶部朝 +Z 前进，
        // 与实际行驶方向一致。
        //
        // 历史问题：
        // 这里曾经写成 -=，会导致车轮看起来在倒转。
        // ========================================================

        _wheelRotationAngle +=
            deltaAngleDeg;

        if (_wheelRotationAngle >
                360000.0 ||
            _wheelRotationAngle <
                -360000.0)
        {
            _wheelRotationAngle %=
                360.0;
        }

        if (_frontLeftRollingRotation != null)
        {
            _frontLeftRollingRotation.Angle =
                _wheelRotationAngle;
        }

        if (_frontRightRollingRotation != null)
        {
            _frontRightRollingRotation.Angle =
                _wheelRotationAngle;
        }

        if (_rearLeftRollingRotation != null)
        {
            _rearLeftRollingRotation.Angle =
                _wheelRotationAngle;
        }

        if (_rearRightRollingRotation != null)
        {
            _rearRightRollingRotation.Angle =
                _wheelRotationAngle;
        }
    }

    private void UpdateCamera(
        VehicleState state)
    {
        var headingRad =
            state.HeadingDeg *
            Math.PI /
            180.0;

        var forwardX =
            Math.Sin(
                headingRad);

        var forwardZ =
            Math.Cos(
                headingRad);

        const double cameraDistance =
            8.5;

        const double cameraHeight =
            5.2;

        var cameraX =
            state.X -
            forwardX *
            cameraDistance;

        var cameraZ =
            state.Z -
            forwardZ *
            cameraDistance;

        _camera.Position =
            new Point3D(
                cameraX,
                cameraHeight,
                cameraZ);

        const double lookAhead =
            5.0;

        var targetX =
            state.X +
            forwardX *
            lookAhead;

        var targetZ =
            state.Z +
            forwardZ *
            lookAhead;

        var lookDirection =
            new Vector3D(
                targetX -
                cameraX,
                1.0 -
                cameraHeight,
                targetZ -
                cameraZ);

        lookDirection.Normalize();

        _camera.LookDirection =
            lookDirection;

        _camera.UpDirection =
            new Vector3D(
                0,
                1,
                0);
    }

    private void UpdateStatus(
        VehicleState state)
    {
        SpeedText.Text =
            $"{state.SpeedMps * 3.6:0.0} km/h";

        LongAccelText.Text =
            $"{state.LongitudinalAcceleration:0.00} m/s²";

        LatAccelText.Text =
            $"{state.LateralAcceleration:0.00} m/s²";

        YawRateText.Text =
            $"{state.YawRateDegPerSec:0.00} deg/s";

        HeadingText.Text =
            $"{state.HeadingDeg:0.0}°";

        SteeringText.Text =
            $"{state.SteeringAngleDeg:0.0}°";

        DistanceText.Text =
            $"{Math.Sqrt(state.X * state.X + state.Z * state.Z):0.0} m";
    }

    /*
     * =========================================================
     * 键盘控制
     * =========================================================
     */

    private void Window_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            _simulator.ResetVehicle();

            _wheelRotationAngle =
                0.0;

            _hasPreviousVehiclePosition =
                false;

            e.Handled = true;

            return;
        }

        ApplyKeyboardControls();

        e.Handled = true;
    }

    private void Window_KeyUp(
        object sender,
        KeyEventArgs e)
    {
        ApplyKeyboardControls();

        e.Handled = true;
    }

    private void ApplyKeyboardControls()
    {
        double throttle = 0.0;

        double brake = 0.0;

        double steering = 0.0;

        if (Keyboard.IsKeyDown(
                Key.W))
        {
            throttle = 1.0;
        }

        if (Keyboard.IsKeyDown(
                Key.S))
        {
            brake = 1.0;
        }

        if (Keyboard.IsKeyDown(
                Key.Space))
        {
            brake = 1.0;
        }

        /*
         * steering 约定：
         *
         * +1 = 左
         * -1 = 右
         *
         * 与 VehicleSimulator.SetControls、
         * Heading（+ = 左）完全一致。
         */
        if (Keyboard.IsKeyDown(
                Key.A))
        {
            steering = 1.0;
        }

        if (Keyboard.IsKeyDown(
                Key.D))
        {
            steering = -1.0;
        }

        _simulator.SetControls(
            throttle,
            brake,
            steering);
    }

    private void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        _renderTimer.Stop();

        _simulator.ReleaseControls();

        if (_simulatorCts != null)
        {
            _simulatorCts.Cancel();
        }
    }

    private static GeometryModel3D CreateBox(
        double sizeX,
        double sizeY,
        double sizeZ,
        Point3D center,
        Material material)
    {
        var mesh =
            new MeshGeometry3D();

        var x1 =
            center.X -
            sizeX / 2.0;

        var x2 =
            center.X +
            sizeX / 2.0;

        var y1 =
            center.Y -
            sizeY / 2.0;

        var y2 =
            center.Y +
            sizeY / 2.0;

        var z1 =
            center.Z -
            sizeZ / 2.0;

        var z2 =
            center.Z +
            sizeZ / 2.0;

        mesh.Positions.Add(
            new Point3D(
                x1,
                y1,
                z1));

        mesh.Positions.Add(
            new Point3D(
                x2,
                y1,
                z1));

        mesh.Positions.Add(
            new Point3D(
                x2,
                y2,
                z1));

        mesh.Positions.Add(
            new Point3D(
                x1,
                y2,
                z1));

        mesh.Positions.Add(
            new Point3D(
                x1,
                y1,
                z2));

        mesh.Positions.Add(
            new Point3D(
                x2,
                y1,
                z2));

        mesh.Positions.Add(
            new Point3D(
                x2,
                y2,
                z2));

        mesh.Positions.Add(
            new Point3D(
                x1,
                y2,
                z2));

        AddQuad(
            mesh,
            0,
            1,
            2,
            3);

        AddQuad(
            mesh,
            4,
            7,
            6,
            5);

        AddQuad(
            mesh,
            0,
            4,
            5,
            1);

        AddQuad(
            mesh,
            1,
            5,
            6,
            2);

        AddQuad(
            mesh,
            2,
            6,
            7,
            3);

        AddQuad(
            mesh,
            4,
            0,
            3,
            7);

        return new GeometryModel3D
        {
            Geometry =
                mesh,

            Material =
                material,

            BackMaterial =
                material
        };
    }

    private static void AddBox(
        Model3DGroup group,
        double sizeX,
        double sizeY,
        double sizeZ,
        Point3D center,
        Material material)
    {
        group.Children.Add(
            CreateBox(
                sizeX,
                sizeY,
                sizeZ,
                center,
                material));
    }

    private static void AddQuad(
        MeshGeometry3D mesh,
        int a,
        int b,
        int c,
        int d)
    {
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);

        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(c);
        mesh.TriangleIndices.Add(d);
    }

    private static GeometryModel3D CreateCylinder(
        double radius,
        double height,
        Material material,
        Point3D center,
        Vector3D axis)
    {
        const int segments = 32;

        var mesh =
            new MeshGeometry3D();

        var normalizedAxis =
            axis;

        normalizedAxis.Normalize();

        Vector3D reference;

        if (Math.Abs(
                Vector3D.DotProduct(
                    normalizedAxis,
                    new Vector3D(
                        0,
                        1,
                        0))) < 0.9)
        {
            reference =
                new Vector3D(
                    0,
                    1,
                    0);
        }
        else
        {
            reference =
                new Vector3D(
                    0,
                    0,
                    1);
        }

        var basis1 =
            Vector3D.CrossProduct(
                normalizedAxis,
                reference);

        basis1.Normalize();

        var basis2 =
            Vector3D.CrossProduct(
                normalizedAxis,
                basis1);

        basis2.Normalize();

        for (
            int i = 0;
            i < segments;
            i++)
        {
            var angle =
                2.0 *
                Math.PI *
                i /
                segments;

            var radial =
                basis1 *
                Math.Cos(angle) +
                basis2 *
                Math.Sin(angle);

            var bottom =
                center +
                radial * radius -
                normalizedAxis *
                (height / 2.0);

            var top =
                center +
                radial * radius +
                normalizedAxis *
                (height / 2.0);

            mesh.Positions.Add(
                bottom);

            mesh.Positions.Add(
                top);
        }

        for (
            int i = 0;
            i < segments;
            i++)
        {
            var next =
                (i + 1) %
                segments;

            var bottom1 =
                i * 2;

            var top1 =
                i * 2 + 1;

            var bottom2 =
                next * 2;

            var top2 =
                next * 2 + 1;

            mesh.TriangleIndices.Add(
                bottom1);

            mesh.TriangleIndices.Add(
                bottom2);

            mesh.TriangleIndices.Add(
                top2);

            mesh.TriangleIndices.Add(
                bottom1);

            mesh.TriangleIndices.Add(
                top2);

            mesh.TriangleIndices.Add(
                top1);
        }

        return new GeometryModel3D
        {
            Geometry =
                mesh,

            Material =
                material,

            BackMaterial =
                material
        };
    }

    private static Material CreateMaterial(
        Color color)
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                color));
    }

    private static Material CreateGlassMaterial()
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                Color.FromArgb(
                    210,
                    35,
                    55,
                    75)));
    }

    private static Material CreateTireMaterial()
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                Color.FromRgb(
                    18,
                    18,
                    18)));
    }

    private static Material CreateRimMaterial()
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                Color.FromRgb(
                    105,
                    110,
                    115)));
    }

    private static Material CreateHubMaterial()
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                Color.FromRgb(
                    55,
                    58,
                    62)));
    }

    private static Material CreateSpokeMaterial()
    {
        return new DiffuseMaterial(
            new SolidColorBrush(
                Color.FromRgb(
                    190,
                    195,
                    200)));
    }
}