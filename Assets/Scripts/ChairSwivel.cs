using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Makes the seat of an office chair swivel gently left and right.
/// Put this on the chair's root object (e.g. OfficeChair_Fixed).
/// Press the test key (default: S) in Play mode to start/stop all chairs.
/// </summary>
public class ChairSwivel : MonoBehaviour
{
    [Tooltip("The part that turns (seat + back). Leave empty to find 'Empty_chairRotation' automatically.")]
    public Transform seat;

    [Tooltip("Local axis the seat turns around. Models from Blender usually need Z (0,0,1). If the seat tips over instead of turning, try Y (0,1,0).")]
    public Vector3 axis = Vector3.forward;

    [Header("Swivel")]
    [Tooltip("Start swiveling as soon as Play mode starts.")]
    public bool swivelOnStart = true;

    [Tooltip("How far the seat turns to each side, in degrees.")]
    [Range(0f, 90f)] public float maxAngle = 15f;

    [Tooltip("Full left-right swings per second.")]
    [Range(0.05f, 3f)] public float speed = 0.4f;

    [Header("Test key")]
    [Tooltip("Keyboard key that starts/stops the swivel.")]
    public Key toggleKey = Key.S;

    Quaternion startRotation;
    float phase;       // random offset so chairs don't move in sync
    bool swiveling;
    float weight;      // 0..1, used to ease in and out smoothly

    void Awake()
    {
        if (seat == null)
            seat = FindChildRecursive(transform, "Empty_chairRotation");

        if (seat == null)
        {
            Debug.LogWarning($"ChairSwivel on '{name}': no seat assigned and 'Empty_chairRotation' not found.", this);
            enabled = false;
            return;
        }

        startRotation = seat.localRotation;
        phase = Random.Range(0f, Mathf.PI * 2f);
        swiveling = swivelOnStart;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            swiveling = !swiveling;

        // Ease in/out over about one second instead of snapping.
        weight = Mathf.MoveTowards(weight, swiveling ? 1f : 0f, Time.deltaTime);

        float angle = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phase) * maxAngle * weight;
        seat.localRotation = startRotation * Quaternion.AngleAxis(angle, axis);
    }

    static Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindChildRecursive(child, childName);
            if (found != null) return found;
        }
        return null;
    }
}
