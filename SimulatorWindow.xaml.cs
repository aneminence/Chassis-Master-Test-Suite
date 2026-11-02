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
    private AxisAngleRotation3D? _vehiclePitchRotation;
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

    private Point3D _chaseCamPos;
    private Vector3D _chaseLook;
    private Vector3D _chaseUp = new(0, 0, 1);
    private bool _chaseInitialized;
    private DateTime _lastCamTick = DateTime.UtcNow;

    public SimulatorWindow(VehicleSimulator simulator)
    {
        InitializeComponent();
        _simulator = simulator;

        var start = SimulatorSceneBuilder.GetStartPose();
        _simulator.SetStartPose(start.X, start.Y, start.Z, start.HeadingDeg);

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
        _chaseInitialized = false;
        ApplyFollowCameraMode();
        UpdateCamera(_simulator.GetState(), 1.0 / 60.0);

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
                    $"Vehicle Simulator failed:\n\n{ex.Message}",
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
        for (var i = Viewport.Children.Count - 1; i >= 0; i--)
        {
            var typeName = Viewport.Children[i].GetType().Name;
            if (typeName is not ("SunLight" or "DefaultLights"))
            {
                Viewport.Children.RemoveAt(i);
            }
        }

        Viewport.Children.Add(new ModelVisual3D
        {
            Content = SimulatorSceneBuilder.BuildEnvironment()
        });

        var vehicle = SimulatorSceneBuilder.BuildVehicle(
            out _vehicleHeadingRotation,
            out _vehiclePitchRotation,
            out _vehicleTranslation,
            out _frontLeftSteeringRotation,
            out _frontRightSteeringRotation,
            out _frontLeftRollingRotation,
            out _frontRightRollingRotation,
            out _rearLeftRollingRotation,
            out _rearRightRollingRotation);

        Viewport.Children.Add(new ModelVisual3D { Content = vehicle });
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastCamTick).TotalSeconds;
        _lastCamTick = now;
        if (dt <= 0 || dt > 0.1) dt = 1.0 / 60.0;

        var state = _simulator.GetState();
        UpdateVehicleVisual(state);
        UpdateWheelRolling(state);
        if (_followCamera)
        {
            UpdateCamera(state, dt);
        }

        UpdateStatus(state);
    }

    private void UpdateVehicleVisual(VehicleState state)
    {
        if (_vehicleTranslation != null)
        {
            _vehicleTranslation.OffsetX = state.X;
            _vehicleTranslation.OffsetY = state.Y;
            _vehicleTranslation.OffsetZ = state.Z;
        }

        if (_vehicleHeadingRotation != null)
        {
            _vehicleHeadingRotation.Angle = state.HeadingDeg;
        }

        if (_vehiclePitchRotation != null)
        {
            // Pitch about +Y: positive nose up when climbing (+tangent.Z).
            _vehiclePitchRotation.Angle = -state.PitchDeg;
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
        var signedDistance = deltaX * Math.Cos(headingRad) + deltaY * Math.Sin(headingRad);
        if (Math.Abs(signedDistance) < 1e-7) return;

        var deltaAngleDeg = signedDistance / SimulatorSceneBuilder.WheelRadius * 180.0 / Math.PI;
        _wheelRotationAngle -= deltaAngleDeg;
        if (Math.Abs(_wheelRotationAngle) > 360000.0) _wheelRotationAngle %= 360.0;

        if (_frontLeftRollingRotation != null) _frontLeftRollingRotation.Angle = _wheelRotationAngle;
        if (_frontRightRollingRotation != null) _frontRightRollingRotation.Angle = _wheelRotationAngle;
        if (_rearLeftRollingRotation != null) _rearLeftRollingRotation.Angle = _wheelRotationAngle;
        if (_rearRightRollingRotation != null) _rearRightRollingRotation.Angle = _wheelRotationAngle;
    }

    private void UpdateCamera(VehicleState state, double dt)
    {
        var headingRad = state.HeadingDeg * Math.PI / 180.0;
        var forwardX = Math.Cos(headingRad);
        var forwardY = Math.Sin(headingRad);
        const double cameraDistance = 11.0;
        const double cameraHeight = 4.2;
        const double lookAhead = 14.0;
        const double positionLag = 0.18;
        const double lookLag = 0.28;

        var desiredPos = new Point3D(
            state.X - forwardX * cameraDistance,
            state.Y - forwardY * cameraDistance,
            state.Z + cameraHeight);

        var lookTarget = new Point3D(
            state.X + forwardX * lookAhead,
            state.Y + forwardY * lookAhead,
            state.Z + 1.2);

        // Horizon stays level — no lateral camera bank/roll on steering.
        var up = new Vector3D(0, 0, 1);

        if (!_chaseInitialized)
        {
            _chaseCamPos = desiredPos;
            _chaseLook = lookTarget - desiredPos;
            _chaseLook.Normalize();
            _chaseUp = up;
            _chaseInitialized = true;
        }
        else
        {
            var posAlpha = 1.0 - Math.Exp(-dt / positionLag);
            var lookAlpha = 1.0 - Math.Exp(-dt / lookLag);
            _chaseCamPos = new Point3D(
                _chaseCamPos.X + (desiredPos.X - _chaseCamPos.X) * posAlpha,
                _chaseCamPos.Y + (desiredPos.Y - _chaseCamPos.Y) * posAlpha,
                _chaseCamPos.Z + (desiredPos.Z - _chaseCamPos.Z) * posAlpha);

            var desiredLook = lookTarget - _chaseCamPos;
            if (desiredLook.Length > 1e-6)
            {
                desiredLook.Normalize();
                _chaseLook = new Vector3D(
                    _chaseLook.X + (desiredLook.X - _chaseLook.X) * lookAlpha,
                    _chaseLook.Y + (desiredLook.Y - _chaseLook.Y) * lookAlpha,
                    _chaseLook.Z + (desiredLook.Z - _chaseLook.Z) * lookAlpha);
                if (_chaseLook.Length > 1e-6) _chaseLook.Normalize();
            }

            _chaseUp = up;
        }

        if (Viewport.Camera is not PerspectiveCamera camera) return;
        camera.Position = _chaseCamPos;
        camera.LookDirection = _chaseLook;
        camera.UpDirection = _chaseUp;
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
        PositionText.Text = $"{state.X:0.0} / {state.Y:0.0} / z{state.Z:0.0} m";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            _simulator.ResetVehicle();
            _wheelRotationAngle = 0.0;
            _hasPreviousVehiclePosition = false;
            _chaseInitialized = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C)
        {
            _followCamera = !_followCamera;
            if (_followCamera) _chaseInitialized = false;
            ApplyFollowCameraMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P)
        {
            _paused = !_paused;
            _simulator.IsPaused = _paused;
            if (_paused) _simulator.ReleaseControls();
            CameraModeText.Text = _paused
                ? "PAUSED  (P resume)"
                : (_followCamera ? "Camera: Chase  (C toggle)" : "Camera: Free  (C toggle)");
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
            ? "Camera: Chase  (C toggle)"
            : "Camera: Free  (C toggle)";
        Viewport.IsRotationEnabled = !_followCamera;
        Viewport.IsPanEnabled = !_followCamera;
        Viewport.IsZoomEnabled = true;
    }

    private void ApplyKeyboardControls()
    {
        double throttle = 0.0, brake = 0.0, steering = 0.0;
        if (Keyboard.IsKeyDown(Key.W)) throttle = 1.0;
        if (Keyboard.IsKeyDown(Key.S) || Keyboard.IsKeyDown(Key.Space)) brake = 1.0;
        if (Keyboard.IsKeyDown(Key.A)) steering = 1.0;
        if (Keyboard.IsKeyDown(Key.D)) steering = -1.0;
        _simulator.SetControls(throttle, brake, steering);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _simulator.ReleaseControls();
    }
}
