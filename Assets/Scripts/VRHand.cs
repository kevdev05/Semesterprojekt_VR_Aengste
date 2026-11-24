using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One VR hand, driven by a Quest controller. No rays, no teleport.
///  - Follows the controller's position and rotation.
///  - Shows a hand model (child object) whose fingers bend with the buttons:
///      Trigger -> index finger, Grip -> other fingers (+ part of the thumb).
///  - Grip button grabs the nearest "Grabbable" object within reach; releasing throws it.
///
/// Setup: put this on an empty object under "Camera Offset" (next to Main Camera),
/// and put the hand model (LeftHand / RightHand from the XR Hands samples) inside it.
/// </summary>
[DefaultExecutionOrder(-50)]
public class VRHand : MonoBehaviour
{
    public enum Side { Left, Right }

    [Header("Which hand")]
    public Side hand = Side.Left;

    [Header("Hand model alignment")]
    [Tooltip("Rotation (degrees) applied on top of the controller rotation so the hand model sits naturally " +
             "around the controller. Set for the LEFT hand; the right hand mirrors it automatically. " +
             "Can be tweaked live in Play mode.")]
    public Vector3 rotationOffset = new Vector3(90f, 0f, 90f);
    [Tooltip("Position offset (metres, in controller space) for the hand model. Set for the LEFT hand; X is mirrored for the right hand.")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("Grabbing")]
    [Tooltip("Point the hand grabs from (roughly the palm). Leave empty to create one automatically at the hand's origin.")]
    public Transform grabPoint;
    [Tooltip("How close (in metres) an object must be to the grab point to be grabbed.")]
    public float grabRadius = 0.1f;
    [Range(0.1f, 1f), Tooltip("How far the grip button must be pressed to grab.")]
    public float grabThreshold = 0.6f;
    [Range(0f, 1f), Tooltip("Below this, the object is released.")]
    public float releaseThreshold = 0.35f;
    [Tooltip("1 = throw with the real hand speed, 0 = objects just drop.")]
    public float throwStrength = 1f;

    [Header("Finger bending")]
    [Tooltip("Local axis the finger joints bend around. Fingers bend backwards -> flip the sign. Sideways -> try (0,1,0) or (0,0,1).")]
    public Vector3 fingerAxis = new Vector3(1f, 0f, 0f);
    [Tooltip("Local axis the thumb joints bend around.")]
    public Vector3 thumbAxis = new Vector3(1f, 0f, 0f);
    [Tooltip("Degrees for a fully closed finger: first (X), middle (Y) and last (Z) joint.")]
    public Vector3 fingerAngles = new Vector3(70f, 90f, 60f);
    [Tooltip("Degrees for a fully closed thumb: first (X), middle (Y) and last (Z) joint.")]
    public Vector3 thumbAngles = new Vector3(10f, 30f, 40f);
    [Range(0f, 1f)] public float thumbAmount = 0.6f;
    [Tooltip("How quickly the fingers follow the buttons.")]
    public float fingerSpeed = 15f;

    [Header("Testing without headset (Play mode)")]
    public bool useTestValues = false;
    [Range(0f, 1f)] public float testGrip;
    [Range(0f, 1f)] public float testTrigger;

    /// <summary>The object this hand is holding, or null.</summary>
    public Grabbable Held { get; private set; }

    class Finger { public Transform[] joints; public Quaternion[] rest; }

    Finger index, middle, ring, little, thumb;
    InputAction positionAction, rotationAction, gripAction, triggerAction;
    float gripSmoothed, triggerSmoothed;
    bool gripDown;

    Vector3 lastGrabPos;
    Quaternion lastGrabRot;
    Vector3 velocity, angularVelocity;

    void Awake()
    {
        if (grabPoint == null)
        {
            grabPoint = new GameObject("Grab Point").transform;
            grabPoint.SetParent(transform, false);
        }

        var bones = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!bones.ContainsKey(t.name)) bones.Add(t.name, t);

