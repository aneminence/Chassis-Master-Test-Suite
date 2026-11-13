using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace ChassisMasterTestSuite.RenderingTests;

public class WebViewRuntimeTests
{
    [Fact]
    public void CompositionControl_Loaded_ResolvesWindowsRuntimeDependencies()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                using var browser = new WebView2CompositionControl();
                // Exercise the same Loaded handler that crashed startup, without
                // opening a window, initializing a browser profile or fetching tiles.
                browser.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Composition control load timed out.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
