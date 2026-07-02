using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KrisZone.Models;

namespace KrisZone.Settings
{
    internal static class SettingsManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "K-FancyZones");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

        public static AppSettings Current { get; private set; } = new();
        public static bool IsFirstRun { get; private set; }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    IsFirstRun = false;
                }
                else
                {
                    Current = new AppSettings();
                    InitDefaultLayouts();
                    Save();
                    IsFirstRun = true;
                }
            }
            catch
            {
                Current = new AppSettings();
                InitDefaultLayouts();
                IsFirstRun = true;
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
            var defaults = new[]
            {
                CreateGridLayout(1, 2, "2열"),
                CreateGridLayout(1, 3, "3열"),
                CreateGridLayout(2, 1, "2행"),
                CreateGridLayout(2, 2, "2x2 그리드"),
                CreateGridLayout(2, 3, "3x2 그리드"),
                CreatePriorityGrid(),
            };
            foreach (var l in defaults) l.IsTemplate = true;
            Current.Layouts.AddRange(defaults);
        }

        private const int M = 10000;

        private static List<int> EvenPercents(int count) =>
            Enumerable.Range(0, count)
                .Select(i => ((M * (i + 1)) / count) - ((M * i) / count))
                .ToList();

        private static ZoneLayout CreateGridLayout(int rows, int cols, string name)
        {
            var rowP = EvenPercents(rows);
            var colP = EvenPercents(cols);

            var cellMap = new List<int>(rows * cols);
            int idx = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cellMap.Add(idx++);

            var meta = new GridMeta { Rows = rows, Columns = cols, RowPercents = rowP, ColumnPercents = colP, CellChildMap = cellMap };
            var zones = GridMetaToZoneRects(meta, rows, cols, rowP, colP, cellMap);
            return new ZoneLayout { Name = name, Type = LayoutType.Grid, Grid = meta, Zones = zones };
        }

        private static ZoneLayout CreatePriorityGrid()
        {
            // 2행 2열 CellChildMap: 왼쪽 열 전체=0, 오른쪽 위=1, 오른쪽 아래=2
            // rows=2, cols=2  cellMap=[0,1,0,2]
            var rowP = EvenPercents(2);
            var colP = new List<int> { 5000, 5000 };
            var cellMap = new List<int> { 0, 1, 0, 2 };
            var meta = new GridMeta { Rows = 2, Columns = 2, RowPercents = rowP, ColumnPercents = colP, CellChildMap = cellMap };

            // 수동 ZoneRect (left half + top-right + bottom-right)
            var zones = new List<ZoneRect>
            {
                new(0,   0,   0.5, 1.0),
                new(0.5, 0,   0.5, 0.5),
                new(0.5, 0.5, 0.5, 0.5),
            };
            return new ZoneLayout { Name = "Priority Grid", Type = LayoutType.Grid, Grid = meta, Zones = zones };
        }

        private static List<ZoneRect> GridMetaToZoneRects(GridMeta meta, int rows, int cols,
            List<int> rowP, List<int> colP, List<int> cellMap)
        {
            // PrefixSum
            var rowPfx = new List<int>(rows + 1); rowPfx.Add(0);
            int s = 0; foreach (var v in rowP) { s += v; rowPfx.Add(s); }
            var colPfx = new List<int>(cols + 1); colPfx.Add(0);
            s = 0; foreach (var v in colP) { s += v; colPfx.Add(s); }

            int zoneCount = cellMap.Max() + 1;
            var zoneLeft   = Enumerable.Repeat(int.MaxValue, zoneCount).ToList();
            var zoneRight  = Enumerable.Repeat(0, zoneCount).ToList();
            var zoneTop    = Enumerable.Repeat(int.MaxValue, zoneCount).ToList();
            var zoneBottom = Enumerable.Repeat(0, zoneCount).ToList();

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int zi = cellMap[r * cols + c];
                    zoneLeft[zi]   = Math.Min(zoneLeft[zi],   colPfx[c]);
                    zoneRight[zi]  = Math.Max(zoneRight[zi],  colPfx[c + 1]);
                    zoneTop[zi]    = Math.Min(zoneTop[zi],    rowPfx[r]);
                    zoneBottom[zi] = Math.Max(zoneBottom[zi], rowPfx[r + 1]);
                }

            return Enumerable.Range(0, zoneCount).Select(zi =>
                new ZoneRect(zoneLeft[zi] / (double)M, zoneTop[zi] / (double)M,
                    (zoneRight[zi]  - zoneLeft[zi])  / (double)M,
                    (zoneBottom[zi] - zoneTop[zi])   / (double)M)).ToList();
        }
    }
}
