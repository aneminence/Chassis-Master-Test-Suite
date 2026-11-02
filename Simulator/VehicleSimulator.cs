using System.Diagnostics;
using Chassis_Master_Test_Suite.Communication;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// CMTS 人工驾驶车辆模拟器：100 Hz 运动学 + UDP 输出。
///
/// 车体 / 世界水平面约定（右手系，用户锁定）：
///   X = 前进（Heading = 0° 时）
///   Y = 车辆左侧
///   Z = 向上
///
/// Heading：0° = +X；+ = 左转（朝向 +Y）；- = 右转。
/// forward = (cos(Heading), sin(Heading)) 在 X-Y 平面。
///
/// 虚拟 GPS：纬度沿 +X（北），经度沿 +Y（东，与旧版「左/前」映射到 lon/lat 的数值约定对齐，
/// 即旧 Z→lat、旧 X→lon，现改为 X→lat、Y→lon）。
/// </summary>
public sealed class VehicleSimulator
{
    private readonly UdpSender _udpSender;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _stateLock = new();
    private long _sequence;

    public static VehicleSimulator? Current { get; private set; }

    private const double WheelBase = 2.80;
    private const double MaxSteeringAngleDeg = 32.0;
    private const double MaxAcceleration = 4.0;
    private const double MaxBrakeDeceleration = 8.0;
    private const double RollingResistance = 0.12;
    private const double AeroDragCoefficient = 0.0025;

    private double _speedMps;
    private double _x;
    private double _y;
    private double _headingDeg;
    private double _steeringAngleDeg;
    private double _longitudinalAcceleration;
    private double _lateralAcceleration;
    private double _yawRateDegPerSec;
    private double _throttle;
    private double _brake;
    private double _steeringInput;
    private bool _paused;

    private double _startX;
    private double _startY;
    private double _startHeadingDeg;

    public VehicleSimulator(UdpSender udpSender)
    {
        _udpSender = udpSender ?? throw new ArgumentNullException(nameof(udpSender));
        Current = this;
    }

    /// <summary>设置复位起点（赛道起点）。</summary>
    public void SetStartPose(double x, double y, double headingDeg)
    {
        lock (_stateLock)
        {
            _startX = x;
            _startY = y;
            _startHeadingDeg = NormalizeHeading(headingDeg);
        }
    }

    /// <summary>
    /// throttle/brake: 0~1；steering: +1 = 左，-1 = 右。
    /// </summary>
    public void SetControls(double throttle, double brake, double steering)
    {
        lock (_stateLock)
        {
            _throttle = Math.Clamp(throttle, 0.0, 1.0);
            _brake = Math.Clamp(brake, 0.0, 1.0);
            _steeringInput = Math.Clamp(steering, -1.0, 1.0);
        }
    }

    public bool IsPaused
    {
        get { lock (_stateLock) { return _paused; } }
        set { lock (_stateLock) { _paused = value; } }
    }

    public void ReleaseControls()
    {
        lock (_stateLock)
        {
            _throttle = 0.0;
            _brake = 0.0;
            _steeringInput = 0.0;
        }
    }

    public void ResetVehicle()
    {
        lock (_stateLock)
        {
            _x = _startX;
            _y = _startY;
            _headingDeg = _startHeadingDeg;
            _steeringAngleDeg = 0.0;
            _speedMps = 0.0;
            _longitudinalAcceleration = 0.0;
            _lateralAcceleration = 0.0;
            _yawRateDegPerSec = 0.0;
            _throttle = 0.0;
            _brake = 0.0;
            _steeringInput = 0.0;
        }
    }

