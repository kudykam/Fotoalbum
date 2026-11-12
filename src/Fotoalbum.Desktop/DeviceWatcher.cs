using System.Runtime.InteropServices;

namespace Fotoalbum.Desktop;

/// <summary>Neviditelné okno, které od Windows dostává zprávu o připojení nového disku (karty).</summary>
internal sealed class DeviceWatcher : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastVolume
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public int UnitMask;
        public short Flags;
    }

    public event Action<string>? DriveArrived;

    public DeviceWatcher() => CreateHandle(new CreateParams { Caption = "Fotokronikář – hlídání karet" });

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE && (int)m.WParam == DBT_DEVICEARRIVAL && m.LParam != IntPtr.Zero)
        {
            var hdr = Marshal.PtrToStructure<DevBroadcastVolume>(m.LParam);
            if (hdr.DeviceType == DBT_DEVTYP_VOLUME)
            {
                for (var i = 0; i < 26; i++)
                    if ((hdr.UnitMask & (1 << i)) != 0) DriveArrived?.Invoke($"{(char)('A' + i)}:\\");
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
