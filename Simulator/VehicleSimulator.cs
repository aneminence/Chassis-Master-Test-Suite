using System.Diagnostics;
using Chassis_Master_Test_Suite.Communication;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// CMTS driving simulator: soft steering + single-track (bicycle) yaw/sideslip, 100 Hz UDP.
/// Body/world: X forward (heading 0), Y left, Z up. Vehicle Z follows Spa track elevation.
/// </summary>
public sealed class VehicleSimulator
{
    private readonly UdpSender _udpSender;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _stateLock = new();
    private long _sequence;

    public static VehicleSimulator? Current { get; private set; }

    private const double WheelBase = 2.80;
    private const double DistanceToFrontAxle = 1.40;
    private const double DistanceToRearAxle = 1.40;
    private const double VehicleMass = 1450.0;
    private const double YawInertia = 2500.0;
    private const double FrontCorneringStiffness = 70000.0;
    private const double RearCorneringStiffness = 80000.0;
    private const double MaxSteeringAngleDeg = 28.0;
    private const double SteeringTimeConstant = 0.22;
    private const double WheelPowerWatts = 180000.0;
    private const double MaxDriveForce = 8500.0;
    private const double MaxBrakeDeceleration = 9.0;
    private const double RollingResistance = 0.14;
    private const double AeroDragCoefficient = 0.00028; // 0.5 * rho * CdA / mass

    private double _speedMps;
    private double _lateralVelocity;
    private double _yawRateRad;
    private double _x;
    private double _y;
    private double _z;
    private double _headingDeg;
    private double _steeringAngleDeg;
    private double _steeringCommandDeg;
    private double _longitudinalAcceleration;
    private double _lateralAcceleration;
    private double _verticalAcceleration;
    private double _yawRateDegPerSec;
    private double _pitchDeg;
    private double _throttle;
    private double _brake;
    private double _steeringInput;
    private bool _paused;
    private double _prevZ;
    private double _prevVz;
    private bool _hasPrevZ;
    private double _zFiltered;
    private double _pitchFiltered;
    private bool _heightFilterInitialized;
    private const double HeightFilterTau = 0.10;
    private const double PitchFilterTau = 0.14;

    private double _startX;
    private double _startY;
    private double _startZ;
    private double _startHeadingDeg;

    public VehicleSimulator(UdpSender udpSender)
    {
        _udpSender = udpSender ?? throw new ArgumentNullException(nameof(udpSender));
        Current = this;
    }

