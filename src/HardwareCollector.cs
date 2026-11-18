using System.Management;
using Microsoft.Win32;

namespace NetBoxWindowsAgent;

internal static class HardwareCollector
{
    public static HardwareInfo Collect(bool collectSoftware = true, int maxSoftwareEntries = 250)
    {
        var computer = First("Win32_ComputerSystem", "Name", "Domain", "PartOfDomain", "Manufacturer", "Model",
            "TotalPhysicalMemory", "UserName");
        var product = First("Win32_ComputerSystemProduct", "UUID", "Name", "Version");
        var bios = First("Win32_BIOS", "SerialNumber", "SMBIOSBIOSVersion");
        var os = First("Win32_OperatingSystem", "Caption", "Version", "BuildNumber", "OSArchitecture", "InstallDate",
            "LastBootUpTime");
        var enclosure = FirstOrDefault("Win32_SystemEnclosure", "ChassisTypes");
        var processors = Query("Win32_Processor", "Name", "NumberOfCores").ToList();

        var disks = Query("Win32_DiskDrive", "DeviceID", "Model", "SerialNumber", "Size")
            .Select(x => new DiskInfo(S(x, "DeviceID"), S(x, "Model"), S(x, "SerialNumber"), U(x, "Size"))).ToList();
        var networks = Query("Win32_NetworkAdapterConfiguration", "Description", "MACAddress", "IPAddress")
            .Where(x => x["MACAddress"] is not null)
            .Select(x => new NetworkInfo(S(x, "Description"), S(x, "MACAddress"),
                Strings(x, "IPAddress").Where(ip => !ip.StartsWith("169.254.")).ToList())).ToList();
        var memoryModules = Query("Win32_PhysicalMemory", "BankLabel", "DeviceLocator", "Manufacturer", "PartNumber", "SerialNumber", "Capacity")
            .Select(x => new MemoryInfo(
                string.IsNullOrWhiteSpace(S(x, "BankLabel")) ? S(x, "DeviceLocator") : S(x, "BankLabel"),
                S(x, "Manufacturer"), S(x, "PartNumber"), S(x, "SerialNumber"), U(x, "Capacity"))).ToList();

        return new HardwareInfo
        {
            Hostname = Environment.MachineName,
            Domain = S(computer, "Domain"),
            PartOfDomain = computer["PartOfDomain"] is true,
            Manufacturer = S(computer, "Manufacturer"),
            Model = S(computer, "Model"),
            ProductName = S(product, "Name"),
            ProductVersion = S(product, "Version"),
            Serial = S(bios, "SerialNumber"),
            Uuid = S(product, "UUID"),
            BiosVersion = S(bios, "SMBIOSBIOSVersion"),
            Chassis = enclosure?["ChassisTypes"] is ushort[] types ? types.Select(t => (int)t).ToList() : [],
            User = S(computer, "UserName"),
            WindowsCaption = S(os, "Caption"),
            WindowsVersion = S(os, "Version"),
            WindowsBuild = S(os, "BuildNumber"),
            WindowsDisplayVersion = RegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion"),
            Architecture = S(os, "OSArchitecture"),
            Installed = CimDate(S(os, "InstallDate")),
            LastBoot = CimDate(S(os, "LastBootUpTime")),
            Processor = processors.Count > 0 ? S(processors[0], "Name") : "",
            Cores = processors.Sum(p => (int)U(p, "NumberOfCores")),
            MemoryBytes = U(computer, "TotalPhysicalMemory"),
            Disks = disks,
            Networks = networks,
            Nics = CollectNics(),
            MemoryModules = memoryModules,
            Software = collectSoftware ? CollectSoftware(maxSoftwareEntries) : [],
        };
    }

    private static IReadOnlyList<NicInfo> CollectNics()
    {
        var adapters = Query("Win32_NetworkAdapter", "InterfaceIndex", "PhysicalAdapter", "NetConnectionID", "Speed")
            .Where(x => x["PhysicalAdapter"] is true)
            .GroupBy(x => U(x, "InterfaceIndex"))
            .ToDictionary(g => g.Key, g => g.First());
        var nics = new List<NicInfo>();
        foreach (var config in Query("Win32_NetworkAdapterConfiguration", "IPEnabled", "InterfaceIndex", "Description",
                     "MACAddress", "IPAddress", "IPSubnet", "DefaultIPGateway", "DHCPEnabled"))
        {
            if (config["IPEnabled"] is not true) continue;
            adapters.TryGetValue(U(config, "InterfaceIndex"), out var adapter);
            nics.Add(new NicInfo(adapter is null ? "" : S(adapter, "NetConnectionID"), S(config, "Description"),
                S(config, "MACAddress"), Strings(config, "IPAddress"), Strings(config, "IPSubnet"),
                Strings(config, "DefaultIPGateway"), config["DHCPEnabled"] is true, adapter is not null,
                adapter is null ? 0 : U(adapter, "Speed")));
        }
        return nics;
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

    private static string RegistryString(string key, string value)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var sub = hive.OpenSubKey(key);
        return Convert.ToString(sub?.GetValue(value))?.Trim() ?? "";
    }

    // CIM datetime "20240115083000.000000+060" -> "2024-01-15T08:30:00"
    private static string CimDate(string value) =>
        value.Length >= 14 ? $"{value[..4]}-{value[4..6]}-{value[6..8]}T{value[8..10]}:{value[10..12]}:{value[12..14]}" : "";

    private static ManagementBaseObject First(string cls, params string[] fields) => Query(cls, fields).First();
    private static ManagementBaseObject? FirstOrDefault(string cls, params string[] fields) => Query(cls, fields).FirstOrDefault();
    private static IEnumerable<ManagementBaseObject> Query(string cls, params string[] fields)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(',', fields)} FROM {cls}");
        foreach (ManagementBaseObject item in searcher.Get()) yield return item;
    }
    private static string S(ManagementBaseObject x, string key) => Convert.ToString(x[key])?.Trim() ?? "";
    private static ulong U(ManagementBaseObject x, string key) => ulong.TryParse(Convert.ToString(x[key]), out var n) ? n : 0;
    private static List<string> Strings(ManagementBaseObject x, string key) =>
        (x[key] as string[] ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
}
