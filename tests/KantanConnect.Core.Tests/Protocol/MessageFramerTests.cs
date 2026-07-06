using System.Text;
using System.Text.Json;
using KantanConnect.Core.Protocol;

namespace KantanConnect.Core.Tests.Protocol;

public class MessageFramerTests
{
    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsTypeAndPayload()
    {
        using var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("""{"pin":"1234"}""");

        await MessageFramer.WriteAsync(stream, MessageType.PinAttempt, payload);
        stream.Position = 0;

        var message = await MessageFramer.ReadAsync(stream);

        Assert.NotNull(message);
        Assert.Equal(MessageType.PinAttempt, message!.Value.Type);
        Assert.Equal(payload, message.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_MultipleMessagesBackToBack_ReadsEachOneCorrectly()
    {
        using var stream = new MemoryStream();

        await MessageFramer.WriteAsync(stream, MessageType.Ping, []);
        await MessageFramer.WriteAsync(stream, MessageType.Pong, []);
        await MessageFramer.WriteAsync(stream, MessageType.Bye, "adios"u8.ToArray());

        stream.Position = 0;

        var first = await MessageFramer.ReadAsync(stream);
        var second = await MessageFramer.ReadAsync(stream);
        var third = await MessageFramer.ReadAsync(stream);

        Assert.Equal(MessageType.Ping, first!.Value.Type);
        Assert.Equal(MessageType.Pong, second!.Value.Type);
        Assert.Equal(MessageType.Bye, third!.Value.Type);
        Assert.Equal("adios"u8.ToArray(), third.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var message = await MessageFramer.ReadAsync(stream);

        Assert.Null(message);
    }

    [Fact]
    public async Task ReadAsync_StreamClosedMidMessage_ThrowsEndOfStream()
    {
        using var stream = new MemoryStream();
        await MessageFramer.WriteAsync(stream, MessageType.FrameData, new byte[100]);

        // Truncamos el stream a la mitad para simular una conexión cortada a medio mensaje.
        var truncated = new MemoryStream(stream.ToArray()[..50]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => MessageFramer.ReadAsync(truncated));
    }

    [Fact]
    public async Task ReadAsync_PayloadLargerThanMax_ThrowsInvalidData()
    {
        // Escribimos manualmente un header con una longitud absurda, sin el payload real.
        using var stream = new MemoryStream();
        var header = new byte[5];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, MessageFramer.MaxPayloadBytes + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => MessageFramer.ReadAsync(stream));
    }

    [Fact]
    public async Task WriteAsync_JsonPayload_CanBeDeserializedBack()
    {
        using var stream = new MemoryStream();
        var original = new { pin = "4829" };
        var json = JsonSerializer.SerializeToUtf8Bytes(original);

        await MessageFramer.WriteAsync(stream, MessageType.PinAttempt, json);
        stream.Position = 0;

        var message = await MessageFramer.ReadAsync(stream);
        var roundTripped = JsonSerializer.Deserialize<JsonElement>(message!.Value.Payload);

        Assert.Equal("4829", roundTripped.GetProperty("pin").GetString());
    }
}
