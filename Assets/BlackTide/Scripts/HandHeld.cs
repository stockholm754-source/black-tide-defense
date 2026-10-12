using UnityEngine;

namespace BlackTide
{
    // 手持小武器（青岚的手枪）：枪管顺着"手肘 → 手腕"的方向，枪柄朝下。
    // 伸手射击时枪口自然朝前，垂手时朝下；不需要参考模型。
    public class HandHeld : MonoBehaviour
    {
        public Transform hand, elbow;

        public static HandHeld Attach(Transform parent, Animator anim, HumanBodyBones handBone, HumanBodyBones elbowBone, GameObject prefab, Material mat, float length, bool headFar = false, float gripAt = 0.22f)
        {
            var hand = anim.GetBoneTransform(handBone);
            var elbow = anim.GetBoneTransform(elbowBone);
            if (!hand || !elbow || !prefab) return null;
            var holder = new GameObject("Pistol").transform;
            holder.SetParent(parent, false);
            var h = holder.gameObject.AddComponent<HandHeld>();
            h.hand = hand; h.elbow = elbow;

            var m = Object.Instantiate(prefab, holder).transform;
            m.localPosition = Vector3.zero; m.localRotation = Quaternion.identity; m.localScale = Vector3.one;
            var verts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var M = holder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var p in mf.sharedMesh.vertices) verts.Add(M.MultiplyPoint3x4(p));
            }
            var s = WeaponFit.Analyze(verts.ToArray(), false);
            // 枪口方向：枪柄挂在后端，后端在次轴上的跨度更大；让 axis 指向另一端（枪口）
            float spanLo = 0f, spanHi = 0f;
            foreach (var p in verts)
            {
                float t = Vector3.Dot(p - s.center, s.axis);
                float w = Mathf.Abs(Vector3.Dot(p - s.center, s.wide));
                if (t < s.min + s.Len * 0.35f) spanLo = Mathf.Max(spanLo, w);
                if (t > s.max - s.Len * 0.35f) spanHi = Mathf.Max(spanHi, w);
            }
            // headFar（锤、长柄、法杖、号角，2026-10-09）：宽的一头是锤头 / 杖头，要朝外，握在细的那一端——和手枪相反
            if ((spanHi > spanLo) != headFar) { s.axis = -s.axis; float lo = s.min; s.min = -s.max; s.max = -lo; }
            // 枪柄方向：后半段在次轴上的偏向（三阶矩）
            float m3 = 0f;
            foreach (var p in verts)
            {
                float t = Vector3.Dot(p - s.center, s.axis);
                if (t > s.min + s.Len * 0.5f) continue;
                float w = Vector3.Dot(p - s.center, s.wide);
                m3 += w * w * w;
            }
            Vector3 down = m3 >= 0f ? s.wide : -s.wide;
            float scale = length / Mathf.Max(1e-4f, s.Len);
            Quaternion R = Quaternion.Inverse(Quaternion.LookRotation(s.axis, -down));   // 枪口 → +Z，枪柄 → -Y
            Vector3 grip = s.center + s.axis * (s.min + s.Len * gripAt);   // 握点：默认靠后 22%（长柄、法杖握在 30% 左右）
            m.localRotation = R;
            m.localScale = Vector3.one * scale;
            m.localPosition = -(R * grip) * scale;
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return h;
        }

        public Vector3 Muzzle => transform.position + transform.forward * 0.3f;

        void LateUpdate()
        {
            if (!hand || !elbow) return;
            Vector3 dir = hand.position - elbow.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, dir);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(transform.root.forward, dir);
            transform.SetPositionAndRotation(hand.position + dir * 0.05f, Quaternion.LookRotation(dir, up));
        }
    }
}
