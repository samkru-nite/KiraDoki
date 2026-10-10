using System.Diagnostics;
using System.Xml.Linq;

namespace IsleBar.App.Interop;

/// <summary>
/// Who refused an eject (TODO ④, user 10-10: the lifted card names it — "微信 is using this drive"). Windows writes every refusal to
/// the System log: Kernel-PnP event 225, with the program's path, its process id and the device (checked on PC 10-10; a standard
/// user can read it). Read with <c>wevtutil</c> right after a refusal — rare, so a short-lived process is fine. The program's own
/// description ("微信") when it can be read, else its file name. Null when nothing fits.
/// </summary>
internal static class EjectBlocker
{
    private const string Query =
        "*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] and (EventID=225) and TimeCreated[timediff(@SystemTime) <= 20000]]]";

    public static string? Find(string? device)
    {
        try
        {
            using var wevtutil = Process.Start(new ProcessStartInfo("wevtutil.exe", $"qe System /q:\"{Query}\" /c:5 /rd:true /f:xml")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            });
            if (wevtutil is null)
            {
                return null;
            }

            var xml = wevtutil.StandardOutput.ReadToEnd();
            wevtutil.WaitForExit(3000);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var events = XDocument.Parse("<all>" + xml + "</all>").Root!.Elements(ns + "Event")
                .Select(e => e.Descendants(ns + "Data").ToDictionary(d => (string?)d.Attribute("Name") ?? string.Empty, d => d.Value.Trim()))
                .ToList();

            // newest first: the one for this device, else the newest at all (a refusal just now is almost surely ours)
            var hit = events.FirstOrDefault(d => device is not null && d.TryGetValue("DeviceInstance", out var id)
                                                 && string.Equals(id, device, StringComparison.OrdinalIgnoreCase))
                      ?? events.FirstOrDefault();
            if (hit is null)
            {
                TaskbarHost.Log("drive: no refusal in the system log");
                return null;
            }

            var name = NameOf(hit);
            TaskbarHost.Log($"drive: refused by {name ?? "(unknown)"}");
            return name;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Xml.XmlException or IOException)
        {
            TaskbarHost.Log("drive: system log not read — " + ex.Message);
            return null;
        }
    }

    private static string? NameOf(Dictionary<string, string> data)
    {
        // a running program: its own description; else the exe from the command line / the device path ("\Device\HarddiskVolume5\…\x.exe")
        if (data.TryGetValue("ProcessId", out var pid) && int.TryParse(pid, out var id))
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.MainModule?.FileName is { } file && Describe(file) is { } description)
                {
                    return description;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // gone, or not ours to look into (an elevated / system process)
            }
        }

        var path = data.TryGetValue("CommandLine", out var line) && line.Length > 0
            ? line.StartsWith('"') ? line[1..].Split('"')[0] : line.Split(' ')[0]
            : data.GetValueOrDefault("ProcessName");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return (File.Exists(path) ? Describe(path) : null) ?? Path.GetFileNameWithoutExtension(path);
    }

    private static string? Describe(string file)
    {
        var info = FileVersionInfo.GetVersionInfo(file);
        var name = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}
