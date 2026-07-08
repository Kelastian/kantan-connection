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

    [Fact]
    public async Task SendFrameAsync_ViewerReceivesFrameWithSameBytesAndMetadata()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-5", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var hostConnectedTcs = NewTcs<bool>();
        var frameReceivedTcs = NewTcs<CapturedFrame>();
        host.Connected += (_, _) => hostConnectedTcs.TrySetResult(true);
        viewer.FrameReceived += (_, frame) => frameReceivedTcs.TrySetResult(frame);

        host.Start();
        viewer.Start();
        await WaitWithTimeoutAsync(hostConnectedTcs.Task);

        var sentFrame = new CapturedFrame
        {
            EncodedBytes = [1, 2, 3, 4, 5],
            WidthPixels = TestScreenInfo.WidthPixels,
            HeightPixels = TestScreenInfo.HeightPixels,
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };
        await host.SendFrameAsync(sentFrame);

        var receivedFrame = await WaitWithTimeoutAsync(frameReceivedTcs.Task);

        Assert.Equal(sentFrame.EncodedBytes, receivedFrame.EncodedBytes);
        Assert.Equal(sentFrame.WidthPixels, receivedFrame.WidthPixels);
        Assert.Equal(sentFrame.HeightPixels, receivedFrame.HeightPixels);
        Assert.Equal(sentFrame.CapturedAtUtc, receivedFrame.CapturedAtUtc);
    }

    [Fact]
    public async Task SendFrameAsync_ManyFramesSentRapidly_AllArriveUncorrupted()
    {
        // Ejercita el semáforo de escritura de HostSession: manda muchos frames "pegados"
        // (sin esperar cada uno) para forzar que WriteFramedAsync los serialice
        // correctamente en vez de dejar que sus bytes se entrelacen en el socket.
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-6", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var hostConnectedTcs = NewTcs<bool>();
        host.Connected += (_, _) => hostConnectedTcs.TrySetResult(true);

        const int frameCount = 50;
        var receivedFrames = new List<CapturedFrame>();
        var allReceivedTcs = NewTcs<bool>();
        viewer.FrameReceived += (_, frame) =>
        {
            receivedFrames.Add(frame);
            if (receivedFrames.Count == frameCount)
            {
                allReceivedTcs.TrySetResult(true);
            }
        };

        host.Start();
        viewer.Start();
        await WaitWithTimeoutAsync(hostConnectedTcs.Task);

        var sendTasks = Enumerable.Range(0, frameCount).Select(i => host.SendFrameAsync(new CapturedFrame
        {
            EncodedBytes = [(byte)i, (byte)(i + 1)],
            WidthPixels = TestScreenInfo.WidthPixels,
            HeightPixels = TestScreenInfo.HeightPixels,
            CapturedAtUtc = DateTimeOffset.UtcNow,
        }));
        await Task.WhenAll(sendTasks);

        await WaitWithTimeoutAsync(allReceivedTcs.Task, timeoutSeconds: 10);

        Assert.Equal(frameCount, receivedFrames.Count);
        // Cada frame trae un byte índice distinto (0..49); si el framing se corrompiera,
        // estos valores no formarían el conjunto completo y ordenado esperado.
        var indices = receivedFrames.Select(f => (int)f.EncodedBytes[0]).OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(0, frameCount), indices);
    }

    [Fact]
    public async Task SendInputEventAsync_MouseMove_HostReceivesEventWithSameNormalizedCoordinates()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-7", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var hostConnectedTcs = NewTcs<bool>();
        var inputReceivedTcs = NewTcs<InputEvent>();
        host.Connected += (_, _) => hostConnectedTcs.TrySetResult(true);
        host.InputEventReceived += (_, inputEvent) => inputReceivedTcs.TrySetResult(inputEvent);

        host.Start();
        viewer.Start();
        await WaitWithTimeoutAsync(hostConnectedTcs.Task);

        var sentEvent = new InputEvent
        {
            Kind = InputEventKind.MouseMove,
            NormalizedX = 0.25,
            NormalizedY = 0.75,
        };
        await viewer.SendInputEventAsync(sentEvent);

        var receivedEvent = await WaitWithTimeoutAsync(inputReceivedTcs.Task);

        Assert.Equal(InputEventKind.MouseMove, receivedEvent.Kind);
        Assert.Equal(sentEvent.NormalizedX, receivedEvent.NormalizedX);
        Assert.Equal(sentEvent.NormalizedY, receivedEvent.NormalizedY);
    }

    [Fact]
    public async Task SendInputEventAsync_KeyDown_HostReceivesEventWithSameVirtualKeyCode()
    {
        var port = GetRandomFreeTcpPort();
        await using var host = new HostSession("4829", TestScreenInfo, port);
        await using var viewer = new ViewerSession("127.0.0.1", port, "viewer-8", "PC-NIETO")
        {
            RequestPinFromUserAsync = _ => Task.FromResult("4829"),
        };

        var hostConnectedTcs = NewTcs<bool>();
        var inputReceivedTcs = NewTcs<InputEvent>();
        host.Connected += (_, _) => hostConnectedTcs.TrySetResult(true);
        host.InputEventReceived += (_, inputEvent) => inputReceivedTcs.TrySetResult(inputEvent);

        host.Start();
        viewer.Start();
        await WaitWithTimeoutAsync(hostConnectedTcs.Task);

        const int virtualKeyCodeForLetterA = 0x41;
        var sentEvent = new InputEvent { Kind = InputEventKind.KeyDown, VirtualKeyCode = virtualKeyCodeForLetterA };
        await viewer.SendInputEventAsync(sentEvent);

        var receivedEvent = await WaitWithTimeoutAsync(inputReceivedTcs.Task);

        Assert.Equal(InputEventKind.KeyDown, receivedEvent.Kind);
        Assert.Equal(virtualKeyCodeForLetterA, receivedEvent.VirtualKeyCode);
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
