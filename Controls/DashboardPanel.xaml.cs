using System.Globalization;
using System.Windows.Controls;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// 实时数据仪表板。
///
/// 计划功能：
/// 用户可以自定义要查看的通道（当前为固定 5 项）。
///
/// 当前状态：
/// 显示 MainWindow 推送过来的实时数值。
/// </summary>
public partial class DashboardPanel : UserControl
{
    public DashboardPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更新实时数值显示。
    ///
    /// 由 MainWindow 的 UI 定时器调用，
    /// 保持在 UI 线程上。
    /// </summary>
    public void SetValues(
        double speedKph,
        double longitudinalAcceleration,
        double lateralAcceleration,
        double yawRate,
        double steeringAngleDeg)
    {
        SpeedValueText.Text =
            speedKph.ToString("F2", CultureInfo.InvariantCulture);

        LongitudinalAccelerationValueText.Text =
            longitudinalAcceleration.ToString("F2", CultureInfo.InvariantCulture);

        LateralAccelerationValueText.Text =
            lateralAcceleration.ToString("F2", CultureInfo.InvariantCulture);

        YawRateValueText.Text =
            yawRate.ToString("F2", CultureInfo.InvariantCulture);

        SteeringAngleValueText.Text =
            steeringAngleDeg.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 清空显示（尚无数据时）。
    /// </summary>
    public void Clear()
    {
        SpeedValueText.Text = "--";

        LongitudinalAccelerationValueText.Text = "--";

        LateralAccelerationValueText.Text = "--";

        YawRateValueText.Text = "--";

        SteeringAngleValueText.Text = "--";
    }
}
