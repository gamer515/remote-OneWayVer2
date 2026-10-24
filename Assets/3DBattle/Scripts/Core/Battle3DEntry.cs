using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    /// <summary>씬 전환 사이에 3D 전투 시작 정보를 한 번 전달합니다.</summary>
    public static class Battle3DEntry
    {
        private static Battle3DStartData pendingData;

        public static bool TryTake(out Battle3DStartData data)
        {
            data = pendingData;
            pendingData = null;
            return data != null;
        }

        // Decision 씬에서 3DBattleScene을 로드하기 전에 호출합니다.
        public static void SetNext(Battle3DStartData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            pendingData = data;
        }

        // 3DBattleScene의 초기화 담당 코드에서 한 번 호출하고 결과를 보관합니다.
        public static Battle3DStartData TakeOrDefault()
        {
            Battle3DStartData data = pendingData ?? Battle3DStartData.CreateDefault();
            pendingData = null;
            return data;
        }

        // Domain Reload가 꺼져 있어도 이전 Play의 데이터가 남지 않도록 초기화합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            pendingData = null;
        }
    }
}
