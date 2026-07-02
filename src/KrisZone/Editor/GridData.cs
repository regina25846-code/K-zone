using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using KrisZone.Models;

namespace KrisZone.Editor
{
    public class GridData
    {
        public const int Multiplier = 10000;

        public static List<int> PrefixSum(List<int> list)
        {
            var result = new List<int>(list.Count + 1);
            result.Add(0);
            int sum = 0;
            for (int i = 0; i < list.Count; i++)
            {
                sum += list[i];
                result.Add(sum);
            }
            return result;
        }

        private static List<int> AdjacentDifference(List<int> list)
        {
            if (list.Count <= 1) return new List<int>();
            var result = new List<int>(list.Count - 1);
            for (int i = 0; i < list.Count - 1; i++)
                result.Add(list[i + 1] - list[i]);
            return result;
        }

        private static List<int> Unique(List<int> list)
        {
            var result = new List<int>();
            if (list.Count == 0) return result;
            int last = list[0];
            result.Add(last);
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i] != last) { last = list[i]; result.Add(last); }
            }
            return result;
        }

        public struct Zone
        {
            public int Index { get; set; }
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
        }

        public struct Resizer
        {
            public Orientation Orientation { get; set; }
            public List<int> NegativeSideIndices { get; set; }
            public List<int> PositiveSideIndices { get; set; }
        }

        private List<Zone> _zones = new();
        private List<Resizer> _resizers = new();
        private GridMeta _meta;

        public int MinZoneWidth { get; set; } = 1;
        public int MinZoneHeight { get; set; } = 1;

        public GridData(GridMeta meta)
        {
            _meta = meta;
            FromMeta(meta);
        }

        public IReadOnlyList<Zone> Zones => _zones;
        public IReadOnlyList<Resizer> Resizers => _resizers;

        private void FromMeta(GridMeta meta)
        {
            MetaToZones(meta);
            MetaToResizers(meta);
        }

        private void MetaToZones(GridMeta meta)
        {
            int rows = meta.Rows;
            int cols = meta.Columns;
            var grid = meta.GetCellChildMap2D();

            int zoneCount = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    zoneCount = Math.Max(zoneCount, grid[r, c]);
            zoneCount++;

            var indexRowLow  = Enumerable.Repeat(int.MaxValue, zoneCount).ToList();
            var indexRowHigh = Enumerable.Repeat(0, zoneCount).ToList();
            var indexColLow  = Enumerable.Repeat(int.MaxValue, zoneCount).ToList();
            var indexColHigh = Enumerable.Repeat(0, zoneCount).ToList();
            var indexCount   = Enumerable.Repeat(0, zoneCount).ToList();

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int idx = grid[r, c];
                    indexCount[idx]++;
                    indexRowLow[idx]  = Math.Min(indexRowLow[idx],  r);
                    indexRowHigh[idx] = Math.Max(indexRowHigh[idx], r);
                    indexColLow[idx]  = Math.Min(indexColLow[idx],  c);
                    indexColHigh[idx] = Math.Max(indexColHigh[idx], c);
                }

            var rowPfx = PrefixSum(meta.RowPercents);
            var colPfx = PrefixSum(meta.ColumnPercents);

            _zones = new List<Zone>(zoneCount);
            for (int idx = 0; idx < zoneCount; idx++)
            {
                _zones.Add(new Zone
                {
                    Index  = idx,
                    Left   = colPfx[indexColLow[idx]],
                    Right  = colPfx[indexColHigh[idx] + 1],
                    Top    = rowPfx[indexRowLow[idx]],
                    Bottom = rowPfx[indexRowHigh[idx] + 1],
                });
            }
        }

        private void MetaToResizers(GridMeta meta)
        {
            int rows = meta.Rows;
            int cols = meta.Columns;
            var grid = meta.GetCellChildMap2D();

            _resizers = new List<Resizer>();

            // Horizontal resizers
            for (int row = 1; row < rows; row++)
            {
                for (int startCol = 0; startCol < cols;)
                {
                    if (grid[row - 1, startCol] != grid[row, startCol])
                    {
                        int endCol = startCol;
                        while (endCol + 1 < cols && grid[row - 1, endCol + 1] != grid[row, endCol + 1])
                            endCol++;

                        var r = new Resizer { Orientation = Orientation.Horizontal };
                        var neg = new List<int>();
                        var pos = new List<int>();
                        for (int col = startCol; col <= endCol; col++)
                        {
                            neg.Add(grid[row - 1, col]);
                            pos.Add(grid[row, col]);
                        }
                        r.NegativeSideIndices = Unique(neg);
                        r.PositiveSideIndices = Unique(pos);
                        _resizers.Add(r);
                        startCol = endCol + 1;
                    }
                    else startCol++;
                }
            }

            // Vertical resizers
            for (int col = 1; col < cols; col++)
            {
                for (int startRow = 0; startRow < rows;)
                {
                    if (grid[startRow, col - 1] != grid[startRow, col])
                    {
                        int endRow = startRow;
                        while (endRow + 1 < rows && grid[endRow + 1, col - 1] != grid[endRow + 1, col])
                            endRow++;

                        var r = new Resizer { Orientation = Orientation.Vertical };
                        var neg = new List<int>();
                        var pos = new List<int>();
                        for (int row = startRow; row <= endRow; row++)
                        {
                            neg.Add(grid[row, col - 1]);
                            pos.Add(grid[row, col]);
                        }
                        r.NegativeSideIndices = Unique(neg);
                        r.PositiveSideIndices = Unique(pos);
                        _resizers.Add(r);
                        startRow = endRow + 1;
                    }
                    else startRow++;
                }
            }
        }

        private void ZonesToMeta(GridMeta meta)
        {
            var xCoords = _zones.Select(z => z.Right).Concat(_zones.Select(z => z.Left)).Distinct().OrderBy(x => x).ToList();
            var yCoords = _zones.Select(z => z.Top).Concat(_zones.Select(z => z.Bottom)).Distinct().OrderBy(y => y).ToList();

            meta.Rows    = yCoords.Count - 1;
            meta.Columns = xCoords.Count - 1;
            meta.RowPercents    = AdjacentDifference(yCoords);
            meta.ColumnPercents = AdjacentDifference(xCoords);

            var map = new int[meta.Rows, meta.Columns];
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                int sr = yCoords.IndexOf(z.Top),    er = yCoords.IndexOf(z.Bottom);
                int sc = xCoords.IndexOf(z.Left),   ec = xCoords.IndexOf(z.Right);
                for (int r = sr; r < er; r++)
                    for (int c = sc; c < ec; c++)
                        map[r, c] = i;
            }
            meta.SetCellChildMap2D(map);
        }

        private Tuple<List<int>, Zone> ComputeClosure(List<int> indices)
        {
            int left = int.MaxValue, right = int.MinValue, top = int.MaxValue, bottom = int.MinValue;
            if (indices.Count == 0) return Tuple.Create(new List<int>(), new Zone { Index = -1, Left = left, Right = right, Top = top, Bottom = bottom });

            void Extend(Zone z) { left = Math.Min(left, z.Left); right = Math.Max(right, z.Right); top = Math.Min(top, z.Top); bottom = Math.Max(bottom, z.Bottom); }
            foreach (int idx in indices) Extend(_zones[idx]);

            bool possiblyBroken = true;
            while (possiblyBroken)
            {
                possiblyBroken = false;
                foreach (var z in _zones)
                {
                    int area    = (z.Bottom - z.Top) * (z.Right - z.Left);
                    int cutL    = Math.Max(left, z.Left),  cutR = Math.Min(right, z.Right);
                    int cutT    = Math.Max(top, z.Top),    cutB = Math.Min(bottom, z.Bottom);
                    int newArea = Math.Max(0, cutB - cutT) * Math.Max(0, cutR - cutL);
                    if (newArea != 0 && newArea != area) { Extend(z); possiblyBroken = true; }
                }
            }

            var resultIndices = _zones.FindAll(z => left <= z.Left && z.Right <= right && top <= z.Top && z.Bottom <= bottom)
                                       .Select(z => z.Index).ToList();
            return Tuple.Create(resultIndices, new Zone { Index = -1, Left = left, Right = right, Top = top, Bottom = bottom });
        }

        public List<int> MergeClosureIndices(List<int> indices) => ComputeClosure(indices).Item1;

        public void DoMerge(List<int> indices)
        {
            if (indices.Count == 0) return;
            int lowestIndex = indices.Min();
            var closure = ComputeClosure(indices);
            var closureSet = closure.Item1.ToHashSet();
            Zone closureZone = closure.Item2;

            _zones = _zones.FindAll(z => !closureSet.Contains(z.Index)).ToList();
            _zones.Insert(lowestIndex, closureZone);

            ZonesToMeta(_meta);
            FromMeta(_meta);
        }

        public bool CanSplit(int zoneIndex, int position, Orientation orientation)
        {
            if (zoneIndex < 0 || zoneIndex >= _zones.Count) return false;
            var z = _zones[zoneIndex];
            return orientation == Orientation.Horizontal
                ? z.Top + MinZoneHeight <= position && position <= z.Bottom - MinZoneHeight
                : z.Left + MinZoneWidth <= position && position <= z.Right - MinZoneWidth;
        }

        public void Split(int zoneIndex, int position, Orientation orientation)
        {
            if (!CanSplit(zoneIndex, position, orientation)) return;
            var z = _zones[zoneIndex];
            var z1 = z; var z2 = z;
            _zones.RemoveAt(zoneIndex);

            if (orientation == Orientation.Horizontal) { z1.Bottom = position; z2.Top = position; }
            else { z1.Right = position; z2.Left = position; }

            _zones.Insert(zoneIndex, z1);
            _zones.Insert(zoneIndex + 1, z2);

            ZonesToMeta(_meta);
            FromMeta(_meta);
        }

        public bool CanDrag(int resizerIndex, int delta)
        {
            var res = _resizers[resizerIndex];
            int minSize = res.Orientation == Orientation.Vertical ? MinZoneWidth : MinZoneHeight;

            int GetSize(int zi) { var z = _zones[zi]; return res.Orientation == Orientation.Vertical ? z.Right - z.Left : z.Bottom - z.Top; }

            return res.PositiveSideIndices.All(zi => GetSize(zi) - delta >= minSize) &&
                   res.NegativeSideIndices.All(zi => GetSize(zi) + delta >= minSize);
        }

        public void Drag(int resizerIndex, int delta)
        {
            if (!CanDrag(resizerIndex, delta)) return;
            var res = _resizers[resizerIndex];

            foreach (int zi in res.PositiveSideIndices)
            {
                var z = _zones[zi];
                if (res.Orientation == Orientation.Horizontal) z.Top    += delta;
                else                                           z.Left   += delta;
                _zones[zi] = z;
            }
            foreach (int zi in res.NegativeSideIndices)
            {
                var z = _zones[zi];
                if (res.Orientation == Orientation.Horizontal) z.Bottom += delta;
                else                                           z.Right  += delta;
                _zones[zi] = z;
            }

            ZonesToMeta(_meta);
            FromMeta(_meta);
        }

        // GridData.Zone 목록 → ZoneRect 목록 (퍼센트 0~1)
        public List<Models.ZoneRect> ToZoneRects()
        {
            const double m = Multiplier;
            return _zones.Select(z => new Models.ZoneRect(
                z.Left  / m, z.Top    / m,
                (z.Right - z.Left) / m, (z.Bottom - z.Top) / m)).ToList();
        }
    }
}
