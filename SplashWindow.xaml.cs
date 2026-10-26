using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;

namespace Chassis_Master_Test_Suite;

public partial class SplashWindow : Window, IProgress<double>
{
    private const double DesignAspectRatio = 1672.0 / 941.0;
    private double _displayedProgress;

    public SplashWindow()
    {
        InitializeComponent();
        FitToWorkingArea();
        VersionText.Text = GetDisplayVersion();
    }

    public void Report(double value)
    {
        var target = Math.Clamp(value, _displayedProgress, 100.0);
        var animation = new DoubleAnimation
        {
            From = StartupProgressBar.Value,
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        _displayedProgress = target;
        StartupProgressBar.BeginAnimation(
            System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    public Task FadeOutAsync(TimeSpan duration)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = new Duration(duration),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => completion.TrySetResult();
        BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    private void FitToWorkingArea()
    {
        const double preferredWidth = 1000;
        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(preferredWidth, workArea.Width * 0.88);
        var height = width / DesignAspectRatio;

        if (height > workArea.Height * 0.88)
        {
            height = workArea.Height * 0.88;
            width = height * DesignAspectRatio;
        }

        Width = width;
        Height = height;
    }

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var version = informationalVersion?.Split('+', 2)[0];
        if (string.IsNullOrWhiteSpace(version))
            version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        return $"v{version}";
    }
}
