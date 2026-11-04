
using UnityEngine;

public class HeadLookAt : MonoBehaviour
{
    [Header("References")]
    public Transform head;
    public Transform target;

    [Header("Settings")]
    public float rotationSpeed = 5f;
    public float maxYaw = 70f;
    public float maxPitch = 35f;

    private Quaternion currentOffset = Quaternion.identity;

    void LateUpdate()
    {
        if (head == null || target == null)
            return;

        // Get the head's current animated rotation
        Quaternion animatedRotation = head.localRotation;

        // Direction towards the target
        Vector3 direction = target.position - head.position;

        Vector3 localDirection =
            head.parent.InverseTransformDirection(direction.normalized);

        float yaw = Mathf.Atan2(localDirection.x, localDirection.z)
                    * Mathf.Rad2Deg;

        float pitch = -Mathf.Asin(localDirection.y)
                      * Mathf.Rad2Deg;

        yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        // Calculate the additional rotation
        Quaternion targetOffset = Quaternion.Euler(pitch, yaw, 0f);

        currentOffset = Quaternion.Slerp(
            currentOffset,
            targetOffset,
            rotationSpeed * Time.deltaTime
        );

        // Apply offset on top of the animation
        head.localRotation = animatedRotation * currentOffset;
    }
}
