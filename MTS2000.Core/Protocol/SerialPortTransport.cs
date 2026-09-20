using System.IO.Ports;

namespace MTS2000.Core.Protocol;

/// <summary>Real hardware transport: a thin adapter over <see cref="SerialPort"/>.</summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string comPort, int baudRate = 9600)
    {
        _port = new SerialPort(comPort) { BaudRate = baudRate, ReadTimeout = 2500 };
        _port.Open();
    }

    public bool DtrEnable
    {
        get => _port.DtrEnable;
        set => _port.DtrEnable = value;
    }

    public bool RtsEnable
    {
        get => _port.RtsEnable;
        set => _port.RtsEnable = value;
    }

    public int BytesToRead => _port.BytesToRead;

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    public int ReadByte() => _port.ReadByte();

    public void DiscardInBuffer() => _port.DiscardInBuffer();

    public void DiscardOutBuffer() => _port.DiscardOutBuffer();

    public void Dispose()
    {
        _port.Close();
        _port.Dispose();
    }
}
