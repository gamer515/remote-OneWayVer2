using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    [CreateAssetMenu(menuName = "Battle/New Player Attack Set")]
    public sealed class NewPlayerAttackSet : ScriptableObject
    {
        [Serializable] public class Motion
        {
            public string label;
            public AnimationClip clip;
            public int sourceFirstFrame, sourceChainFrame;
            public float chainFrame;
            public Vector2[] damageWindows;
            public AnimationCurve forward = AnimationCurve.Linear(0, 0, 1, 0);
            public AnimationCurve sideways = AnimationCurve.Linear(0, 0, 1, 0);
            public AnimationCurve jump = AnimationCurve.Linear(0, 0, 1, 0);
            public float Frames => clip.length * 30f;
        }
        public Motion[] attacks;
        public int[] order = {4, 1, 2, 5, 4, 3, 0};
        public Motion[] links;
        public Motion entry;
        public AnimationClip idle;
    }
}
