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
        // ── 외형/동작 설정: 설정창을 없애고 파워토이즈 실제 기본값으로 고정 (2026-08-29) ──
        //
        // ⚠ 일부러 get 전용이다. System.Text.Json은 get 전용 프로퍼티를 "쓸 때는 파일에 남기고,
        //   읽을 때는 무시"하기 때문에, 예전 settings.json에 남아있는 옛 값이 있어도 항상 아래
        //   고정값이 적용된다(별도 마이그레이션 코드가 필요 없다).
        //   반대로 Layouts/MonitorConfigs는 get; set; 그대로라 사용자가 만든 레이아웃과
        //   모니터 배치는 종전대로 저장·복원된다 — 여기는 절대 건드리지 말 것.
        //
        // 출처: PowerToys FancyZonesLib/Settings.h (C++ 실제 동작 기준)
        //   shiftDrag = true / showZoneNumber = true / overrideSnapHotkeys = false
        //   zoneColor = "#AACDFF" / zoneHighlightColor = "#008CFF" / zoneHighlightOpacity = 50
        public bool ShiftDrag { get; } = true;
        public bool ShowZoneNumber { get; } = true;
        public bool OverrideSnapHotkeys { get; } = false;
        public string ZoneColor { get; } = "#AACDFF";
        public string ZoneHighlightColor { get; } = "#008CFF";
        public int ZoneHighlightOpacity { get; } = 50;

        // 드래그 중 창 투명화는 팬시존에 없는 K-Zone 고유 기능 — 기존 기본값 유지.
        public bool MakeDraggedWindowTransparent { get; } = true;

        // 출처: PowerToys AlwaysOnTopProperties.cs — DefaultFrameEnabled = true.
        // 테두리 색/두께는 AccentColor.cs가 담당한다(강조색 자동 추종, DefaultFrameThickness = 4).
        public bool AlwaysOnTopEnabled { get; } = true;

        // ── 사용자 데이터 (계속 저장/복원됨) ──
        public List<ZoneLayout> Layouts { get; set; } = new();
        public List<MonitorConfig> MonitorConfigs { get; set; } = new();
    }
}
