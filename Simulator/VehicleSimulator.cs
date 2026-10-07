using System.Diagnostics;
using Chassis_Master_Test_Suite.Communication;

namespace Chassis_Master_Test_Suite.Simulator;

/// <summary>
/// CMTS 人工驾驶车辆模拟器。
///
/// 100 Hz 车辆动态计算 + UDP 输出。
///
/// ============================================================
/// 坐标系约定（已用真实 WPF 3D 渲染实测确认）
/// ============================================================
///
/// X = 车辆左侧
/// Y = 高度方向
/// Z = 车辆前进方向（Heading = 0° 时）
///
/// 注意：
/// 三维场景里 X 轴的正方向出现在屏幕左侧。
/// 这一点已经实测验证，不是推测。
///
/// Heading：
///
/// 0°   = +Z 正前方
/// +    = 左转
/// -    = 右转
///
/// 车头方向向量：
///
/// forward = ( sin(Heading), cos(Heading) )
///
/// 这个定义必须与以下三处保持一致：
///
/// 1. 车辆模型绕 +Y 轴的旋转
/// 2. 位置积分
/// 3. 摄像机跟随
///
/// 任何一处用了 -sin(Heading)，
/// 都会导致「车头朝左、车却向右走」这类错误。
///
/// 控制方式：
///
/// W       加速
/// S       制动
/// A       左转
/// D       右转
/// Space   紧急制动
/// R       复位
/// </summary>
public sealed class VehicleSimulator
{
    private readonly UdpSender _udpSender;

    private readonly Stopwatch _stopwatch = new();

    private readonly object _stateLock = new();

    private long _sequence;

    // ============================================================
    // 当前 Simulator 实例
    // ============================================================

    public static VehicleSimulator? Current { get; private set; }

    // ============================================================
    // 车辆基础参数
    // ============================================================

    private const double WheelBase = 2.80;

    private const double MaxSteeringAngleDeg = 32.0;

    private const double MaxAcceleration = 4.0;

    private const double MaxBrakeDeceleration = 8.0;

    private const double RollingResistance = 0.12;

    private const double AeroDragCoefficient = 0.0025;

    // ============================================================
    // 车辆状态
    // ============================================================

    private double _speedMps;

    private double _x;

    private double _z;

    /*
     * Heading 定义：
     *
     * 0°   = +Z
     * +    = 左转
     * -    = 右转
     */
    private double _headingDeg = 0.0;

    /*
     * 前轮转角，采用 SAE 约定：
     *
     * + = 左转
     * - = 右转
     *
     * 与 Heading（+ = 左）保持同一个方向约定，
     * 这样前轮视觉、航向旋转、速度方向三者不会互相打架。
     */
    private double _steeringAngleDeg;

    private double _longitudinalAcceleration;

    private double _lateralAcceleration;

    private double _yawRateDegPerSec;

    // ============================================================
    // 驾驶控制量
    // ============================================================

    private double _throttle;

    private double _brake;

    /*
     * steering:
     *
     * -1 = 右
     * +1 = 左
     *
     * 与外部 SetControls 的约定一致，
     * 也与 Heading（+ = 左）一致。
     */
    private double _steeringInput;

    // ============================================================
    // 构造函数
    // ============================================================

    public VehicleSimulator(UdpSender udpSender)
    {
        _udpSender =
            udpSender ??
            throw new ArgumentNullException(
                nameof(udpSender));

        Current = this;
    }

    // ============================================================
    // 控制接口
    // ============================================================

    /// <summary>
    /// 设置驾驶员控制量。
    ///
    /// throttle:
    /// 0 ~ 1
    ///
    /// brake:
    /// 0 ~ 1
    ///
    /// steering:
    /// -1 = 右
    /// +1 = 左
    ///
    /// 注意：
    /// steering 采用「+ = 左，- = 右」，
    /// 与 Heading（+ = 左）以及内部
    /// _steeringAngleDeg 使用同一个方向约定，
    /// 因此内部不做任何符号翻转。
    /// </summary>
    public void SetControls(
        double throttle,
        double brake,
        double steering)
    {
        lock (_stateLock)
        {
            _throttle =
                Math.Clamp(
                    throttle,
                    0.0,
                    1.0);

            _brake =
                Math.Clamp(
                    brake,
                    0.0,
                    1.0);

            // 外部接口：+ = 左，- = 右
            // 内部角度：+ = 左，- = 右
            //
            // 两者约定一致，这里只做限幅，不做符号翻转。
            _steeringInput =
                Math.Clamp(
                    steering,
                    -1.0,
                    1.0);
        }
    }

    /// <summary>
    /// 立即释放驾驶控制。
    /// </summary>
    public void ReleaseControls()
    {
        lock (_stateLock)
        {
            _throttle = 0.0;

            _brake = 0.0;

            _steeringInput = 0.0;
        }
    }

    // ============================================================
    // 车辆复位
    // ============================================================

