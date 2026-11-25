
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

    [Header("Laptop")]
    [Tooltip("Point to look at while typing (e.g. an empty object on the screen). Leave empty if there is no laptop.")]
    public Transform laptop;
    [Tooltip("Seconds to turn the head to the laptop and back. (Random glances have their own, calm speed.)")]
    public float turnTime = 1.5f;

    [Header("Head gestures (nod, head shake)")]
    [Tooltip("Animator states with this Tag keep their head movement: it is added on top of looking at the target. " +
             "All other states behave as before.")]
    public string gestureTag = "HeadGesture";
    [Tooltip("1 = as strong as in the animation, 0 = no head gestures.")]
    [Range(0f, 2f)] public float gestureStrength = 1f;

    const float GestureFadeOutTime = 0.25f;   // seconds

    Animator animator;
    Transform neckBase;          // bone the head gesture is measured against
    Quaternion restPose = Quaternion.identity;      // head pose before the gesture, relative to neckBase
    Quaternion gestureStart = Quaternion.identity;  // first head pose of the gesture, relative to neckBase
    float holdWeight;            // 1 during a gesture, fades to 0 afterwards

    Transform body;              // character root: defines forward/up for yaw/pitch limits
    Transform leftEye, rightEye;
    Vector3 faceAxisLocal;       // head-local axis the face looks along
    Vector3 eyeOffsetLocal;      // fallback eye position in head-local space
    float yaw, pitch;            // current (smoothed) offset in degrees

    // Turning from one target to another: the direction is blended with a soft start and stop.
    enum Look { Patient, Laptop, Person, Point }
    Look look = Look.Patient;
    HeadLookAt person;                     // Look.Person: another NPC
    Vector3 point;                         // Look.Point: a place in the room
    float turn = 1f;                       // 0..1 progress of the turn
    float turnDuration = 1f;               // seconds
    float fromYaw, fromPitch;              // direction at the start of the turn (character's frame)
    float lookYaw, lookPitch;              // direction right now
    float lastGlanceYaw = 999f, lastGlancePitch;
    // While looking at the laptop, the small head movements of the animation are kept.
    const float SlowFaceSpeed = 0.5f;
    float slowFaceYaw, slowFacePitch, keepMotion;

    /// <summary>Is the laptop the main thing to look at (typing)?</summary>
    public bool AtLaptop { get; private set; }

    /// <summary>Is the NPC glancing somewhere else for a moment?</summary>
    public bool IsGlancing { get; private set; }

    /// <summary>Seconds the current head turn takes.</summary>
    public float TurnDuration => turnDuration;

    /// <summary>Look at the laptop (true) or at the patient (false). The head turns there smoothly.</summary>
    public void LookAtLaptop(bool on)
    {
        on = on && laptop != null;
        if (on == AtLaptop) return;
        AtLaptop = on;
        IsGlancing = false;
        TurnTo(on ? Look.Laptop : Look.Patient, turnTime);
    }

    /// <summary>
    /// Look somewhere else for a moment: slightly aside, at another NPC or at the table. While typing: up at the patient.
    /// The head never turns further than maxAngle away from the patient.
    /// </summary>
    public bool StartGlance(System.Collections.Generic.List<HeadLookAt> others, float maxAngle)
    {
        if (head == null || target == null || IsGlancing) return false;

        Vector3 eye = EyePosition();
        AnglesTo(eye, target.position, out float patientYaw, out float patientPitch);
        patientYaw = Mathf.Clamp(patientYaw, -maxYaw, maxYaw);

        if (AtLaptop)
        {
            IsGlancing = true;
            TurnTo(Look.Patient, GlanceTime(patientYaw, patientPitch));
            return true;
        }

        float towardsFront = patientYaw > 0f ? -1f : 1f;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float y, p;
            HeadLookAt who = null;
            float kind = Random.value;
            if (kind < 0.58f)                    // slightly aside: mostly a little, sometimes more
            {
                float angle = kind < 0.44f ? Random.Range(10f, 20f) : Random.Range(20f, 35f);
                float side = Random.value < 0.85f ? towardsFront : -towardsFront;
                y = patientYaw + side * angle;
                p = patientPitch + Random.Range(-3f, 10f);
            }
            else if (kind < 0.92f)               // another NPC
            {
                who = others[Random.Range(0, others.Count)];
                if (who == this || who == null || who.head == null) continue;
                AnglesTo(eye, who.EyePosition(), out y, out p);
            }
            else                                 // the table in front
            {
                y = Mathf.MoveTowards(patientYaw, Random.Range(-10f, 10f), Random.Range(25f, maxAngle));
                p = Random.Range(12f, 22f);
            }

            float turnAngle = AngleBetween(patientYaw, patientPitch, y, p);
            if (Mathf.Abs(y) > maxYaw || Mathf.Abs(p) > maxPitch || turnAngle < 5f || turnAngle > maxAngle) continue;
            if (turnAngle > 40f && Random.value > 0.4f) continue;                        // large turns are rare
            if (AngleBetween(lastGlanceYaw, lastGlancePitch, y, p) < 6f) continue;   // not the same place twice in a row

            lastGlanceYaw = y;
            lastGlancePitch = p;
            person = who;
            point = eye + body.TransformDirection(FromAngles(y, p)) * 3f;
            IsGlancing = true;
            TurnTo(who != null ? Look.Person : Look.Point, GlanceTime(y, p));
            return true;
        }
        return false;
    }

    /// <summary>Back to the patient (or to the laptop while typing).</summary>
    public void EndGlance(float minTime)
    {
        if (!IsGlancing) return;
        IsGlancing = false;
        AnglesTo(EyePosition(), (AtLaptop ? laptop : target).position, out float y, out float p);
        TurnTo(AtLaptop ? Look.Laptop : Look.Patient, Mathf.Max(minTime, GlanceTime(Mathf.Clamp(y, -maxYaw, maxYaw), p)));
    }

    void TurnTo(Look to, float seconds)
    {
        look = to;
        fromYaw = lookYaw;
        fromPitch = lookPitch;
        turn = 0f;
        turnDuration = Mathf.Max(seconds, 0.01f);
    }

    /// <summary>Calm turning speed: small turns about 1 second, large ones about 2.</summary>
    float GlanceTime(float y, float p) =>
        (0.9f + AngleBetween(lookYaw, lookPitch, y, p) / 50f) * Random.Range(0.9f, 1.2f);

    static float AngleBetween(float y1, float p1, float y2, float p2) =>
        Vector3.Angle(FromAngles(y1, p1), FromAngles(y2, p2));

    void AnglesTo(Vector3 eye, Vector3 worldPoint, out float y, out float p) =>
        ToAngles(body.InverseTransformDirection(worldPoint - eye).normalized, out y, out p);

    Vector3 LookPoint()
    {
        switch (look)
        {
            case Look.Laptop: return laptop.position;
            case Look.Person: return person.EyePosition();
            case Look.Point: return point;
            default: return target.position;
        }
    }

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

            animator = anim;
            neckBase = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (neckBase == null) neckBase = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (neckBase == null) neckBase = anim.GetBoneTransform(HumanBodyBones.Spine);
        }
        if (neckBase == null) neckBase = body;
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

        // Split the animated head rotation into a resting pose (aimed at the target) and a gesture (kept on top).
        Quaternion animated = head.rotation;
        SplitGesture(animated, out Quaternion rest, out Quaternion gesture);

        // Face direction of the resting pose, in the character's frame.
        Vector3 face = body.InverseTransformDirection(rest * faceAxisLocal).normalized;
        ToAngles(face, out float faceYaw, out float facePitch);

        Vector3 headPos = head.position;
        Vector3 eye = headPos + rest * (Quaternion.Inverse(animated) * (EyePosition() - headPos));
        Quaternion faceRot = Quaternion.LookRotation(face, Vector3.up);

        // Laptop: aim the slow average of the face, so the animation's own head movement stays on top.
        bool atLaptop = look == Look.Laptop;
        float slow = 1f - Mathf.Exp(-SlowFaceSpeed * Time.deltaTime);
        keepMotion = Mathf.MoveTowards(keepMotion, atLaptop ? 1f : 0f, Time.deltaTime / turnDuration);
        slowFaceYaw = keepMotion > 0f ? Mathf.LerpAngle(slowFaceYaw, faceYaw, slow) : faceYaw;
        slowFacePitch = keepMotion > 0f ? Mathf.LerpAngle(slowFacePitch, facePitch, slow) : facePitch;
        float refYaw = Mathf.LerpAngle(faceYaw, slowFaceYaw, keepMotion);
        float refPitch = Mathf.LerpAngle(facePitch, slowFacePitch, keepMotion);

        turn = Mathf.MoveTowards(turn, 1f, Time.deltaTime / turnDuration);
        float blend = turn * turn * turn * (turn * (6f * turn - 15f) + 10f); // soft start and stop
        Vector3 lookPoint = LookPoint();

        // Solve twice: turning the head moves the eyes, so refine once with the moved eye position.
        float wantYaw = 0f, wantPitch = 0f;
        for (int i = 0; i < 2; i++)
        {
            Quaternion r = WorldOffset(faceRot, faceYaw + wantYaw, facePitch + wantPitch);
            Vector3 movedEye = headPos + r * (eye - headPos);
            Vector3 dir = body.InverseTransformDirection(lookPoint - movedEye).normalized;
            ToAngles(dir, out float y, out float p);
            lookYaw = Mathf.LerpAngle(fromYaw, y, blend);
            lookPitch = Mathf.LerpAngle(fromPitch, p, blend);
            wantYaw = Mathf.Clamp(Mathf.DeltaAngle(refYaw, lookYaw), -maxYaw, maxYaw);
            wantPitch = Mathf.Clamp(Mathf.DeltaAngle(refPitch, lookPitch), -maxPitch, maxPitch);
        }

        float k = 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime);
        yaw = Mathf.Lerp(yaw, wantYaw, k);
        pitch = Mathf.Lerp(pitch, wantPitch, k);

        head.rotation = WorldOffset(faceRot, faceYaw + yaw, facePitch + pitch) * rest * gesture;
    }

    /// <summary>
    /// Splits the animated head rotation into a resting pose (which gets aimed at the target)
    /// and a gesture (kept on top). Outside a gesture: resting pose = animation, no gesture.
    /// </summary>
    void SplitGesture(Quaternion animated, out Quaternion rest, out Quaternion gesture)
    {
        Quaternion relative = Quaternion.Inverse(neckBase.rotation) * animated;
        bool active = InGesture(out bool entering);

        if (active) holdWeight = 1f;
        else holdWeight = Mathf.MoveTowards(holdWeight, 0f, Time.deltaTime / GestureFadeOutTime);

        if (holdWeight <= 0f) restPose = relative;                  // pose to return to: the one before the gesture
        if (holdWeight <= 0f || entering) gestureStart = relative;  // a gesture is measured from its own first pose

        rest = neckBase.rotation * Quaternion.Slerp(relative, restPose, holdWeight);
        gesture = Quaternion.SlerpUnclamped(Quaternion.identity,
            Quaternion.Inverse(gestureStart) * relative, holdWeight * gestureStrength);
    }

    /// <summary>Is any animator layer playing (or blending into) a state with the gesture tag?</summary>
    bool InGesture(out bool entering)
    {
        entering = false;
        if (animator == null || !animator.isInitialized || string.IsNullOrEmpty(gestureTag)) return false;

        for (int i = 0; i < animator.layerCount; i++)
        {
            bool blending = animator.IsInTransition(i);
            AnimatorStateInfo state = blending
                ? animator.GetNextAnimatorStateInfo(i)
                : animator.GetCurrentAnimatorStateInfo(i);
            if (!state.IsTag(gestureTag)) continue;
            entering = blending;
            return true;
        }
        return false;
    }

    /// <summary>World rotation that turns the animated face direction to the given yaw/pitch (no added roll).</summary>
    Quaternion WorldOffset(Quaternion faceRot, float y, float p)
    {
        Quaternion want = Quaternion.LookRotation(FromAngles(y, p), Vector3.up);
        Quaternion local = want * Quaternion.Inverse(faceRot);          // in the character's frame
        return body.rotation * local * Quaternion.Inverse(body.rotation); // to world
    }
}
