using System.Management;
using Microsoft.Win32;

namespace NetBoxWindowsAgent;

internal static class HardwareCollector
{
    public static HardwareInfo Collect(bool collectSoftware = true, int maxSoftwareEntries = 250)
    {
        var computer = First("Win32_ComputerSystem", "Manufacturer", "Model", "TotalPhysicalMemory");
        var product = First("Win32_ComputerSystemProduct", "UUID");
        var bios = First("Win32_BIOS", "SerialNumber");
        var os = First("Win32_OperatingSystem", "Caption", "Version", "OSArchitecture");
        var cpu = First("Win32_Processor", "Name");

        var disks = Query("Win32_DiskDrive", "DeviceID", "Model", "SerialNumber", "Size")
            .Select(x => new DiskInfo(S(x, "DeviceID"), S(x, "Model"), S(x, "SerialNumber"), U(x, "Size"))).ToList();
        var networks = Query("Win32_NetworkAdapterConfiguration", "Description", "MACAddress", "IPAddress")
            .Where(x => x["MACAddress"] is not null)
            .Select(x => new NetworkInfo(S(x, "Description"), S(x, "MACAddress"),
                (x["IPAddress"] as string[] ?? []).Where(ip => !ip.StartsWith("169.254.")).ToList())).ToList();
        var memoryModules = Query("Win32_PhysicalMemory", "BankLabel", "DeviceLocator", "Manufacturer", "PartNumber", "SerialNumber", "Capacity")
            .Select(x => new MemoryInfo(
                string.IsNullOrWhiteSpace(S(x, "BankLabel")) ? S(x, "DeviceLocator") : S(x, "BankLabel"),
                S(x, "Manufacturer"), S(x, "PartNumber"), S(x, "SerialNumber"), U(x, "Capacity"))).ToList();

        return new HardwareInfo(Environment.MachineName, S(computer, "Manufacturer"), S(computer, "Model"),
            S(bios, "SerialNumber"), S(product, "UUID"), S(os, "Caption"), S(os, "Version"),
            S(os, "OSArchitecture"), S(cpu, "Name"), U(computer, "TotalPhysicalMemory"), disks, networks,
            memoryModules, collectSoftware ? CollectSoftware(maxSoftwareEntries) : []);
    }

    private static IReadOnlyList<SoftwareInfo> CollectSoftware(int maximum)
    {
        var software = new Dictionary<string, SoftwareInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(subKeyName);
                var name = Convert.ToString(entry?.GetValue("DisplayName"))?.Trim();
                if (string.IsNullOrWhiteSpace(name) || Convert.ToInt32(entry?.GetValue("SystemComponent", 0)) == 1) continue;
                var version = Convert.ToString(entry?.GetValue("DisplayVersion"))?.Trim() ?? "";
                var publisher = Convert.ToString(entry?.GetValue("Publisher"))?.Trim() ?? "";
                software.TryAdd($"{name}\0{version}", new SoftwareInfo(name, version, publisher));
            }
        }
        return software.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(Math.Clamp(maximum, 0, 1000)).ToList();
    }

    private static ManagementBaseObject First(string cls, params string[] fields) => Query(cls, fields).First();
    private static IEnumerable<ManagementBaseObject> Query(string cls, params string[] fields)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(',', fields)} FROM {cls}");
        foreach (ManagementBaseObject item in searcher.Get()) yield return item;
    }
    private static string S(ManagementBaseObject x, string key) => Convert.ToString(x[key])?.Trim() ?? "";
    private static ulong U(ManagementBaseObject x, string key) => ulong.TryParse(Convert.ToString(x[key]), out var n) ? n : 0;
}