    public void ResetVehicle()
    {
        lock (_stateLock)
        {
            _speedMps = 0.0;

            _x = 0.0;

            _z = 0.0;

            _headingDeg = 0.0;

            _steeringAngleDeg = 0.0;

            _longitudinalAcceleration = 0.0;

            _lateralAcceleration = 0.0;

            _yawRateDegPerSec = 0.0;

            _throttle = 0.0;

            _brake = 0.0;

            _steeringInput = 0.0;
        }
    }

    // ============================================================
    // 当前状态
    // ============================================================

    public VehicleState GetState()
    {
        lock (_stateLock)
        {
            return new VehicleState
            {
                X = _x,

                Z = _z,

                SpeedMps = _speedMps,

                HeadingDeg = _headingDeg,

                SteeringAngleDeg =
                    _steeringAngleDeg,

                LongitudinalAcceleration =
                    _longitudinalAcceleration,

                LateralAcceleration =
                    _lateralAcceleration,

                YawRateDegPerSec =
                    _yawRateDegPerSec
            };
        }
    }

    // ============================================================
    // 100 Hz 主循环
    // ============================================================

    public async Task RunAsync(
        CancellationToken cancellationToken)
    {
        const int frequencyHz = 100;
        const double dt = 1.0 / frequencyHz;

        const int periodMilliseconds = 10;

        _stopwatch.Restart();

        // ========================================================
        // 这里刻意不使用 PeriodicTimer + WaitForNextTickAsync(token)。
        //
        // 那个写法在取消时会抛 OperationCanceledException，
        // 而模拟器关闭是正常流程，
        // 不应该靠异常来做控制流，
        // 否则 Visual Studio 调试器会不断报告首次异常。
        //
        // 改为：
        //
        // 1. 循环前检查取消标志
        //    （Task.Delay 不传 token，因此不会抛异常）
        // 2. 关闭时最多多等一个周期（10 ms）
        //
        // 这样既不会抛异常，
        // 也不会占用 WPF UI Dispatcher。
        // ========================================================

        while (!cancellationToken.IsCancellationRequested)
        {
            UpdateVehicle(dt);

            await SendCurrentPacket().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // ====================================================
            // 注意：这里刻意使用不接收 CancellationToken 的 Delay。
            //
            // Task.Delay(ms, token) 在 token 已取消时会立即抛出
            // TaskCanceledException，
            // 那会在 Visual Studio 调试器里显示为首次异常。
            //
            // 关闭时最多多等一个周期（10 ms），
            // 但整个关闭过程不再产生任何异常。
            // ====================================================

            await Task.Delay(
                periodMilliseconds).ConfigureAwait(false);
        }
    }

    // ============================================================
    // 车辆动力学
    // ============================================================

