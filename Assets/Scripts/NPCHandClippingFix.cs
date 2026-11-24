using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps an NPC's hands from sinking into its own thighs, belly and chest (incl. clothing),
/// on top of ANY animation. Works for every humanoid NPC without per-animation setup.
///
/// How it works:
///  - The body is approximated by a few capsules (thighs, belly, chest) built from the humanoid bones.
///    Their thickness is measured from the character's meshes (clothes included) with
///    "Fit body parts to mesh" (right-click the component header). The values are saved,
///    so nothing is measured at runtime.
///  - After the animation each frame, if a hand is inside a capsule, the arm is bent with
///    two-bone IK so the hand rests on the surface instead. The hand keeps its animated rotation.
///
/// Tuning per NPC: raise "Padding" for bulky clothes, or edit a body part's radius.
/// Select the NPC to see the capsules in the Scene view.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // after other LateUpdate scripts that move bones
public class NPCHandClippingFix : MonoBehaviour
{
    [System.Serializable]
    public class BodyPart
    {
        public string name;
        public bool enabled = true;
        public HumanBodyBones from;
        public HumanBodyBones to;
        [Tooltip("Thickness (m) from the bone line to the outside of the clothes.")]
        public float radius = 0.08f;
        [Tooltip("Shift (m) of the capsule from the bone line to the real centre of the body part, " +
                 "in the 'from' bone's rotation space. Set by 'Fit body parts to mesh'.")]
        public Vector3 centerOffset;
    }

    [Tooltip("Extra space (m) kept between the hand and the body/clothes.")]
    public float padding = 0.01f;
    [Tooltip("Hand thickness (m), measured from the palm centre.")]
    public float handRadius = 0.025f;
    [Range(0f, 1f)] public float weight = 1f;
    [Tooltip("How quickly the correction follows (higher = snappier, lower = smoother).")]
    public float smoothing = 20f;
    public bool fixLeftHand = true;
    public bool fixRightHand = true;

    public List<BodyPart> bodyParts = new List<BodyPart>();

    class Arm
    {
        public Transform upper, lower, hand;
        public Vector3 palmLocal;   // palm centre in hand-local space
        public Vector3 push;        // smoothed correction (world)
    }

    Animator anim;
    Arm left, right;
    Transform[] partFrom, partTo;

