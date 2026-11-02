using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Chassis_Master_Test_Suite.Simulator;

public partial class SimulatorWindow : Window
{
    private readonly VehicleSimulator _simulator;
    private readonly DispatcherTimer _renderTimer;

    private CancellationTokenSource? _simulatorCts;
    private Task? _simulatorTask;

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
    private double _lastVehicleY;
    private bool _hasPreviousVehiclePosition;
    private bool _followCamera = true;
    private bool _paused;

    public SimulatorWindow(VehicleSimulator simulator)
    {
        InitializeComponent();

        _simulator = simulator;

        var start = SimulatorSceneBuilder.GetStartPose();
        _simulator.SetStartPose(start.X, start.Y, start.HeadingDeg);

        BuildScene();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += RenderTimer_Tick;
        _renderTimer.Start();

        Loaded += SimulatorWindow_Loaded;
        Closed += SimulatorWindow_Closed;
        Focusable = true;
    }

    private void SimulatorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Focusable = true;
        Focus();
        Keyboard.Focus(this);

        _simulator.ResetVehicle();
        _wheelRotationAngle = 0.0;
        _hasPreviousVehiclePosition = false;
        ApplyFollowCameraMode();
        UpdateCamera(_simulator.GetState());

        if (_simulatorCts == null)
        {
            _simulatorCts = new CancellationTokenSource();
            _simulatorTask = RunSimulatorAsync(_simulatorCts.Token);
        }
    }

    private async Task RunSimulatorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _simulator.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
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

    private void SimulatorWindow_Closed(object? sender, EventArgs e)
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
        // 保留 XAML 中的灯光（SunLight / DefaultLights），清掉其它动态内容后重建赛道与车。
        for (var i = Viewport.Children.Count - 1; i >= 0; i--)
        {
            var child = Viewport.Children[i];
            var typeName = child.GetType().Name;
            if (typeName is not ("SunLight" or "DefaultLights"))
            {
                Viewport.Children.RemoveAt(i);
            }
        }

        Viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D
        {
            Content = SimulatorSceneBuilder.BuildEnvironment()
        });

        var vehicle = SimulatorSceneBuilder.BuildVehicle(
            out _vehicleHeadingRotation,
            out _vehicleTranslation,
            out _frontLeftSteeringRotation,
            out _frontRightSteeringRotation,
            out _frontLeftRollingRotation,
            out _frontRightRollingRotation,
            out _rearLeftRollingRotation,
            out _rearRightRollingRotation);

        Viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D
        {
            Content = vehicle
        });
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        var state = _simulator.GetState();
        UpdateVehicleVisual(state);
        UpdateWheelRolling(state);

        if (_followCamera)
        {
            UpdateCamera(state);
        }

        UpdateStatus(state);
    }

    private void UpdateVehicleVisual(VehicleState state)
    {
        if (_vehicleTranslation != null)
        {
            _vehicleTranslation.OffsetX = state.X;
            _vehicleTranslation.OffsetY = state.Y;
            _vehicleTranslation.OffsetZ = 0.0;
        }

        if (_vehicleHeadingRotation != null)
        {
            _vehicleHeadingRotation.Angle = state.HeadingDeg;
        }

        if (_frontLeftSteeringRotation != null)
        {
            _frontLeftSteeringRotation.Angle = state.SteeringAngleDeg;
        }

        if (_frontRightSteeringRotation != null)
        {
            _frontRightSteeringRotation.Angle = state.SteeringAngleDeg;
        }
    }

    private void UpdateWheelRolling(VehicleState state)
    {
        if (!_hasPreviousVehiclePosition)
        {
            _lastVehicleX = state.X;
            _lastVehicleY = state.Y;
            _hasPreviousVehiclePosition = true;
            return;
        }

        var deltaX = state.X - _lastVehicleX;
        var deltaY = state.Y - _lastVehicleY;
        _lastVehicleX = state.X;
        _lastVehicleY = state.Y;

        var headingRad = state.HeadingDeg * Math.PI / 180.0;
        var forwardX = Math.Cos(headingRad);
        var forwardY = Math.Sin(headingRad);
        var signedDistance = deltaX * forwardX + deltaY * forwardY;

        if (Math.Abs(signedDistance) < 1e-7)
        {
            return;
        }

        var deltaAngleDeg = signedDistance / SimulatorSceneBuilder.WheelRadius * 180.0 / Math.PI;

        // 绕车体 +Y 滚动：前进用负角，使轮顶朝 +X。
        _wheelRotationAngle -= deltaAngleDeg;
        if (Math.Abs(_wheelRotationAngle) > 360000.0)
        {
            _wheelRotationAngle %= 360.0;
        }

        if (_frontLeftRollingRotation != null)
        {
            _frontLeftRollingRotation.Angle = _wheelRotationAngle;
        }

        if (_frontRightRollingRotation != null)
        {
            _frontRightRollingRotation.Angle = _wheelRotationAngle;
        }

        if (_rearLeftRollingRotation != null)
        {
            _rearLeftRollingRotation.Angle = _wheelRotationAngle;
        }

        if (_rearRightRollingRotation != null)
        {
            _rearRightRollingRotation.Angle = _wheelRotationAngle;
        }
    }

    private void UpdateCamera(VehicleState state)
    {
        var headingRad = state.HeadingDeg * Math.PI / 180.0;
        var forwardX = Math.Cos(headingRad);
        var forwardY = Math.Sin(headingRad);

        const double cameraDistance = 9.0;
        const double cameraHeight = 5.0;
        const double lookAhead = 6.0;

        var cameraX = state.X - forwardX * cameraDistance;
        var cameraY = state.Y - forwardY * cameraDistance;

        var targetX = state.X + forwardX * lookAhead;
        var targetY = state.Y + forwardY * lookAhead;

        var camera = Viewport.Camera as PerspectiveCamera;
        if (camera == null)
        {
            return;
        }

        camera.Position = new Point3D(cameraX, cameraY, cameraHeight);
        var look = new Vector3D(targetX - cameraX, targetY - cameraY, 1.2 - cameraHeight);
        look.Normalize();
        camera.LookDirection = look;
        camera.UpDirection = new Vector3D(0, 0, 1);
    }

    private void UpdateStatus(VehicleState state)
    {
        SpeedText.Text = $"{state.SpeedMps * 3.6:0.0} km/h";
        ThrottleText.Text = $"{state.Throttle * 100.0:0}%";
        BrakeText.Text = $"{state.Brake * 100.0:0}%";
        SteeringText.Text = $"{state.SteeringAngleDeg:0.0}°";
        LongAccelText.Text = $"{state.LongitudinalAcceleration:0.00} m/s²";
        LatAccelText.Text = $"{state.LateralAcceleration:0.00} m/s²";
        YawRateText.Text = $"{state.YawRateDegPerSec:0.00} deg/s";
        HeadingText.Text = $"{state.HeadingDeg:0.0}°";
        PositionText.Text = $"{state.X:0.0} / {state.Y:0.0} m";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            _simulator.ResetVehicle();
            _wheelRotationAngle = 0.0;
            _hasPreviousVehiclePosition = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C)
        {
            _followCamera = !_followCamera;
            ApplyFollowCameraMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P)
        {
            _paused = !_paused;
            _simulator.IsPaused = _paused;
            if (_paused)
            {
                _simulator.ReleaseControls();
            }

            CameraModeText.Text = _paused
                ? "PAUSED  (P 继续)"
                : (_followCamera ? "Camera: Follow  (C 切换)" : "Camera: Free  (C 切换)");
            e.Handled = true;
            return;
        }

        if (_paused)
        {
            e.Handled = true;
            return;
        }

        ApplyKeyboardControls();
        e.Handled = true;
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (_paused)
        {
            e.Handled = true;
            return;
        }

        ApplyKeyboardControls();
        e.Handled = true;
    }

    private void ApplyFollowCameraMode()
    {
        CameraModeText.Text = _followCamera
            ? "Camera: Follow  (C 切换)"
            : "Camera: Free  (C 切换)";

        // 跟车时关掉 Helix 旋转，避免抢相机；自由视角恢复鼠标控制。
        Viewport.IsRotationEnabled = !_followCamera;
        Viewport.IsPanEnabled = !_followCamera;
        Viewport.IsZoomEnabled = true;
    }

    private void ApplyKeyboardControls()
    {
        double throttle = 0.0;
        double brake = 0.0;
        double steering = 0.0;

        if (Keyboard.IsKeyDown(Key.W))
        {
            throttle = 1.0;
        }

        if (Keyboard.IsKeyDown(Key.S) || Keyboard.IsKeyDown(Key.Space))
        {
            brake = 1.0;
        }

        if (Keyboard.IsKeyDown(Key.A))
        {
            steering = 1.0;
        }

        if (Keyboard.IsKeyDown(Key.D))
        {
            steering = -1.0;
        }

        _simulator.SetControls(throttle, brake, steering);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _simulator.ReleaseControls();
    }
}

