using UnityEngine;

public class HeadLookAt : MonoBehaviour
{
    [Header("References")]
    public Transform head;
    public Transform target;

    [Header("Settings")]
    [Range(0f, 1f)]
    [Tooltip("0 = Nur Animation, 1 = Voller Fokus auf das Target")]
    public float lookAtWeight = 1f;
    public float rotationSpeed = 5f;
    public float maxYaw = 70f;
    public float maxPitch = 35f;

    [Header("Fine-Tuning")]
    public Vector3 eyePositionOffset = new Vector3(0f, 0.1f, 0.05f);
    public Vector3 rotationOffset = Vector3.zero;

    private Quaternion smoothedLookRotation;
    private bool isInitialized = false;

    void LateUpdate()
    {
        if (head == null || target == null)
            return;

        // Die aktuelle Rotation aus dem Animator sichern, BEVOR wir sie verändern
        Quaternion animatedRotation = head.localRotation;

        if (!isInitialized)
        {
            smoothedLookRotation = animatedRotation;
            isInitialized = true;
        }

        // Richtung von den Augen zum Target berechnen
        Vector3 headEyePosition = head.TransformPoint(eyePositionOffset);
        Vector3 direction = target.position - headEyePosition;
        Vector3 localDirection = head.parent.InverseTransformDirection(direction.normalized);

        // Winkel berechnen und begrenzen
        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Asin(localDirection.y) * Mathf.Rad2Deg;

        yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        // Absolute Ziel-Rotation im lokalen Raum
        Quaternion targetLocalRotation = Quaternion.Euler(
            pitch + rotationOffset.x, 
            yaw + rotationOffset.y, 
            rotationOffset.z
        );

        // Zeitliches Glätten der LookAt-Bewegung (Verhindert ruckartige Kopfbewegungen)
        smoothedLookRotation = Quaternion.Slerp(
            smoothedLookRotation,
            targetLocalRotation,
            rotationSpeed * Time.deltaTime
        );

        // Überblenden zwischen der originalen Animation (0) und der geglätteten Ziel-Rotation (1)
        head.localRotation = Quaternion.Slerp(
            animatedRotation, 
            smoothedLookRotation, 
            lookAtWeight
        );
    }
}
