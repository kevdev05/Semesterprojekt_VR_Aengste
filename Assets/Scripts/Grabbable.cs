using UnityEngine;

/// <summary>
/// Makes an object grabbable with a VRHand (grip button).
/// Adding this component also adds a Rigidbody, and a Box Collider if the object has no collider yet.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Grabbable : MonoBehaviour
{
    /// <summary>The hand currently holding this object, or null.</summary>
    public VRHand HeldBy { get; private set; }

    Rigidbody rb;
    bool wasKinematic;
    RigidbodyInterpolation wasInterpolation;
    Vector3 offsetPosition;
    Quaternion offsetRotation;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>Called by VRHand when the grip button is pressed near this object.</summary>
    public void Grab(VRHand hand)
    {
        if (HeldBy != null)
        {
            // Taken over by the other hand.
            HeldBy.ClearHeld(this);
        }
        else
        {
            wasKinematic = rb.isKinematic;
            wasInterpolation = rb.interpolation;
            Application.onBeforeRender += Follow;
        }

        HeldBy = hand;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;

        // Keep the object exactly where it was grabbed, relative to the hand.
        Transform h = hand.GrabTransform;
        offsetPosition = h.InverseTransformPoint(transform.position);
        offsetRotation = Quaternion.Inverse(h.rotation) * transform.rotation;
    }

    /// <summary>Called by VRHand when the grip button is released.</summary>
    public void Release(Vector3 velocity, Vector3 angularVelocity)
    {
        if (HeldBy == null) return;

        HeldBy.ClearHeld(this);
        HeldBy = null;
        Application.onBeforeRender -= Follow;

        rb.isKinematic = wasKinematic;
        rb.interpolation = wasInterpolation;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;
        }
    }

    void LateUpdate()
    {
        Follow();
    }

    void Follow()
    {
        if (HeldBy == null) return;
        Transform h = HeldBy.GrabTransform;
        transform.SetPositionAndRotation(h.TransformPoint(offsetPosition), h.rotation * offsetRotation);
    }

    void OnDisable()
    {
        // e.g. the object is switched off while someone is holding it.
        if (HeldBy != null) Release(Vector3.zero, Vector3.zero);
    }

    // Runs when the component is added in the editor: sensible physics defaults + a collider.
    void Reset()
    {
        var body = GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (GetComponentInChildren<Collider>() != null) return;

        var renderers = GetComponentsInChildren<Renderer>();
        var box = gameObject.AddComponent<BoxCollider>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        box.center = transform.InverseTransformPoint(b.center);
        Vector3 s = transform.InverseTransformVector(b.size);
        box.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }
}
