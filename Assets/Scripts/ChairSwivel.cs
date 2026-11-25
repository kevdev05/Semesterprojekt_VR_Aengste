using UnityEngine;

/// <summary>
/// Turns the seat of an office chair slowly by a few degrees, like a person shifting on the chair.
/// The seat turns a little and stays there until the next move.
/// Put this on the chair's root object. WHEN chairs move is decided by TherapistControl.
/// </summary>
public class ChairSwivel : MonoBehaviour
{
    [Tooltip("The part that turns (seat + back). Leave empty to find 'Empty_chairRotation' automatically.")]
    public Transform seat;

    [Tooltip("Local axis the seat turns around. Models from Blender usually need Z (0,0,1). If the seat tips over instead of turning, try Y (0,1,0).")]
    public Vector3 axis = Vector3.forward;

    [Header("Movement")]
    [Tooltip("How often this chair moves on its own, on average. 0 = never, 1 = about once per minute, 5 = about every 12 seconds. " +
             "The time between two moves is random. Can be changed in Play mode.")]
    [Range(0f, 5f)] public float movesPerMinute = 1f;

    [Tooltip("How far the seat may turn from its start position, in degrees (to one side, to the other side).")]
    public Vector2 range = new Vector2(-4f, 4f);

    [Tooltip("Smallest and largest turn of one move, in degrees.")]
    public Vector2 turnSize = new Vector2(2f, 4f);

    [Tooltip("Average speed in degrees per second. Every move starts and stops softly.")]
    public float speed = 1f;

    const float StopTime = 0.5f;     // seconds to come to a stop
    const float ReturnSpeed = 3f;    // degrees per second on the way back to the start position
    const float MinReturnTime = 1.2f;

    Quaternion startRotation;
    float angle;                     // current angle from the start position
    float velocity;                  // degrees per second, for a soft stop
    float from, to, progress = 1f, duration = 1f;
    float stopLeft;                  // > 0: slowing down

    /// <summary>Is the seat turning right now?</summary>
    public bool IsMoving => progress < 1f || stopLeft > 0f;

    /// <summary>Seconds the current move takes.</summary>
    public float Duration => duration;

    bool returning;

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
    }

    /// <summary>Random seconds from one move to the next (half to one and a half times the average).</summary>
    public float RandomInterval() =>
        movesPerMinute > 0f ? 60f / movesPerMinute * Random.Range(0.5f, 1.5f) : float.PositiveInfinity;

    /// <summary>Turn a little to a random side and stay there.</summary>
    public void StartMove()
    {
        float size = Random.Range(turnSize.x, turnSize.y);
        float side = Random.value < 0.5f ? -1f : 1f;
        float target = Mathf.Clamp(angle + side * size, range.x, range.y);
        if (Mathf.Abs(target - angle) < turnSize.x)         // no room on this side: the other one
            target = Mathf.Clamp(angle - side * size, range.x, range.y);
        MoveTo(target, Mathf.Abs(target - angle) / Mathf.Max(speed, 0.1f));
        returning = false;
    }

    /// <summary>Slow down and stay where the seat is.</summary>
    public void Stop()
    {
        if (progress >= 1f) return;
        progress = 1f;
        stopLeft = StopTime;
        returning = false;
    }

    /// <summary>Turn back to the start position (e.g. for typing, so the hands reach the laptop).</summary>
    public void ReturnToStart()
    {
        if (returning || (!IsMoving && Mathf.Abs(angle) < 0.01f)) return;
        MoveTo(0f, Mathf.Max(MinReturnTime, Mathf.Abs(angle) / ReturnSpeed));
        returning = true;
    }

    void MoveTo(float target, float seconds)
    {
        from = angle;
        to = target;
        duration = Mathf.Max(seconds, 0.01f);
        progress = 0f;
        stopLeft = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (progress < 1f)
        {
            progress = Mathf.MoveTowards(progress, 1f, dt / duration);
            float p = progress;
            float soft = p * p * p * (p * (6f * p - 15f) + 10f);
            float next = Mathf.Lerp(from, to, soft);
            velocity = (next - angle) / dt;
            angle = next;
            if (progress >= 1f) returning = false;
        }
        else if (stopLeft > 0f)
        {
            angle = Mathf.Clamp(angle + velocity * (stopLeft / StopTime) * dt, range.x, range.y);
            stopLeft -= dt;
        }
        else return;

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
