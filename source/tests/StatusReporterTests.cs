using System;

namespace LittleDeepSeek
{
    internal static class StatusReporterTests
    {
        private static int Main(string[] args)
        {
            WeatherReport weather = StatusReporter.ParseWeather(
                "{\"current_condition\":[{\"temp_C\":\"26\",\"FeelsLikeC\":\"28\",\"humidity\":\"85\",\"weatherDesc\":[{\"value\":\"Partly cloudy\"}]}],\"nearest_area\":[{\"areaName\":[{\"value\":\"杭州\"}]}]}", "");
            Require(weather.Success, "weather JSON should parse");
            Require(weather.City == "杭州", "nearest city should be used for automatic location");
            Require(weather.ToSpeech().Contains("26°C · 多云"), "weather should be localized");

            HardwareReport sample = new HardwareReport { CpuPercent = 91, MemoryPercent = 55, GpuTemperature = 60 };
            Require(sample.GetWarning().Contains("CPU 已到 91%"), "CPU threshold should alert at 90%");
            sample.CpuPercent = 50; sample.MemoryPercent = 95;
            Require(sample.GetWarning().Contains("内存已到 95%"), "memory threshold should alert at 95%");
            sample.MemoryPercent = 50; sample.GpuTemperature = 81;
            Require(sample.GetWarning().Contains("显卡温度 81°C"), "GPU threshold should alert above 80C");
            sample.GpuTemperature = null;
            Require(sample.ToSpeech().Contains("显卡温度未读取到"), "missing NVIDIA tools should be explained");

            HardwareReport live = StatusReporter.GetHardware();
            Require(live.CpuPercent.HasValue || live.MemoryPercent.HasValue, "WMI should provide at least CPU or memory on this Windows host");
            Console.WriteLine("StatusReporter tests passed. Live: " + live.ToSpeech().Replace("\n", " | "));
            if (args.Length > 0 && args[0] == "--live-weather")
            {
                WeatherReport liveWeather = StatusReporter.GetWeather("杭州");
                Require(liveWeather.Success, "live wttr.in weather request should succeed");
                Console.WriteLine("Live weather: " + liveWeather.ToSpeech().Replace("\n", " | "));
            }
            return 0;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
