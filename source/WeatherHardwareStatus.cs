using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Web.Script.Serialization;

namespace LittleDeepSeek
{
    // All slow I/O in this file is called from a worker thread.  The pet's UI
    // stays responsive while weather, WMI, or nvidia-smi are unavailable.
    internal sealed class WeatherReport
    {
        internal bool Success;
        internal string City;
        internal string Temperature;
        internal string Description;
        internal string FeelsLike;
        internal string Humidity;

        internal string ToSpeech()
        {
            if (!Success) return "天气暂时没有查到，晚点再试试吧。";
            string detail = "";
            if (!String.IsNullOrEmpty(FeelsLike)) detail += "体感 " + FeelsLike + "°C";
            if (!String.IsNullOrEmpty(Humidity)) detail += (detail.Length > 0 ? " · " : "") + "湿度 " + Humidity + "%";
            return City + "现在 " + Temperature + "°C · " + Description
                + (detail.Length > 0 ? "\n" + detail : "");
        }
    }

    internal sealed class HardwareReport
    {
        internal int? CpuPercent;
        internal int? MemoryPercent;
        internal int? GpuTemperature;

        internal string ToSpeech()
        {
            string cpu = CpuPercent.HasValue ? "CPU " + CpuPercent.Value + "%" : "CPU 未读取到";
            string memory = MemoryPercent.HasValue ? "内存 " + MemoryPercent.Value + "%" : "内存 未读取到";
            string gpu = GpuTemperature.HasValue ? "显卡 " + GpuTemperature.Value + "°C" : "显卡温度未读取到";
            return "电脑状态：" + cpu + " · " + memory + "\n" + gpu;
        }

        internal string GetWarning()
        {
            if (CpuPercent.HasValue && CpuPercent.Value >= 90)
                return "CPU 已到 " + CpuPercent.Value + "%！\n让电脑喘口气吧～";
            if (MemoryPercent.HasValue && MemoryPercent.Value >= 95)
                return "内存已到 " + MemoryPercent.Value + "%！\n关掉几个不用的程序吧～";
            if (GpuTemperature.HasValue && GpuTemperature.Value > 80)
                return "显卡温度 " + GpuTemperature.Value + "°C 了！\n我都快被烤热啦～";
            return null;
        }
    }

    internal static class StatusReporter
    {
        private const string WeatherEndpoint = "https://wttr.in/";

        internal static WeatherReport GetWeather(string configuredCity)
        {
            try
            {
                // .NET Framework 4.x may otherwise offer only legacy TLS on a
                // freshly installed Windows profile; wttr.in requires TLS 1.2.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string city = (configuredCity ?? "").Trim();
                string target = city.Length == 0 ? WeatherEndpoint + "?format=j1"
                    : WeatherEndpoint + Uri.EscapeDataString(city) + "?format=j1";
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(target);
                request.Timeout = 9000;
                request.ReadWriteTimeout = 9000;
                request.UserAgent = "LittleDeepSeek desktop pet";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    return ParseWeather(reader.ReadToEnd(), city);
            }
            catch { return new WeatherReport { Success = false }; }
        }

        internal static WeatherReport ParseWeather(string json, string configuredCity)
        {
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object[] conditions = root == null ? null : root["current_condition"] as object[];
                Dictionary<string, object> current = conditions == null || conditions.Length == 0 ? null
                    : conditions[0] as Dictionary<string, object>;
                if (current == null) return new WeatherReport { Success = false };

                string city = configuredCity;
                object[] areas = root["nearest_area"] as object[];
                if (String.IsNullOrEmpty(city) && areas != null && areas.Length > 0)
                {
                    Dictionary<string, object> area = areas[0] as Dictionary<string, object>;
                    object[] names = area == null ? null : area["areaName"] as object[];
                    Dictionary<string, object> name = names == null || names.Length == 0 ? null : names[0] as Dictionary<string, object>;
                    city = ReadValue(name, "value");
                }
                if (String.IsNullOrEmpty(city)) city = "这里";
                object[] descriptions = current["weatherDesc"] as object[];
                Dictionary<string, object> description = descriptions == null || descriptions.Length == 0 ? null
                    : descriptions[0] as Dictionary<string, object>;
                return new WeatherReport {
                    Success = true,
                    City = city,
                    Temperature = ReadValue(current, "temp_C"),
                    Description = TranslateWeather(ReadValue(description, "value")),
                    FeelsLike = ReadValue(current, "FeelsLikeC"),
                    Humidity = ReadValue(current, "humidity")
                };
            }
            catch { return new WeatherReport { Success = false }; }
        }

        internal static HardwareReport GetHardware()
        {
            HardwareReport report = new HardwareReport();
            try
            {
                double total = 0; int count = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT LoadPercentage FROM Win32_Processor"))
                using (ManagementObjectCollection results = searcher.Get())
                    foreach (ManagementObject item in results)
                    {
                        object value = item["LoadPercentage"];
                        if (value != null) { total += Convert.ToDouble(value, CultureInfo.InvariantCulture); count++; }
                    }
                if (count > 0) report.CpuPercent = (int)Math.Round(total / count);
            }
            catch { }
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection results = searcher.Get())
                    foreach (ManagementObject item in results)
                    {
                        ulong total = Convert.ToUInt64(item["TotalVisibleMemorySize"], CultureInfo.InvariantCulture);
                        ulong free = Convert.ToUInt64(item["FreePhysicalMemory"], CultureInfo.InvariantCulture);
                        if (total > 0) report.MemoryPercent = (int)Math.Round((total - free) * 100d / total);
                        break;
                    }
            }
            catch { }
            report.GpuTemperature = TryGetNvidiaTemperature();
            return report;
        }

        private static int? TryGetNvidiaTemperature()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(1500))
                    {
                        if (process != null) try { process.Kill(); } catch { }
                        return null;
                    }
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    string firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length > 0
                        ? output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim() : "";
                    int value;
                    return Int32.TryParse(firstLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? (int?)value : null;
                }
            }
            catch { return null; }
        }

        private static string ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static string TranslateWeather(string value)
        {
            switch (value)
            {
                case "Sunny": case "Clear": return "晴";
                case "Partly cloudy": return "多云";
                case "Cloudy": case "Overcast": return "阴";
                case "Mist": case "Fog": return "有雾";
                case "Light rain": case "Patchy rain nearby": return "小雨";
                case "Moderate rain": return "中雨";
                case "Heavy rain": return "大雨";
                case "Light snow": return "小雪";
                case "Moderate or heavy snow showers": return "大雪";
                case "Thundery outbreaks possible": return "可能有雷雨";
                default: return String.IsNullOrEmpty(value) ? "未知" : value;
            }
        }
    }
}
