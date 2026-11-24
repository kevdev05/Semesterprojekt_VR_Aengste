
using UnityEngine;

/// <summary>
/// Turns an NPC's head (on top of its animation) so that its EYES look at the target,
/// e.g. the VR camera (= the player's eyes).
/// The aim is calculated from the eye position, not from the head bone's pivot (which sits
/// at the base of the skull, ~10 cm below the eyes and would make the NPC look over the target).
/// </summary>
public class HeadLookAt : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Head bone. Leave empty to use the humanoid Head bone.")]
    public Transform head;
    [Tooltip("What to look at. Leave empty to use the main camera (the VR headset).")]
    public Transform target;

    [Header("Settings")]
    public float rotationSpeed = 5f;
    public float maxYaw = 70f;
    public float maxPitch = 35f;
    [Tooltip("Used only if the model has no eye bones: eye position relative to the head bone, " +
             "in metres (up, forward) in the character's frame.")]
    public Vector2 fallbackEyeOffset = new Vector2(0.09f, 0.09f);

    Transform body;              // character root: defines forward/up for yaw/pitch limits
    Transform leftEye, rightEye;
    Vector3 faceAxisLocal;       // head-local axis the face looks along
    Vector3 eyeOffsetLocal;      // fallback eye position in head-local space
    float yaw, pitch;            // current (smoothed) offset in degrees

    void Start()
    {
        var anim = GetComponentInParent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        body = anim != null ? anim.transform : transform;

        if (anim != null && anim.isHuman)
        {
            if (head == null) head = anim.GetBoneTransform(HumanBodyBones.Head);
            leftEye = anim.GetBoneTransform(HumanBodyBones.LeftEye);
            rightEye = anim.GetBoneTransform(HumanBodyBones.RightEye);
        }
        if (target == null && Camera.main != null) target = Camera.main.transform;
        if (head == null) return;

        // Which way does this rig's head bone face? (Rigs differ, so measure it.)
        faceAxisLocal = head.InverseTransformDirection(body.forward);

        Vector3 eyeWorld = head.position + body.up * fallbackEyeOffset.x + body.forward * fallbackEyeOffset.y;
        eyeOffsetLocal = head.InverseTransformPoint(eyeWorld);
    }

    Vector3 EyePosition()
    {
        if (leftEye != null && rightEye != null) return (leftEye.position + rightEye.position) * 0.5f;
        if (leftEye != null) return leftEye.position;
        if (rightEye != null) return rightEye.position;
        return head.TransformPoint(eyeOffsetLocal);
    }

    static void ToAngles(Vector3 d, out float y, out float p)
    {
        y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        p = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    static Vector3 FromAngles(float y, float p) => Quaternion.Euler(p, y, 0f) * Vector3.forward;

    void LateUpdate()
    {
        if (head == null || target == null) return;

        // Face direction from the animation, in the character's frame.
        Vector3 face = body.InverseTransformDirection(head.rotation * faceAxisLocal).normalized;
        ToAngles(face, out float faceYaw, out float facePitch);

        Vector3 headPos = head.position;
        Vector3 eye = EyePosition();
        Quaternion faceRot = Quaternion.LookRotation(face, Vector3.up);

        // Solve twice: turning the head moves the eyes, so refine once with the moved eye position.
        float wantYaw = 0f, wantPitch = 0f;
        for (int i = 0; i < 2; i++)
        {
            Quaternion r = WorldOffset(faceRot, faceYaw + wantYaw, facePitch + wantPitch);
            Vector3 movedEye = headPos + r * (eye - headPos);
            Vector3 dir = body.InverseTransformDirection(target.position - movedEye).normalized;
            ToAngles(dir, out float y, out float p);
            wantYaw = Mathf.Clamp(Mathf.DeltaAngle(faceYaw, y), -maxYaw, maxYaw);
            wantPitch = Mathf.Clamp(Mathf.DeltaAngle(facePitch, p), -maxPitch, maxPitch);
        }

        float k = 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime);
        yaw = Mathf.Lerp(yaw, wantYaw, k);
        pitch = Mathf.Lerp(pitch, wantPitch, k);

        head.rotation = WorldOffset(faceRot, faceYaw + yaw, facePitch + pitch) * head.rotation;
    }

    /// <summary>World rotation that turns the animated face direction to the given yaw/pitch (no added roll).</summary>
    Quaternion WorldOffset(Quaternion faceRot, float y, float p)
    {
        Quaternion want = Quaternion.LookRotation(FromAngles(y, p), Vector3.up);
        Quaternion local = want * Quaternion.Inverse(faceRot);          // in the character's frame
        return body.rotation * local * Quaternion.Inverse(body.rotation); // to world
    }
}
