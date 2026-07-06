using System.Net;
using System.Net.Sockets;
using KantanConnect.Core.Models;
using KantanConnect.Core.Session;

namespace KantanConnect.Core.Tests.Session;

/// <summary>
/// Pruebas de integración en loopback (127.0.0.1) entre <see cref="HostSession"/> y
/// <see cref="ViewerSession"/> reales, usando un puerto TCP aleatorio por test.
/// </summary>
public class HostViewerSessionTests
{
    private static int GetRandomFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static readonly ScreenInfo TestScreenInfo = new() { WidthPixels = 1920, HeightPixels = 1080 };

    [Fact]
    public async Task CorrectPinOnFirstAttempt_BothSidesReachConnected()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-1", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var hostConnectedTcs = NewTcs<bool>();
        var viewerConnectedTcs = NewTcs<bool>();
        host.Connected += (_, _) => hostConnectedTcs.TrySetResult(true);
        viewer.Connected += (_, _) => viewerConnectedTcs.TrySetResult(true);

        host.Start();
        viewer.Start();

        await WaitWithTimeoutAsync(hostConnectedTcs.Task);
        await WaitWithTimeoutAsync(viewerConnectedTcs.Task);
    }

    [Fact]
    public async Task WrongPinThenCorrectPin_EventuallyConnects()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);

        var callCount = 0;
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-2", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ =>
            {
                callCount++;
                return Task.FromResult(callCount == 1 ? "0000" : "4829");
            },
        };

        var viewerConnectedTcs = NewTcs<bool>();
        var pinRejectedTcs = NewTcs<int>();
        viewer.Connected += (_, _) => viewerConnectedTcs.TrySetResult(true);
        viewer.PinRejected += (_, e) => pinRejectedTcs.TrySetResult(e.RemainingAttempts);

        host.Start();
        viewer.Start();

        var remainingAfterFirstFail = await WaitWithTimeoutAsync(pinRejectedTcs.Task);
        Assert.Equal(2, remainingAfterFirstFail);

        await WaitWithTimeoutAsync(viewerConnectedTcs.Task);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task AllPinAttemptsWrong_SessionEndsWithAttemptsExhausted()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-3", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("0000"),
        };

        var hostEndedTcs = NewTcs<SessionEndReason>();
        var viewerEndedTcs = NewTcs<SessionEndReason>();
        host.SessionEnded += (_, e) => hostEndedTcs.TrySetResult(e.Reason);
        viewer.SessionEnded += (_, e) => viewerEndedTcs.TrySetResult(e.Reason);

        host.Start();
        viewer.Start();

        var hostReason = await WaitWithTimeoutAsync(hostEndedTcs.Task);
        var viewerReason = await WaitWithTimeoutAsync(viewerEndedTcs.Task);

        Assert.Equal(SessionEndReason.PinAttemptsExhausted, hostReason);
        Assert.Equal(SessionEndReason.PinAttemptsExhausted, viewerReason);
    }

    [Fact]
    public async Task ViewerReceivesHostScreenInfo_BeforePinNegotiation()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-4", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var screenInfoTcs = NewTcs<ScreenInfo>();
        viewer.ScreenInfoReceived += (_, info) => screenInfoTcs.TrySetResult(info);

        host.Start();
        viewer.Start();

        var receivedScreenInfo = await WaitWithTimeoutAsync(screenInfoTcs.Task);

        Assert.Equal(TestScreenInfo.WidthPixels, receivedScreenInfo.WidthPixels);
        Assert.Equal(TestScreenInfo.HeightPixels, receivedScreenInfo.HeightPixels);
    }

    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, int timeoutSeconds = 5)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (completed != task)
        {
            throw new TimeoutException($"La operación no completó dentro de {timeoutSeconds}s.");
        }

        return await task;
    }
}
