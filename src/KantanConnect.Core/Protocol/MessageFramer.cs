using System.Buffers.Binary;

namespace KantanConnect.Core.Protocol;

/// <summary>
/// Resultado de leer un mensaje completo desde el stream: su tipo y el payload crudo
/// (JSON para mensajes de control, bytes de imagen para <see cref="MessageType.FrameData"/>).
/// </summary>
public readonly record struct FramedMessage(MessageType Type, byte[] Payload);

/// <summary>
/// Convierte mensajes en frames binarios sobre un <see cref="Stream"/> TCP y viceversa.
///
/// TCP es un flujo de bytes continuo: no sabe dónde termina un "mensaje" y empieza el
/// siguiente. Por eso cada mensaje se envuelve así:
///
///   [4 bytes: longitud del resto, Big Endian] [1 byte: MessageType] [payload...]
///
/// Al leer, primero se leen esos 4 bytes para saber exactamente cuántos bytes más hay
/// que esperar, y se bloquea hasta tenerlos todos (ver <see cref="ReadExactAsync"/>).
/// Así, tanto un mensaje de control pequeño (JSON) como un frame JPEG de varios KB
/// viajan por el mismo canal sin mezclarse ni cortarse a la mitad.
/// </summary>
public static class MessageFramer
{
    /// <summary>Límite defensivo: ningún mensaje individual debería superar los 32 MB.</summary>
    public const int MaxPayloadBytes = 32 * 1024 * 1024;

    private const int LengthPrefixBytes = sizeof(int);
    private const int TypeBytes = sizeof(byte);

    public static async Task WriteAsync(
        Stream stream,
        MessageType type,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var totalLength = TypeBytes + payload.Length;
        var header = new byte[LengthPrefixBytes + TypeBytes];

        BinaryPrimitives.WriteInt32BigEndian(header, totalLength);
        header[LengthPrefixBytes] = (byte)type;

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lee un mensaje completo. Devuelve <c>null</c> si el stream se cerró de forma
    /// ordenada justo al inicio de un mensaje (fin de conexión esperado).
    /// </summary>
    public static async Task<FramedMessage?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var lengthBuffer = new byte[LengthPrefixBytes];
        var readFirstByte = await ReadExactAsync(stream, lengthBuffer, allowZeroBytesRead: true, cancellationToken)
            .ConfigureAwait(false);

        if (!readFirstByte)
        {
            return null;
        }

        var totalLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (totalLength < TypeBytes || totalLength > MaxPayloadBytes)
        {
            throw new InvalidDataException(
                $"Longitud de mensaje fuera de rango: {totalLength} bytes (máximo {MaxPayloadBytes}).");
        }

        var body = new byte[totalLength];
        await ReadExactAsync(stream, body, allowZeroBytesRead: false, cancellationToken).ConfigureAwait(false);

        var type = (MessageType)body[0];
        var payload = body[TypeBytes..];

        return new FramedMessage(type, payload);
    }

    /// <summary>
    /// Llena <paramref name="buffer"/> por completo, reintentando lecturas parciales
    /// (TCP puede entregar los datos en pedazos más chicos del buffer solicitado).
    /// </summary>
    /// <param name="allowZeroBytesRead">
    /// Cuando es true, un cierre de conexión antes de leer ningún byte se trata como fin
    /// ordenado (devuelve false) en vez de excepción; es el caso de esperar el próximo mensaje.
    /// </param>
    private static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        bool allowZeroBytesRead,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowZeroBytesRead && offset == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("La conexión se cerró en medio de un mensaje.");
            }

            offset += read;
        }

        return true;
    }
}