    private void UpdateVehicle(double dt)
    {
        lock (_stateLock)
        {
            // ====================================================
            // 读取控制
            // ====================================================

            var throttle =
                _throttle;

            var brake =
                _brake;

            var steeringInput =
                _steeringInput;

            // ====================================================
            // 转向
            // ====================================================

            var targetSteering =
                steeringInput *
                MaxSteeringAngleDeg;

            const double steeringResponse = 7.0;

            var steeringDifference =
                targetSteering -
                _steeringAngleDeg;

            _steeringAngleDeg +=
                steeringDifference *
                Math.Clamp(
                    steeringResponse * dt,
                    0.0,
                    1.0);

            // ====================================================
            // 纵向动力学
            // ====================================================

            var driveAcceleration =
                throttle *
                MaxAcceleration;

            var brakeAcceleration =
                brake *
                MaxBrakeDeceleration;

            var rolling =
                _speedMps > 0.01
                    ? RollingResistance
                    : 0.0;

            var aerodynamicDrag =
                AeroDragCoefficient *
                _speedMps *
                _speedMps;

            var acceleration =
                driveAcceleration -
                brakeAcceleration -
                rolling -
                aerodynamicDrag;

            // ====================================================
            // 低速时防止车辆反向
            // ====================================================

            if (_speedMps <= 0.0 &&
                acceleration < 0.0)
            {
                acceleration = 0.0;
            }

            _longitudinalAcceleration =
                acceleration;

            _speedMps +=
                acceleration * dt;

            if (_speedMps < 0.0)
            {
                _speedMps = 0.0;
            }

            // ====================================================
            // 前轮转角
            // ====================================================

            var steeringRad =
                _steeringAngleDeg *
                Math.PI /
                180.0;

            // ====================================================
            // 车辆横摆运动
            //
            // 符号约定（已实测确认）：
            //
            // _steeringAngleDeg:
            // + = 左转
            // - = 右转
            //
            // Heading:
            // + = 左转
            // - = 右转
            //
            // 自行车模型：
            //
            // yawRate = v / L * tan(delta)
            //
            // delta > 0（左转）
            //     → yawRate > 0
            //     → Heading 增大
            //
            // Heading 增大在三维场景里是左转，
            // 因为车头方向 forward = (sin h, cos h)，
            // h 从 0° 增到 90° 时车头指向 +X，
            // 而 +X 在屏幕左侧（已实测）。
            //
            // 因此这里使用正号。
            // ====================================================

            double yawRateRad;

            if (_speedMps > 0.01)
            {
                yawRateRad =
                    _speedMps /
                    WheelBase *
                    Math.Tan(
                        steeringRad);
            }
            else
            {
                yawRateRad = 0.0;
            }

            // ====================================================
            // 横摆角速度
            // ====================================================

            _yawRateDegPerSec =
                yawRateRad *
                180.0 /
                Math.PI;

            // ====================================================
            // 航向积分
            //
            // + Heading = 左转
            // - Heading = 右转
            // ====================================================

            _headingDeg +=
                _yawRateDegPerSec *
                dt;

            _headingDeg =
                NormalizeHeading(
                    _headingDeg);

            // ====================================================
            // 横向加速度
            // ====================================================

            _lateralAcceleration =
                _speedMps *
                yawRateRad;

            // ====================================================
            // 世界坐标位置
            //
            // 车头方向向量（与车辆模型旋转、摄像机完全一致）：
            //
            // forward = ( sin(Heading), cos(Heading) )
            //
            // Heading = 0°   → forward = (0, 1)  → 向 +Z 前进
            // Heading = 90°  → forward = (1, 0)  → 向 +X 前进
            //
            // 三维场景实测确认：
            // +X 出现在屏幕左侧。
            //
            // 所以 Heading = +90° 时车辆向屏幕左侧行驶，
            // 与「车头朝左」的画面完全一致。
            //
            // ----------------------------------------------------
            // 历史问题记录：
            //
            // 这里曾经写成 velocityX = -_speedMps * sin(Heading)。
            //
            // 那个负号导致车辆的实际行驶方向
            // 与车头朝向完全相反（点积 = -1），
            // 表现为「车头左转、车却向右开」。
            //
            // 不要再加回负号。
            // ====================================================

            var headingRad =
                _headingDeg *
                Math.PI /
                180.0;

            var velocityX =
                _speedMps *
                Math.Sin(
                    headingRad);

            var velocityZ =
                _speedMps *
                Math.Cos(
                    headingRad);

            _x +=
                velocityX * dt;

            _z +=
                velocityZ * dt;
        }
    }

    // ============================================================
    // UDP 数据
    // ============================================================

    private async Task SendCurrentPacket()
    {
        VehicleState state;

        lock (_stateLock)
        {
            state = new VehicleState
            {
                X = _x,

                Z = _z,

                SpeedMps = _speedMps,

                HeadingDeg = _headingDeg,

                SteeringAngleDeg =
                    _steeringAngleDeg,

                LongitudinalAcceleration =
                    _longitudinalAcceleration,

                LateralAcceleration =
                    _lateralAcceleration,

                YawRateDegPerSec =
                    _yawRateDegPerSec
            };
        }

        // ========================================================
        // 本地坐标 -> 虚拟 GPS
        // ========================================================

        const double originLatitude =
            31.2304;

        const double originLongitude =
            121.4737;

        const double metersPerDegreeLatitude =
            111320.0;

        var latitude =
            originLatitude +
            state.Z /
            metersPerDegreeLatitude;

        var longitudeScale =
            Math.Cos(
                originLatitude *
                Math.PI /
                180.0);

        var metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            longitudeScale;

        var longitude =
            originLongitude +
            state.X /
            metersPerDegreeLongitude;

        // ========================================================
        // UDP Packet
        // ========================================================

        var packet = new UdpPacket
        {
            Version = 1,

            Sequence =
                _sequence++,

            Timestamp =
                _stopwatch.ElapsedMilliseconds,

            SpeedKph =
                state.SpeedMps * 3.6,

            LongitudinalAcceleration =
                state.LongitudinalAcceleration,

            LateralAcceleration =
                state.LateralAcceleration,

            VerticalAcceleration =
                0.0,

            YawRate =
                state.YawRateDegPerSec,

            Latitude =
                latitude,

            Longitude =
                longitude,

            Altitude =
                10.0,

            Heading =
                state.HeadingDeg
        };

        await _udpSender.SendAsync(
            packet,
            CancellationToken.None);
    }

    // ============================================================
    // Heading 归一化
    // ============================================================

    private static double NormalizeHeading(
        double heading)
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

/// <summary>
/// Simulator 当前车辆状态。
/// </summary>
public sealed class VehicleState
{
    public double X { get; init; }

    public double Z { get; init; }

    public double SpeedMps { get; init; }

    public double HeadingDeg { get; init; }

    public double SteeringAngleDeg { get; init; }

    public double LongitudinalAcceleration
    {
        get;
        init;
    }

    public double LateralAcceleration
    {
        get;
        init;
    }

    public double YawRateDegPerSec
    {
        get;
        init;
    }
}