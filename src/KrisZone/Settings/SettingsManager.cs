using System;
using System.IO;
using System.Text.Json;
using KrisZone.Models;

namespace KrisZone.Settings
{
    internal static class SettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrisZone");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

        public static AppSettings Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    Current = new AppSettings();
                    InitDefaultLayouts();
                    Save();
                }
            }
            catch
            {
                Current = new AppSettings();
                InitDefaultLayouts();
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }

        private static void InitDefaultLayouts()
        {
            Current.Layouts.AddRange(new[]
            {
                CreateColumns(2, "2열"),
                CreateColumns(3, "3열"),
                CreateRows(2, "2행"),
                CreateGrid(2, 2, "2x2 그리드"),
                CreateGrid(3, 2, "3x2 그리드"),
                CreatePriorityGrid(),
            });
        }

        private static ZoneLayout CreateColumns(int count, string name)
        {
            var layout = new ZoneLayout { Name = name, Type = LayoutType.Canvas };
            double w = 1.0 / count;
            for (int i = 0; i < count; i++)
                layout.Zones.Add(new ZoneRect(i * w, 0, w, 1.0));
            return layout;
        }

        private static ZoneLayout CreateRows(int count, string name)
        {
            var layout = new ZoneLayout { Name = name, Type = LayoutType.Canvas };
            double h = 1.0 / count;
            for (int i = 0; i < count; i++)
                layout.Zones.Add(new ZoneRect(0, i * h, 1.0, h));
            return layout;
        }

        private static ZoneLayout CreateGrid(int cols, int rows, string name)
        {
            var layout = new ZoneLayout { Name = name, Type = LayoutType.Canvas };
            double w = 1.0 / cols, h = 1.0 / rows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    layout.Zones.Add(new ZoneRect(c * w, r * h, w, h));
            return layout;
        }

        private static ZoneLayout CreatePriorityGrid()
        {
            return new ZoneLayout
            {
                Name = "Priority Grid",
                Type = LayoutType.Canvas,
                Zones = new System.Collections.Generic.List<ZoneRect>
                {
                    new(0,    0,    0.5,  1.0),   // Left half
                    new(0.5,  0,    0.25, 0.5),   // Top-right quarter
                    new(0.75, 0,    0.25, 0.5),   // Top-far-right
                    new(0.5,  0.5,  0.5,  0.5),   // Bottom-right half
                }
            };
        }
    }
}