        string p = hand == Side.Left ? "L_" : "R_";
        index  = MakeFinger(bones, p + "IndexProximal",  p + "IndexIntermediate",  p + "IndexDistal");
        middle = MakeFinger(bones, p + "MiddleProximal", p + "MiddleIntermediate", p + "MiddleDistal");
        ring   = MakeFinger(bones, p + "RingProximal",   p + "RingIntermediate",   p + "RingDistal");
        little = MakeFinger(bones, p + "LittleProximal", p + "LittleIntermediate", p + "LittleDistal");
        thumb  = MakeFinger(bones, p + "ThumbMetacarpal", p + "ThumbProximal",     p + "ThumbDistal");
    }

    Finger MakeFinger(Dictionary<string, Transform> bones, params string[] names)
    {
        var f = new Finger { joints = new Transform[names.Length], rest = new Quaternion[names.Length] };
        for (int i = 0; i < names.Length; i++)
        {
            if (bones.TryGetValue(names[i], out var t))
            {
                f.joints[i] = t;
                f.rest[i] = t.localRotation;
            }
            else
            {
                Debug.LogWarning($"VRHand on '{name}': bone '{names[i]}' not found. Is 'Hand' set to the right side, " +
                                 "and is the hand model (LeftHand / RightHand) a child of this object?", this);
            }
        }
        return f;
    }

    void OnEnable()
    {
        string h = hand == Side.Left ? "{LeftHand}" : "{RightHand}";

        positionAction = new InputAction("Position", InputActionType.Value, expectedControlType: "Vector3");
        positionAction.AddBinding("<XRController>" + h + "/devicePosition");

        rotationAction = new InputAction("Rotation", InputActionType.Value, expectedControlType: "Quaternion");
        rotationAction.AddBinding("<XRController>" + h + "/deviceRotation");

        gripAction = new InputAction("Grip", InputActionType.Value, expectedControlType: "Axis");
        gripAction.AddBinding("<XRController>" + h + "/{Grip}");
        gripAction.AddBinding("<XRController>" + h + "/grip");

        triggerAction = new InputAction("Trigger", InputActionType.Value, expectedControlType: "Axis");
        triggerAction.AddBinding("<XRController>" + h + "/{Trigger}");
        triggerAction.AddBinding("<XRController>" + h + "/trigger");

        positionAction.Enable();
        rotationAction.Enable();
        gripAction.Enable();
        triggerAction.Enable();

        Application.onBeforeRender += UpdatePose;

        lastGrabPos = grabPoint.position;
        lastGrabRot = grabPoint.rotation;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= UpdatePose;

        if (Held != null) Held.Release(Vector3.zero, Vector3.zero);
        Held = null;
        gripDown = false;

        foreach (var a in new[] { positionAction, rotationAction, gripAction, triggerAction })
        {
            a?.Disable();
            a?.Dispose();
        }
    }

    /// <summary>Moves this object to where the controller is (relative to Camera Offset).</summary>
    void UpdatePose()
    {
        if (positionAction == null || positionAction.controls.Count == 0) return;
        Quaternion controllerRot = rotationAction.ReadValue<Quaternion>();
        bool right = hand == Side.Right;
        Vector3 rotOff = right ? new Vector3(rotationOffset.x, -rotationOffset.y, -rotationOffset.z) : rotationOffset;
        Vector3 posOff = right ? new Vector3(-positionOffset.x, positionOffset.y, positionOffset.z) : positionOffset;

        transform.localPosition = positionAction.ReadValue<Vector3>() + controllerRot * posOff;
        transform.localRotation = controllerRot * Quaternion.Euler(rotOff);
    }

    void Update()
    {
        UpdatePose();

        float grip = useTestValues ? testGrip : gripAction.ReadValue<float>();
        float trigger = useTestValues ? testTrigger : triggerAction.ReadValue<float>();

        // Hand speed, used for throwing.
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 pos = grabPoint.position;
        Quaternion rot = grabPoint.rotation;
        velocity = Vector3.Lerp(velocity, (pos - lastGrabPos) / dt, 0.5f);
        (rot * Quaternion.Inverse(lastGrabRot)).ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        Vector3 w = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
        angularVelocity = Vector3.Lerp(angularVelocity, w, 0.5f);
        lastGrabPos = pos;
        lastGrabRot = rot;

        // Grab / release with hysteresis so it doesn't flicker.
        if (!gripDown && grip >= grabThreshold)
        {
            gripDown = true;
            TryGrab();
        }
        else if (gripDown && grip <= releaseThreshold)
        {
            gripDown = false;
            if (Held != null)
            {
                var g = Held;
                Held = null;
                g.Release(velocity * throwStrength, angularVelocity * throwStrength);
            }
        }

        // Fingers.
        float k = 1f - Mathf.Exp(-fingerSpeed * Time.deltaTime);
        gripSmoothed = Mathf.Lerp(gripSmoothed, grip, k);
        triggerSmoothed = Mathf.Lerp(triggerSmoothed, trigger, k);
    }

    void LateUpdate()
    {
        Bend(index, triggerSmoothed, fingerAxis, fingerAngles);
        Bend(middle, gripSmoothed, fingerAxis, fingerAngles);
        Bend(ring, gripSmoothed, fingerAxis, fingerAngles);
        Bend(little, gripSmoothed, fingerAxis, fingerAngles);
        Bend(thumb, gripSmoothed * thumbAmount, thumbAxis, thumbAngles);
    }

    void TryGrab()
    {
        Grabbable best = null;
        float bestDist = float.MaxValue;

        foreach (var c in Physics.OverlapSphere(grabPoint.position, grabRadius, ~0, QueryTriggerInteraction.Collide))
        {
            var g = c.GetComponentInParent<Grabbable>();
            if (g == null || !g.isActiveAndEnabled) continue;

            float d = (c.bounds.ClosestPoint(grabPoint.position) - grabPoint.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = g;
            }
        }

        if (best == null) return;

        best.Grab(this);
        Held = best;
    }

    /// <summary>Called by Grabbable when the object leaves this hand (other hand took it, or it was disabled).</summary>
    public void ClearHeld(Grabbable g)
    {
        if (Held == g) Held = null;
    }

    /// <summary>Transform the held object follows.</summary>
    public Transform GrabTransform => grabPoint;

    static void Bend(Finger f, float amount, Vector3 axis, Vector3 angles)
    {
        if (f == null || axis.sqrMagnitude < 0.0001f) return;
        Vector3 a = axis.normalized;
        for (int i = 0; i < f.joints.Length && i < 3; i++)
        {
            if (f.joints[i] == null) continue;
            f.joints[i].localRotation = f.rest[i] * Quaternion.AngleAxis(angles[i] * amount, a);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(grabPoint != null ? grabPoint.position : transform.position, grabRadius);
    }
}
