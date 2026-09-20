namespace MTS2000.Core.Protocol;

/// <summary>Control-bus frames needed to prepare an MTS2000-family radio for programming.</summary>
public static class ControlBusCommands
{
    public static ControlBusFrame EnterExtendedMode => new(0x00, 0x12, 0x01, 0x06);
    public static ControlBusFrame EnterProgrammingMode => new(0x01, 0x02, 0x00, 0x40);
    public static ControlBusFrame ResetRadio => new(0x00, 0x00, 0x01, 0x08);
    public static ControlBusFrame FirmwareVersionQuery => new(0x08, 0x00, 0x00, 0xC0, expectsReply: true);
}