    public void SetStartPose(double x, double y, double z, double headingDeg)
    {
        lock (_stateLock)
        {
            _startX = x;
            _startY = y;
            _startZ = z;
            _startHeadingDeg = NormalizeHeading(headingDeg);
        }
    }

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
            _z = _startZ;
            _headingDeg = _startHeadingDeg;
            _steeringAngleDeg = 0.0;
            _steeringCommandDeg = 0.0;
            _speedMps = 0.0;
            _lateralVelocity = 0.0;
            _yawRateRad = 0.0;
            _longitudinalAcceleration = 0.0;
            _lateralAcceleration = 0.0;
            _verticalAcceleration = 0.0;
            _yawRateDegPerSec = 0.0;
            _pitchDeg = 0.0;
            _throttle = 0.0;
            _brake = 0.0;
            _steeringInput = 0.0;
            _hasPrevZ = false;
            _heightFilterInitialized = false;
            _zFiltered = _startZ;
            _pitchFiltered = 0.0;
        }
    }

    public VehicleState GetState()
    {
        lock (_stateLock)
        {
            return SnapshotUnlocked();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        const int frequencyHz = 100;
        var period = TimeSpan.FromMilliseconds(1000.0 / frequencyHz);
        _stopwatch.Restart();
        var lastSeconds = _stopwatch.Elapsed.TotalSeconds;
        var accumulated = 0.0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _stopwatch.Elapsed.TotalSeconds;
            var dt = now - lastSeconds;
            lastSeconds = now;
            if (dt <= 0) dt = period.TotalSeconds;
            else if (dt > 0.25) dt = 0.25;

            bool paused;
            lock (_stateLock) { paused = _paused; }
            if (!paused)
            {
                accumulated += dt;
                while (accumulated >= period.TotalSeconds)
                {
                    Step(period.TotalSeconds);
                    accumulated -= period.TotalSeconds;
                }
            }
            else accumulated = 0;
            await SendCurrentPacket().ConfigureAwait(false);

            try { await Task.Delay(period, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Step(double dt)
    {
        lock (_stateLock)
        {
            // Speed-dependent max steer (high speed locks less angle) + 1st-order filter.
            var speedKph = Math.Abs(_speedMps) * 3.6;
            var steerScale = 1.0 / (1.0 + Math.Pow(speedKph / 40.0, 2.0));
            var targetSteer = _steeringInput * MaxSteeringAngleDeg * steerScale;
            var alpha = 1.0 - Math.Exp(-dt / SteeringTimeConstant);
            _steeringCommandDeg += (targetSteer - _steeringCommandDeg) * alpha;
            _steeringAngleDeg = _steeringCommandDeg;

            // Generic sport sedan calibration, wheel power rather than constant acceleration.
            var driveForce = Math.Min(MaxDriveForce, WheelPowerWatts / Math.Max(_speedMps, 1.0));
            var accel = _throttle * driveForce / VehicleMass - _brake * MaxBrakeDeceleration;
            accel -= 9.81 * Math.Sin(_pitchDeg * Math.PI / 180.0);
            accel -= RollingResistance * Math.Sign(_speedMps == 0 ? _throttle - _brake : _speedMps);
            accel -= AeroDragCoefficient * _speedMps * Math.Abs(_speedMps);
            _longitudinalAcceleration = accel;
            // Brake is not reverse gear; crossing zero used to destabilize the lateral model.
            _speedMps = Math.Max(0.0, _speedMps + accel * dt);
            if (_brake > 0.05 && Math.Abs(_speedMps) < 0.08)
            {
                _speedMps = 0.0;
                _lateralVelocity = 0.0;
                _yawRateRad = 0.0;
                _longitudinalAcceleration = 0.0;
            }

            var u = Math.Max(Math.Abs(_speedMps), 0.5) * Math.Sign(_speedMps == 0 ? 1.0 : _speedMps);
            if (Math.Abs(_speedMps) < 0.35)
            {
                // Low-speed: damped bicycle → near kinematic.
                var delta = _steeringAngleDeg * Math.PI / 180.0;
                var yawTarget = _speedMps / WheelBase * Math.Tan(delta);
                _yawRateRad += (yawTarget - _yawRateRad) * Math.Clamp(6.0 * dt, 0, 1);
                _lateralVelocity *= Math.Exp(-8.0 * dt);
            }
            else
            {
                var delta = _steeringAngleDeg * Math.PI / 180.0;
                var a = DistanceToFrontAxle;
                var b = DistanceToRearAxle;
                // Backward Euler solves the coupled linear bicycle system.
                // Explicit Euler oscillates at low speed because damping scales as 1/u.
                // Quasi-static longitudinal load transfer and combined-slip force budget.
                var transfer = VehicleMass * _longitudinalAcceleration * 0.52 / WheelBase;
                var frontLoad = Math.Clamp(VehicleMass * 9.81 * b / WheelBase - transfer, 1000, VehicleMass * 9.81 - 1000);
                var rearLoad = VehicleMass * 9.81 - frontLoad;
                var longitudinalUsage = Math.Clamp(Math.Abs(_longitudinalAcceleration) / 10.3, 0, 0.98);
                var gripScale = Math.Sqrt(1 - longitudinalUsage * longitudinalUsage);
                var slipF = Math.Atan2(_lateralVelocity + a * _yawRateRad, u) - delta;
                var slipR = Math.Atan2(_lateralVelocity - b * _yawRateRad, u);
                double EffectiveStiffness(double stiffness, double slip, double load)
                {
                    var limit = 1.05 * load * gripScale;
                    var demand = stiffness * Math.Abs(slip);
                    return demand < 1e-6 ? stiffness : limit * Math.Tanh(demand / limit) / Math.Abs(slip);
                }
                var cf = EffectiveStiffness(FrontCorneringStiffness, slipF, frontLoad);
                var cr = EffectiveStiffness(RearCorneringStiffness, slipR, rearLoad);
                var a11 = -(cf + cr) / (VehicleMass * u);
                var a12 = (-a * cf + b * cr) / (VehicleMass * u) - u;
                var a21 = (-a * cf + b * cr) / (YawInertia * u);
                var a22 = -(a * a * cf + b * b * cr) / (YawInertia * u);
                var m11 = 1 - dt * a11;
                var m12 = -dt * a12;
                var m21 = -dt * a21;
                var m22 = 1 - dt * a22;
                var rhsV = _lateralVelocity + dt * cf * delta / VehicleMass;
                var rhsR = _yawRateRad + dt * a * cf * delta / YawInertia;
                var determinant = m11 * m22 - m12 * m21;
                _lateralVelocity = (rhsV * m22 - m12 * rhsR) / determinant;
                _yawRateRad = (m11 * rhsR - m21 * rhsV) / determinant;

                // Soft clamp wild rates
                _yawRateRad = Math.Clamp(_yawRateRad, -1.6, 1.6);
                _lateralVelocity = Math.Clamp(_lateralVelocity, -12.0, 12.0);
            }

            _yawRateDegPerSec = _yawRateRad * 180.0 / Math.PI;
            _headingDeg = NormalizeHeading(_headingDeg + _yawRateDegPerSec * dt);
            _lateralAcceleration = (_speedMps * _yawRateRad) + 0.15 * _lateralVelocity;

            var headingRad = _headingDeg * Math.PI / 180.0;
            var cos = Math.Cos(headingRad);
            var sin = Math.Sin(headingRad);
            // Body: +X forward, +Y left → world
            var vxWorld = _speedMps * cos - _lateralVelocity * sin;
            var vyWorld = _speedMps * sin + _lateralVelocity * cos;
            _x += vxWorld * dt;
            _y += vyWorld * dt;

            if (SpaTrackCenterline.TrySampleAt(_x, _y, out var groundZ, out var tangent, out _))
            {
                var horiz = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
                var pitchRaw = horiz > 1e-6
                    ? Math.Atan2(tangent.Z * (tangent.X * cos + tangent.Y * sin) / horiz, horiz) * 180.0 / Math.PI
                    : 0.0;

                if (!_heightFilterInitialized)
                {
                    _zFiltered = groundZ;
                    _pitchFiltered = pitchRaw;
                    _heightFilterInitialized = true;
                }
                else
                {
                    var az = 1.0 - Math.Exp(-dt / HeightFilterTau);
                    var ap = 1.0 - Math.Exp(-dt / PitchFilterTau);
                    _zFiltered += (groundZ - _zFiltered) * az;
                    _pitchFiltered += (pitchRaw - _pitchFiltered) * ap;
                }

                // Ground contact is a geometric constraint, not a delayed response.
                // Filtering height independently of XY sinks the car on climbs.
                _z = groundZ;
                _pitchDeg = _pitchFiltered;
            }

            if (_hasPrevZ)
            {
                var vz = (_z - _prevZ) / Math.Max(dt, 1e-4);
                _verticalAcceleration = (vz - _prevVz) / Math.Max(dt, 1e-4);
                _prevVz = vz;
            }
            else
            {
                _prevVz = 0;
                _verticalAcceleration = 0;
                _hasPrevZ = true;
            }

            _prevZ = _z;
        }
    }

    private VehicleState SnapshotUnlocked() => new()
    {
        X = _x,
        Y = _y,
        Z = _z,
        SpeedMps = _speedMps,
        HeadingDeg = _headingDeg,
        PitchDeg = _pitchDeg,
        SteeringAngleDeg = _steeringAngleDeg,
        LongitudinalAcceleration = _longitudinalAcceleration,
        LateralAcceleration = _lateralAcceleration,
        VerticalAcceleration = _verticalAcceleration,
        YawRateDegPerSec = _yawRateDegPerSec,
        Throttle = _throttle,
        Brake = _brake
    };

    private async Task SendCurrentPacket()
    {
        VehicleState state;
        lock (_stateLock) { state = SnapshotUnlocked(); }

        const double originLatitude = 31.2304;
        const double originLongitude = 121.4737;
        const double metersPerDegreeLatitude = 111320.0;
        var latitude = originLatitude + state.X / metersPerDegreeLatitude;
        var metersPerDegreeLongitude =
            metersPerDegreeLatitude * Math.Cos(originLatitude * Math.PI / 180.0);
        // X is north, Y is west (left at heading zero). GPS heading is clockwise.
        var longitude = originLongitude - state.Y / metersPerDegreeLongitude;

        var packet = new UdpPacket
        {
            Version = 1,
            Sequence = _sequence++,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SpeedKph = state.SpeedMps * 3.6,
            LongitudinalAcceleration = state.LongitudinalAcceleration,
            LateralAcceleration = state.LateralAcceleration,
            VerticalAcceleration = state.VerticalAcceleration,
            YawRate = state.YawRateDegPerSec,
            Latitude = latitude,
            Longitude = longitude,
            Altitude = 10.0 + state.Z,
            Heading = NormalizeHeading(-state.HeadingDeg)
        };

        await _udpSender.SendAsync(packet, CancellationToken.None).ConfigureAwait(false);
    }

    private static double NormalizeHeading(double heading)
    {
        while (heading >= 360.0) heading -= 360.0;
        while (heading < 0.0) heading += 360.0;
        return heading;
    }
}

public sealed class VehicleState
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double SpeedMps { get; init; }
    public double HeadingDeg { get; init; }
    public double PitchDeg { get; init; }
    public double SteeringAngleDeg { get; init; }
    public double LongitudinalAcceleration { get; init; }
    public double LateralAcceleration { get; init; }
    public double VerticalAcceleration { get; init; }
    public double YawRateDegPerSec { get; init; }
    public double Throttle { get; init; }
    public double Brake { get; init; }
}