    public VehicleState GetState()
    {
        lock (_stateLock)
        {
            return new VehicleState
            {
                X = _x,
                Y = _y,
                SpeedMps = _speedMps,
                HeadingDeg = _headingDeg,
                SteeringAngleDeg = _steeringAngleDeg,
                LongitudinalAcceleration = _longitudinalAcceleration,
                LateralAcceleration = _lateralAcceleration,
                YawRateDegPerSec = _yawRateDegPerSec,
                Throttle = _throttle,
                Brake = _brake
            };
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        const int frequencyHz = 100;
        var period = TimeSpan.FromMilliseconds(1000.0 / frequencyHz);

        _stopwatch.Restart();
        var lastSeconds = _stopwatch.Elapsed.TotalSeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _stopwatch.Elapsed.TotalSeconds;
            var dt = now - lastSeconds;
            lastSeconds = now;

            if (dt <= 0)
            {
                dt = period.TotalSeconds;
            }
            else if (dt > 0.05)
            {
                dt = 0.05;
            }

            bool paused;
            lock (_stateLock)
            {
                paused = _paused;
            }

            if (!paused)
            {
                Step(dt);
            }

            // 暂停时仍发当前包，保持主窗链路活着
            await SendCurrentPacket().ConfigureAwait(false);

            try
            {
                await Task.Delay(period, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Step(double dt)
    {
        double throttle;
        double brake;
        double steeringInput;

        lock (_stateLock)
        {
            throttle = _throttle;
            brake = _brake;
            steeringInput = _steeringInput;

            var targetSteering = steeringInput * MaxSteeringAngleDeg;
            const double steeringResponse = 7.0;
            var steeringDifference = targetSteering - _steeringAngleDeg;
            _steeringAngleDeg += steeringDifference * Math.Clamp(steeringResponse * dt, 0.0, 1.0);

            var accel = throttle * MaxAcceleration - brake * MaxBrakeDeceleration;
            accel -= RollingResistance * Math.Sign(_speedMps);
            accel -= AeroDragCoefficient * _speedMps * Math.Abs(_speedMps);

            _longitudinalAcceleration = accel;
            _speedMps += accel * dt;

            if (brake > 0.05 && Math.Abs(_speedMps) < 0.05)
            {
                _speedMps = 0.0;
                _longitudinalAcceleration = 0.0;
            }

            var steeringRad = _steeringAngleDeg * Math.PI / 180.0;
            double yawRateRad;
            if (Math.Abs(_speedMps) > 0.05)
            {
                yawRateRad = _speedMps / WheelBase * Math.Tan(steeringRad);
            }
            else
            {
                yawRateRad = 0.0;
            }

            _yawRateDegPerSec = yawRateRad * 180.0 / Math.PI;
            _headingDeg = NormalizeHeading(_headingDeg + _yawRateDegPerSec * dt);
            _lateralAcceleration = _speedMps * yawRateRad;

            var headingRad = _headingDeg * Math.PI / 180.0;
            var velocityX = _speedMps * Math.Cos(headingRad);
            var velocityY = _speedMps * Math.Sin(headingRad);
            _x += velocityX * dt;
            _y += velocityY * dt;
        }
    }

    private async Task SendCurrentPacket()
    {
        VehicleState state;
        lock (_stateLock)
        {
            state = new VehicleState
            {
                X = _x,
                Y = _y,
                SpeedMps = _speedMps,
                HeadingDeg = _headingDeg,
                SteeringAngleDeg = _steeringAngleDeg,
                LongitudinalAcceleration = _longitudinalAcceleration,
                LateralAcceleration = _lateralAcceleration,
                YawRateDegPerSec = _yawRateDegPerSec,
                Throttle = _throttle,
                Brake = _brake
            };
        }

        const double originLatitude = 31.2304;
        const double originLongitude = 121.4737;
        const double metersPerDegreeLatitude = 111320.0;

        var latitude = originLatitude + state.X / metersPerDegreeLatitude;
        var metersPerDegreeLongitude =
            metersPerDegreeLatitude * Math.Cos(originLatitude * Math.PI / 180.0);
        var longitude = originLongitude + state.Y / metersPerDegreeLongitude;

        var packet = new UdpPacket
        {
            Version = 1,
            Sequence = _sequence++,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SpeedKph = state.SpeedMps * 3.6,
            LongitudinalAcceleration = state.LongitudinalAcceleration,
            LateralAcceleration = state.LateralAcceleration,
            VerticalAcceleration = 0.0,
            YawRate = state.YawRateDegPerSec,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = 10.0,
            Heading = state.HeadingDeg
        };

        await _udpSender.SendAsync(packet, CancellationToken.None).ConfigureAwait(false);
    }

    private static double NormalizeHeading(double heading)
    {
        while (heading >= 360.0)
        {
            heading -= 360.0;
        }

        while (heading < 0.0)
        {
            heading += 360.0;
        }

        return heading;
    }
}

public sealed class VehicleState
{
    public double X { get; init; }
    public double Y { get; init; }
    public double SpeedMps { get; init; }
    public double HeadingDeg { get; init; }
    public double SteeringAngleDeg { get; init; }
    public double LongitudinalAcceleration { get; init; }
    public double LateralAcceleration { get; init; }
    public double YawRateDegPerSec { get; init; }
    public double Throttle { get; init; }
    public double Brake { get; init; }
}

