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

    public class ZoneLayout
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "새 레이아웃";
        public LayoutType Type { get; set; } = LayoutType.Canvas;
        public List<ZoneRect> Zones { get; set; } = new();
        public int SensitivityRadius { get; set; } = 20;
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
        public bool AppLastZone { get; set; } = false;
        public string ZoneColor { get; set; } = "#AACDFF";
        public string ZoneBorderColor { get; set; } = "#FFFFFF";
        public string ZoneHighlightColor { get; set; } = "#008CFF";
        public string ZoneNumberColor { get; set; } = "#000000";
        public int ZoneHighlightOpacity { get; set; } = 50;
        public List<string> ExcludedApps { get; set; } = new();
        public List<ZoneLayout> Layouts { get; set; } = new();
        public List<MonitorConfig> MonitorConfigs { get; set; } = new();
        public Dictionary<string, Guid> AppLastZoneMap { get; set; } = new();
    }
}
