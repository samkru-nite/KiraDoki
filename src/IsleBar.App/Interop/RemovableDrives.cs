using System.Runtime.InteropServices;
using System.Text;
using IsleBar.Core.SystemWatch;
using Microsoft.Win32.SafeHandles;

namespace IsleBar.App.Interop;

/// <summary>
/// USB drives and memory cards for the USB card (TODO ④, 10-10): every ready drive that is removable, or fixed but on an external bus
/// (a USB hard disk, an SD card — asked once per letter with IOCTL_STORAGE_QUERY_PROPERTY), never the system drive. Phones (MTP)
/// have no drive letter, so they never show.
/// <para><see cref="Eject"/> = Windows' own "safely remove": from the volume to its disk (device number), then up to the first device
/// that says it is removable, and CM_Request_Device_Eject on it — a program holding a file vetoes it. No removable device found →
/// not ejected (never a whole card reader or hub by guess).</para>
/// </summary>
internal static class RemovableDrives
{
    private static readonly Dictionary<string, bool> External = new(StringComparer.OrdinalIgnoreCase);   // fixed drives: on an external bus?

    public static List<PluggedDrive> List()
    {
        var list = new List<PluggedDrive>();
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var root = drive.Name;
                if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed)
                    || string.Equals(root, system, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                seen.Add(root);
                if (drive.DriveType == DriveType.Fixed && !IsExternal(root))
                {
                    continue;
                }

                if (!drive.IsReady)
                {
                    continue;   // an empty card reader slot
                }

                list.Add(new PluggedDrive(root, drive.VolumeLabel, drive.TotalSize, drive.AvailableFreeSpace, Serial(root)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // pulled out while being read: gone next time
            }
        }

        foreach (var gone in External.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            External.Remove(gone);   // another disk may get the letter later
        }

        return list;
    }

    private static bool IsExternal(string root)
    {
        if (External.TryGetValue(root, out var known))
        {
            return known;
        }

        var bus = BusOf(root);
        var external = bus is BusUsb or BusSd or BusMmc or Bus1394;
        External[root] = external;
        TaskbarHost.Log($"drive: {root[..2]} is fixed, bus {bus?.ToString() ?? "?"} → {(external ? "external" : "internal, left out")}");
        return external;
    }

    private const int BusUsb = 7, Bus1394 = 4, BusSd = 0xC, BusMmc = 0xD;

    private static int? BusOf(string root)
    {
        using var volume = OpenVolume(root);
        if (volume.IsInvalid)
        {
            return null;
        }

        var query = new byte[12];   // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0), PropertyStandardQuery (0)
        var output = new byte[1024];
        return DeviceIoControl(volume, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out var got, IntPtr.Zero) && got >= 32
            ? BitConverter.ToInt32(output, 28)   // STORAGE_DEVICE_DESCRIPTOR.BusType
            : null;
    }

    private static uint Serial(string root)
        => GetVolumeInformation(root, null, 0, out var serial, out _, out _, null, 0) ? serial : 0;

    /// <summary>
    /// Safely removes the drive. <c>Why</c> null = Windows took it away; else why not, for the log ("veto 6 \\?\…", "no removable
    /// device"). <c>Device</c> = the device asked to go ("USB\VID_…"), to find who refused it in the system log
    /// (<see cref="EjectBlocker"/>). Takes up to a couple of seconds: call off the UI thread.
    /// </summary>
    public static (string? Why, string? Device) Eject(string root)
    {
        if (DiskNumber(root) is not { } number)
        {
            return ("no disk number", null);
        }

        if (DiskDevice(number) is not { } disk)
        {
            return ("disk not found", null);
        }

        // up from the disk to the first device that can be removed (the USB stick itself; a memory card's slot isn't one)
        var device = disk;
        for (var hops = 0; hops < 4 && !IsRemovable(device); hops++)
        {
            if (CM_Get_Parent(out var parent, device, 0) != 0)
            {
                return ("no removable device", null);
            }

            device = parent;
        }

        if (!IsRemovable(device))
        {
            return ("no removable device", null);
        }

        var id = new StringBuilder(400);
        var name = CM_Get_Device_IDW(device, id, id.Capacity, 0) == 0 ? id.ToString() : null;
        string? why = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var veto = new StringBuilder(260);
            var result = CM_Request_Device_EjectW(device, out var vetoType, veto, veto.Capacity, 0);
            if (result == 0 && vetoType == 0)
            {
                return (null, name);
            }

            why = $"veto {vetoType} {veto} (result {result})";
            Thread.Sleep(300);   // a scan that just let go: try again
        }

        return (why, name);
    }

    private static (uint Type, uint Number)? DiskNumber(string root)
    {
        using var volume = OpenVolume(root);
        var output = new byte[12];   // STORAGE_DEVICE_NUMBER: DeviceType, DeviceNumber, PartitionNumber
        return !volume.IsInvalid && DeviceIoControl(volume, IoctlStorageGetDeviceNumber, null, 0, output, output.Length, out var got, IntPtr.Zero) && got >= 8
            ? (BitConverter.ToUInt32(output, 0), BitConverter.ToUInt32(output, 4))
            : null;
    }

    private static uint? DiskDevice((uint Type, uint Number) wanted)
    {
        var guid = DiskInterface;
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                {
                    return null;
                }

                var info = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var need, ref info);
                if (need <= 0)
                {
                    continue;
                }

                var detail = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);   // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, need, out _, ref info))
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null)
                    {
                        continue;
                    }

                    using var disk = CreateFile(path, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    var output = new byte[12];
                    if (!disk.IsInvalid && DeviceIoControl(disk, IoctlStorageGetDeviceNumber, null, 0, output, output.Length, out var got, IntPtr.Zero)
                        && got >= 8 && BitConverter.ToUInt32(output, 0) == wanted.Type && BitConverter.ToUInt32(output, 4) == wanted.Number)
                    {
                        return info.DevInst;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static bool IsRemovable(uint device)
    {
        var size = 4;
        return CM_Get_DevNode_Registry_PropertyW(device, CmDrpCapabilities, out _, out var caps, ref size, 0) == 0
               && (caps & CmDevcapRemovable) != 0;
    }

    private static SafeFileHandle OpenVolume(string root)
        => CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const uint IoctlStorageGetDeviceNumber = 0x2D1080;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const int CmDrpCapabilities = 0x10;
    private const uint CmDevcapRemovable = 0x4;
    private static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");   // GUID_DEVINTERFACE_DISK

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder? fileSystem, int fileSystemSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid, uint index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int detailSize, out int required, ref SpDevinfoData info);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint device, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_EjectW(uint device, out int vetoType, StringBuilder vetoName, int nameLength, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint device, StringBuilder buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint device, int property, out int type, out uint buffer, ref int length, int flags);
}
