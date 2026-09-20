namespace MTS2000.Core.Protocol;

/// <summary>
/// The byte-level duplex link <see cref="RadioProgrammingSession"/> talks over. Abstracted so
/// the session logic (retries, echo handling, frame parsing) can be exercised against an
/// in-memory fake radio in tests without a real serial port or hardware.
/// </summary>
public interface ISerialTransport : IDisposable
{
    bool DtrEnable { get; set; }
    bool RtsEnable { get; set; }
    int BytesToRead { get; }

    void Write(byte[] buffer, int offset, int count);
    int Read(byte[] buffer, int offset, int count);
    int ReadByte();
    void DiscardInBuffer();
    void DiscardOutBuffer();
}
