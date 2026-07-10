using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KrisZone.Models
{
    public enum LayoutType { Blank, Focus, Columns, Rows, Grid, PriorityGrid, Canvas }

    public class ZoneRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        // Percentage-based (0.0 ~ 1.0) relative to monitor
        public ZoneRect() { }
        public ZoneRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
    }

    // Grid 레이아웃 메타데이터 (파워토이즈 방식)
    public class GridMeta
    {
        public int Rows { get; set; } = 1;
        public int Columns { get; set; } = 1;
        public List<int> RowPercents { get; set; } = new();
        public List<int> ColumnPercents { get; set; } = new();
        public List<int> CellChildMap { get; set; } = new(); // flat row-major

        public int[,] GetCellChildMap2D()
        {
            var map = new int[Rows, Columns];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                    map[r, c] = (r * Columns + c < CellChildMap.Count) ? CellChildMap[r * Columns + c] : 0;
            return map;
        }

        public void SetCellChildMap2D(int[,] map)
        {
            CellChildMap = new List<int>(Rows * Columns);
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                    CellChildMap.Add(map[r, c]);
        }

        public static GridMeta Default1x1() => new GridMeta
        {
            Rows = 1, Columns = 1,
            RowPercents = new List<int> { 10000 },
            ColumnPercents = new List<int> { 10000 },
            CellChildMap = new List<int> { 0 }
        };
    }

    public class ZoneLayout
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "새 레이아웃";
        public LayoutType Type { get; set; } = LayoutType.Grid;
        public List<ZoneRect> Zones { get; set; } = new();
        public int SensitivityRadius { get; set; } = 20;
        public GridMeta? Grid { get; set; } = GridMeta.Default1x1();
        public bool IsTemplate { get; set; } = false;
    }

    public class MonitorConfig
    {
        public string MonitorId { get; set; } = "";
        public Guid LayoutId { get; set; }
    }

    public class AppSettings
    {
        public bool ShiftDrag { get; set; } = true;
        public bool CtrlMultiSelect { get; set; } = true;
        public bool MakeDraggedWindowTransparent { get; set; } = true;
        public bool ShowZoneNumber { get; set; } = true;
        public bool OverrideSnapHotkeys { get; set; } = false;
        public bool AlwaysOnTopEnabled { get; set; } = true;
        public string AlwaysOnTopBorderColor { get; set; } = "#8B95A5";
        public string ZoneColor { get; set; } = "#BAE6FD";
        public string ZoneBorderColor { get; set; } = "#FFFFFF";
        public string ZoneHighlightColor { get; set; } = "#38BDF8";
        public string ZoneNumberColor { get; set; } = "#FFFFFF";
        public int ZoneHighlightOpacity { get; set; } = 30;
        public List<ZoneLayout> Layouts { get; set; } = new();
        public List<MonitorConfig> MonitorConfigs { get; set; } = new();
    }
}
