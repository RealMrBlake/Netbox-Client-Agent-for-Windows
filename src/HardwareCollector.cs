using System.Management;

namespace NetBoxWindowsAgent;

internal static class HardwareCollector
{
    public static HardwareInfo Collect()
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

        return new HardwareInfo(Environment.MachineName, S(computer, "Manufacturer"), S(computer, "Model"),
            S(bios, "SerialNumber"), S(product, "UUID"), S(os, "Caption"), S(os, "Version"),
            S(os, "OSArchitecture"), S(cpu, "Name"), U(computer, "TotalPhysicalMemory"), disks, networks);
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
