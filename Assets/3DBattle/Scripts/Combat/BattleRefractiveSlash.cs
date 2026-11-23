using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleRefractiveSlash : MonoBehaviour
    {
        public float duration = .35f;
        float age;
        Renderer surface;
        MaterialPropertyBlock properties;
        void Awake()
        {
            surface = GetComponentInChildren<Renderer>();
            properties = new MaterialPropertyBlock();
            Apply(0);
        }
        public void Initialize(float seconds) { duration = Mathf.Max(.01f, seconds); age = 0; Apply(0); }
        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            Apply(Mathf.SmoothStep(0, 1, t / .12f) * (1 - Mathf.SmoothStep(.45f, 1, t)));
        }
        void Apply(float strength)
        {
            if (!surface) return;
            surface.GetPropertyBlock(properties);
            properties.SetFloat("_Visibility", strength);
            surface.SetPropertyBlock(properties);
        }
    }
}
