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
                    MigrateTemplates();
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

        private static void MigrateTemplates()
        {
            // 구 템플릿 제거
            var obsolete = new[] { "2열", "3열", "2행" };
            Current.Layouts.RemoveAll(l => l.IsTemplate && obsolete.Contains(l.Name));

            // 새 템플릿 없으면 추가
            var newDefaults = new[] { Create49Inch(), Create27InchPivot(), Create16InchZeusLab() };
            foreach (var l in newDefaults)
            {
                if (!Current.Layouts.Any(x => x.IsTemplate && x.Name == l.Name))
                {
                    l.IsTemplate = true;
                    Current.Layouts.Add(l);
                }
            }
            Save();
        }

        private static void InitDefaultLayouts()
        {
            var defaults = new[]
            {
                CreateGridLayout(2, 2, "2x2 그리드"),
                CreateGridLayout(2, 3, "3x2 그리드"),
                CreatePriorityGrid(),
                Create49Inch(),
                Create27InchPivot(),
                Create16InchZeusLab(),
            };
            foreach (var l in defaults) l.IsTemplate = true;
            Current.Layouts.AddRange(defaults);
        }

        private static ZoneLayout Create49Inch() => new ZoneLayout
        {
            Name = "49인치",
            Zones = new System.Collections.Generic.List<ZoneRect>
            {
                new(0.0000, 0.0000, 0.3720, 1.0000),
                new(0.3720, 0.0000, 0.3615, 1.0000),
                new(0.7335, 0.0000, 0.0993, 1.0000),
                new(0.8328, 0.0000, 0.0939, 0.5007),
                new(0.8328, 0.5007, 0.0939, 0.4993),
                new(0.9267, 0.0000, 0.0733, 0.5007),
                new(0.9267, 0.5007, 0.0733, 0.4993),
            }
        };

        private static ZoneLayout Create27InchPivot() => new ZoneLayout
        {
            Name = "27인치 피벗",
            Zones = new System.Collections.Generic.List<ZoneRect>
            {
                new(0.0000, 0.0000, 1.0000, 0.3188),
                new(0.0000, 0.3188, 0.5000, 0.3309),
                new(0.5000, 0.3188, 0.5000, 0.3309),
                new(0.0000, 0.6497, 1.0000, 0.3503),
            }
        };

        private static ZoneLayout Create16InchZeusLab() => new ZoneLayout
        {
            Name = "16인치 제우스랩",
            Zones = new System.Collections.Generic.List<ZoneRect>
            {
                new(0.0, 0.0, 1.0, 1.0),
            }
        };

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
