using System.Collections.Generic;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Correct the rendered skeleton, never the battle root or jump trajectory.
    // Clear last frame's correction before BossJumpMotionDebug manually samples its Animator.
    [DefaultExecutionOrder(-30)]
    public sealed class BossFootGrounding : MonoBehaviour
    {
        public SkinnedMeshRenderer skin;
        public Transform skeletonRoot;
        public Transform leftFoot, rightFoot;
        public Battle3DSceneController battle;
        [Min(0f)] public float soleClearance = .015f;
        public float AppliedLift { get; private set; }
        struct Probe { public Vector3 vertex; public BoneWeight weights; public bool left; }
        readonly List<Probe> probes = new List<Probe>();
        Matrix4x4[] bindPoses, matrices;
        Transform[] bones;
        Vector3 localCorrection;
        void Awake() => Initialize();
        void Initialize()
        {
            if (probes.Count > 0 || skin == null || skin.sharedMesh == null) return;
            var mesh = skin.sharedMesh;
            bones = skin.bones; bindPoses = mesh.bindposes; matrices = new Matrix4x4[bones.Length];
            var vertices = mesh.vertices; var weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i]; float left = 0, right = 0;
                Add(w.boneIndex0,w.weight0,ref left,ref right); Add(w.boneIndex1,w.weight1,ref left,ref right);
                Add(w.boneIndex2,w.weight2,ref left,ref right); Add(w.boneIndex3,w.weight3,ref left,ref right);
                if (left + right >= .5f) probes.Add(new Probe {vertex=vertices[i],weights=w,left=left>=right});
            }
        }
        void Add(int index,float weight,ref float left,ref float right)
        {
            if (weight <= 0 || index >= bones.Length) return;
            string name = bones[index].name;
            if (!name.Contains("Foot") && !name.Contains("Toe")) return;
            if (name.StartsWith("Left")) left += weight; else if (name.StartsWith("Right")) right += weight;
        }
        void Update() => RemoveCorrection();
        public void RemoveCorrection()
        {
            if (skeletonRoot != null) skeletonRoot.localPosition -= localCorrection;
            localCorrection = Vector3.zero; AppliedLift = 0;
        }
        void LateUpdate() => CorrectPose();
        public void CorrectPose()
        {
            if (skeletonRoot == null || skin == null || localCorrection != Vector3.zero) return;
            Initialize();
            if (probes.Count == 0) return;
            for (int i=0;i<bones.Length;i++) matrices[i]=bones[i].localToWorldMatrix*bindPoses[i];
            float leftMin=float.PositiveInfinity, rightMin=float.PositiveInfinity;
            foreach(var p in probes)
            {
                var w=p.weights;
                Vector3 point=matrices[w.boneIndex0].MultiplyPoint3x4(p.vertex)*w.weight0+
                    matrices[w.boneIndex1].MultiplyPoint3x4(p.vertex)*w.weight1+
                    matrices[w.boneIndex2].MultiplyPoint3x4(p.vertex)*w.weight2+
                    matrices[w.boneIndex3].MultiplyPoint3x4(p.vertex)*w.weight3;
                if(p.left) leftMin=Mathf.Min(leftMin,point.y); else rightMin=Mathf.Min(rightMin,point.y);
            }
            float lift=0;
            if(TryFloor(leftFoot,out float leftFloor)) lift=Mathf.Max(lift,leftFloor+soleClearance-leftMin);
            if(TryFloor(rightFoot,out float rightFloor)) lift=Mathf.Max(lift,rightFloor+soleClearance-rightMin);
            AppliedLift=lift;
            if(lift<=0) return;
            localCorrection=skeletonRoot.parent.InverseTransformVector(Vector3.up*lift);
            skeletonRoot.localPosition+=localCorrection;
        }
        bool TryFloor(Transform foot,out float floor)
        {
            floor=float.NegativeInfinity;
            if(foot==null) return false;
            var boundary=battle!=null && battle.CurrentStage!=null ? battle.CurrentStage.boundary : null;
            foreach(var hit in Physics.RaycastAll(foot.position+Vector3.up*20,Vector3.down,100,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.normal.y<.5f || hit.collider.GetComponentInParent<BattleBoss>()!=null ||
                    hit.collider.GetComponentInParent<BattlePlayerController>()!=null) continue;
                if(boundary!=null && System.Array.IndexOf(boundary.walls,hit.collider)>=0) continue;
                floor=Mathf.Max(floor,hit.point.y);
            }
            return !float.IsNegativeInfinity(floor);
        }
        void OnDisable() => RemoveCorrection();
    }
}