    void Start()
    {
        anim = FindAnimator();
        if (anim == null || !anim.isHuman)
        {
            Debug.LogWarning($"NPCHandClippingFix on '{name}': needs a humanoid Animator.", this);
            enabled = false;
            return;
        }
        if (bodyParts.Count == 0) bodyParts = DefaultParts();

        left = MakeArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal);
        right = MakeArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);

        partFrom = new Transform[bodyParts.Count];
        partTo = new Transform[bodyParts.Count];
        for (int i = 0; i < bodyParts.Count; i++)
        {
            partFrom[i] = Bone(bodyParts[i].from);
            partTo[i] = Bone(bodyParts[i].to);
        }
    }

    void LateUpdate()
    {
        if (anim == null || partFrom == null) return;
        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        if (fixLeftHand) FixArm(left, k);
        if (fixRightHand) FixArm(right, k);
    }

    void FixArm(Arm arm, float k)
    {
        if (arm == null) return;

        // How far must the palm move to be outside every body part?
        Vector3 palm = arm.hand.TransformPoint(arm.palmLocal);
        Vector3 push = Vector3.zero;
        for (int iter = 0; iter < 2; iter++) // twice: getting out of one part can push into another
        {
            for (int i = 0; i < bodyParts.Count; i++)
            {
                if (!bodyParts[i].enabled || partFrom[i] == null || partTo[i] == null) continue;
                Vector3 p = palm + push;
                Vector3 off = partFrom[i].rotation * bodyParts[i].centerOffset;
                Vector3 c = ClosestOnSegment(partFrom[i].position + off, partTo[i].position + off, p);
                Vector3 d = p - c;
                float dist = d.magnitude;
                float need = bodyParts[i].radius + padding + handRadius;
                if (dist >= need) continue;
                Vector3 dir = dist > 1e-4f ? d / dist : anim.transform.forward;
                push += dir * (need - dist);
            }
        }

        arm.push = Vector3.Lerp(arm.push, push * weight, k);
        if (arm.push.sqrMagnitude < 1e-8f) return;

        SolveTwoBone(arm.upper, arm.lower, arm.hand, arm.hand.position + arm.push);
    }

    /// <summary>Bends shoulder + elbow so the hand reaches target. Keeps the hand's world rotation.</summary>
    static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
    {
        Quaternion handRot = c.rotation;
        Vector3 A = a.position, B = b.position, C = c.position;
        float lab = (B - A).magnitude, lbc = (C - B).magnitude;
        if (lab < 1e-4f || lbc < 1e-4f) return;
        float lat = Mathf.Clamp((target - A).magnitude, Mathf.Abs(lab - lbc) + 1e-3f, (lab + lbc) * 0.999f);

        // 1) Elbow angle that gives the needed shoulder-to-hand distance.
        float cur = Vector3.Angle(A - B, C - B);
        float want = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2f * lab * lbc), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 axis = Vector3.Cross(A - B, C - B);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(C - A, Vector3.up);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
        axis.Normalize();

        Quaternion elbow = b.rotation;
        b.rotation = Quaternion.AngleAxis(want - cur, axis) * elbow;
        if (Mathf.Abs(Vector3.Angle(A - B, c.position - B) - want) > 0.5f)
            b.rotation = Quaternion.AngleAxis(cur - want, axis) * elbow; // other direction

        // 2) Swing the upper arm so the hand lands on the target.
        a.rotation = Quaternion.FromToRotation(c.position - A, target - A) * a.rotation;

        c.rotation = handRot;
    }

    static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
        return a + ab * t;
    }

    Arm MakeArm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, HumanBodyBones middleFinger)
    {
        var arm = new Arm { upper = Bone(upper), lower = Bone(lower), hand = Bone(hand) };
        if (arm.upper == null || arm.lower == null || arm.hand == null) return null;

        var finger = Bone(middleFinger);
        Vector3 palm = finger != null
            ? (arm.hand.position + finger.position) * 0.5f
            : arm.hand.position + (arm.hand.position - arm.lower.position).normalized * 0.04f;
        arm.palmLocal = arm.hand.InverseTransformPoint(palm);
        return arm;
    }

    Transform Bone(HumanBodyBones b) => anim != null ? anim.GetBoneTransform(b) : null;

    Animator FindAnimator()
    {
        var a = GetComponent<Animator>();
        if (a == null) a = GetComponentInChildren<Animator>();
        if (a == null) a = GetComponentInParent<Animator>();
        return a;
    }

    static List<BodyPart> DefaultParts() => new List<BodyPart>
    {
        new BodyPart { name = "Left thigh",  from = HumanBodyBones.LeftUpperLeg,  to = HumanBodyBones.LeftLowerLeg,  radius = 0.08f },
        new BodyPart { name = "Right thigh", from = HumanBodyBones.RightUpperLeg, to = HumanBodyBones.RightLowerLeg, radius = 0.08f },
        new BodyPart { name = "Belly",       from = HumanBodyBones.Hips,          to = HumanBodyBones.Chest,         radius = 0.12f },
        new BodyPart { name = "Chest",       from = HumanBodyBones.Chest,         to = HumanBodyBones.Neck,          radius = 0.12f },
    };

    // ---------------------------------------------------------------- fitting (Editor)

    /// <summary>
    /// Measures each body part's thickness from the character's skinned meshes (clothes included)
    /// in the current pose and stores it in the radius fields. Returns a short report.
    /// </summary>
    [ContextMenu("Fit body parts to mesh")]
    public string FitToMesh()
    {
        anim = FindAnimator();
        if (anim == null || !anim.isHuman) return $"{name}: no humanoid Animator";
        if (bodyParts.Count == 0) bodyParts = DefaultParts();

        // Every humanoid bone transform, so bone ownership stops at the next humanoid bone.
        var human = new HashSet<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var t = anim.GetBoneTransform((HumanBodyBones)i);
            if (t != null) human.Add(t);
        }

        // Which bones' vertices belong to which part.
        var owner = new Dictionary<Transform, int>();
        var ends = new (Vector3 a, Vector3 b)[bodyParts.Count];
        for (int i = 0; i < bodyParts.Count; i++)
        {
            var fromBone = anim.GetBoneTransform(bodyParts[i].from);
            var toBone = anim.GetBoneTransform(bodyParts[i].to);
            if (fromBone == null || toBone == null) continue; // optional bone (e.g. Chest) missing on this rig
            ends[i] = (fromBone.position, toBone.position);
            foreach (var hb in OwnBones(bodyParts[i]))
            {
                var bone = anim.GetBoneTransform(hb);
                if (bone != null) Claim(bone, i, owner, human, true);
            }
        }

        // Per part: every vertex as its offset from the bone line.
        var pts = new List<Vector3>[bodyParts.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = new List<Vector3>();

        var baked = new Mesh();
        foreach (var smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !smr.enabled) continue;
            var bones = smr.bones;
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length != mesh.vertexCount) continue;

            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            // Depending on the rig's scale setup the baked mesh may or may not include the
            // renderer's scale: pick the matrix whose result matches the renderer's real size.
            var noScale = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            var full = smr.transform.localToWorldMatrix;
            var toWorld = SizeError(verts, noScale, smr) <= SizeError(verts, full, smr) ? noScale : full;

            for (int v = 0; v < verts.Length; v++)
            {
                int bi = weights[v].boneIndex0;
                if (bi < 0 || bi >= bones.Length || bones[bi] == null) continue;
                if (!owner.TryGetValue(bones[bi], out int part)) continue;

                var (a, b) = ends[part];
                Vector3 w = toWorld.MultiplyPoint3x4(verts[v]);
                Vector3 ab = b - a;
                float t = Vector3.Dot(w - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f);
                if (t < 0.1f || t > 0.9f) continue; // ignore joint areas
                Vector3 off = w - (a + ab * t);
                if (off.magnitude < 0.35f) pts[part].Add(off);
            }
        }
        DestroyImmediate(baked);

        var report = new System.Text.StringBuilder(name + ":");
        for (int i = 0; i < bodyParts.Count; i++)
        {
            var list = pts[i];
            if (list.Count < 20) { report.Append($" {bodyParts[i].name}=kept {bodyParts[i].radius:F3}"); continue; }

            // 1) Shift the capsule to the centre of the flesh/clothes (the bone is usually not centred).
            Vector3 centre = Vector3.zero;
            foreach (var o in list) centre += o;
            centre /= list.Count;

            // 2) Radius = typical distance from that centre line (median: robust against hems, buttocks etc.).
            var d = new List<float>(list.Count);
            foreach (var o in list) d.Add((o - centre).magnitude);
            d.Sort();
            float radius = d[d.Count / 2];

            var fromBone = anim.GetBoneTransform(bodyParts[i].from);
            bodyParts[i].centerOffset = Quaternion.Inverse(fromBone.rotation) * centre;
            bodyParts[i].radius = radius;
            report.Append($" {bodyParts[i].name}=r{radius:F3}/shift{centre.magnitude:F3}");
        }
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        return report.ToString();
    }

    static float SizeError(Vector3[] verts, Matrix4x4 m, SkinnedMeshRenderer smr)
    {
        if (verts.Length == 0) return float.MaxValue;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (var v in verts) { var w = m.MultiplyPoint3x4(v); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
        return Mathf.Abs((max - min).magnitude - smr.bounds.size.magnitude);
    }

    static IEnumerable<HumanBodyBones> OwnBones(BodyPart p)
    {
        switch (p.from)
        {
            case HumanBodyBones.Hips: yield return HumanBodyBones.Hips; yield return HumanBodyBones.Spine; break;
            case HumanBodyBones.Chest: yield return HumanBodyBones.Chest; yield return HumanBodyBones.UpperChest; break;
            default: yield return p.from; break;
        }
    }

    static void Claim(Transform t, int part, Dictionary<Transform, int> owner, HashSet<Transform> human, bool isRoot)
    {
        if (!isRoot && human.Contains(t)) return; // next humanoid bone belongs to someone else
        owner[t] = part;
        foreach (Transform c in t) Claim(c, part, owner, human, false);
    }

    void OnDrawGizmosSelected()
    {
        var a = FindAnimator();
        if (a == null || !a.isHuman) return;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        foreach (var p in bodyParts)
        {
            if (!p.enabled) continue;
            var f = a.GetBoneTransform(p.from);
            var t = a.GetBoneTransform(p.to);
            if (f == null || t == null) continue;
            Vector3 off = f.rotation * p.centerOffset;
            Vector3 fp = f.position + off, tp = t.position + off;
            Vector3 dir = (tp - fp).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(dir, Vector3.up).normalized : Vector3.right;
            Vector3 side2 = Vector3.Cross(dir, side);
            Gizmos.DrawWireSphere(fp, p.radius);
            Gizmos.DrawWireSphere(tp, p.radius);
            Gizmos.DrawLine(fp + side * p.radius, tp + side * p.radius);
            Gizmos.DrawLine(fp - side * p.radius, tp - side * p.radius);
            Gizmos.DrawLine(fp + side2 * p.radius, tp + side2 * p.radius);
            Gizmos.DrawLine(fp - side2 * p.radius, tp - side2 * p.radius);
        }
    }
}
