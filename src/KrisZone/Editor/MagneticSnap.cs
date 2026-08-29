using System;
using System.Collections.Generic;

namespace KrisZone.Editor
{
    // 파워토이즈 FancyZonesEditor의 MagneticSnap.cs 이식(2026-08-29). 분할선을 그릴 때 같은 축의
    // 기존 그리드 경계선에 자석처럼 붙게 하는 로직.
    //
    // ⚠ 이 함수는 마우스가 움직일 때마다 호출된다(초당 수십~수백 회). 절대 리스트를 새로
    //   만들거나 LINQ를 쓰지 말 것 — 호출부가 미리 만들어 캐시해 둔 정렬된 경계선 배열을
    //   그대로 받고, 존 범위(low, high) 밖은 인덱스로 건너뛴다.
    internal static class MagneticSnap
    {
        // GridData.Multiplier(10000) 기준 — 한 keyPoint가 가질 수 있는 자석 영향권의 상한.
        public const int MagnetZoneMaxSize = GridData.Multiplier / 12;

        // data(현재 위치, Multiplier 스케일)를 keyPoints 중 (low, high) 안에 있는 것들에 스냅한다.
        //
        // 각 keyPoint의 영향권 = min(이전 경계까지, 다음 경계까지, MagnetZoneMaxSize) / 2.
        //   ⚠ 첫/마지막 keyPoint의 "이전/다음"은 무한대가 아니라 존의 경계(low/high)다.
        //     파워토이즈가 그렇게 한다(PixelToDataWithSnapping의 previous/next). 무한대로 두면
        //     존 가장자리에 바짝 붙은 경계선까지 최대 반경(4.16%)을 그대로 가져가서, 선이
        //     실제보다 훨씬 넓은 구간에서 들러붙어 뚝뚝 끊기는 느낌이 난다.
        //
        // 데드존((영향권+1)/2) 안에서는 keyPoint에 정확히 붙고, 데드존 밖 ~ 영향권 안에서는
        // 2배속 고무줄 변환으로 끌어당긴다. 이 두 구간은 수학적으로 이어져 있어서
        // (영향권 경계에서 출력 = 입력, 데드존 경계에서 출력 = keyPoint) 값이 튀지 않는다.
        public static int Snap(int data, IReadOnlyList<int> keyPoints, int low, int high)
        {
            if (high <= low) return data;
            data = Math.Clamp(data, low, high);
            if (keyPoints == null || keyPoints.Count == 0) return data;

            // 정렬돼 있으므로 (low, high) 구간의 앞뒤 인덱스만 잡으면 된다(할당 없음).
            int first = 0;
            while (first < keyPoints.Count && keyPoints[first] <= low) first++;
            int last = keyPoints.Count - 1;
            while (last >= 0 && keyPoints[last] >= high) last--;
            if (first > last) return data;

            for (int i = first; i <= last; i++)
            {
                int keyPoint = keyPoints[i];
                int previous = i == first ? low  : keyPoints[i - 1];
                int next     = i == last  ? high : keyPoints[i + 1];

                int magnetZoneSize = Math.Min(keyPoint - previous,
                                              Math.Min(next - keyPoint, MagnetZoneMaxSize)) / 2;
                if (magnetZoneSize <= 0) continue;

                int deadZone = (magnetZoneSize + 1) / 2;
                int absDiff = Math.Abs(data - keyPoint);

                if (absDiff <= deadZone)
                    return Math.Clamp(keyPoint, low, high);

                if (absDiff <= magnetZoneSize)
                {
                    int result = data < keyPoint
                        ? data + (data - (keyPoint - magnetZoneSize))
                        : data - ((keyPoint + magnetZoneSize) - data);
                    return Math.Clamp(result, low, high);
                }
            }

            return data;
        }
    }
}
