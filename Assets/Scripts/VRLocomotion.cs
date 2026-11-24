using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Smooth VR movement with the Quest controllers. No teleport.
///   Left stick  -> walk (in the direction you are looking)
///   Right stick -> turn smoothly left/right
/// Uses the Character Controller on the same object, so walls with colliders stop the player.
///
/// Setup: put this on the XR Origin root object ("XR"), which has a Character Controller.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class VRLocomotion : MonoBehaviour
{
    [Tooltip("The headset camera. Leave empty to use the Main Camera.")]
    public Transform head;

    [Header("Moving (left stick)")]
    public bool enableMoving = true;
    [Tooltip("Walking speed in metres per second.")]
    public float moveSpeed = 1.5f;
    [Tooltip("How quickly the player speeds up and slows down. Lower = softer.")]
    public float acceleration = 6f;

    [Header("Turning (right stick)")]
    public bool enableTurning = true;
    [Tooltip("Turning speed in degrees per second. Lower is more comfortable.")]
    public float turnSpeed = 60f;

    [Header("General")]
    [Range(0f, 0.5f), Tooltip("Small stick movements below this are ignored.")]
    public float deadzone = 0.15f;
    public float gravity = -9.81f;
    [Tooltip("The player's body height follows the headset, within these limits.")]
    public float minHeight = 1f;
    public float maxHeight = 2.2f;

    CharacterController body;
    InputAction moveAction, turnAction;
    Vector3 horizontalVelocity;
    float verticalVelocity;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        if (head == null && Camera.main != null) head = Camera.main.transform;
    }

    void OnEnable()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        moveAction.AddBinding("<XRController>{LeftHand}/thumbstick");

        turnAction = new InputAction("Turn", InputActionType.Value, expectedControlType: "Vector2");
        turnAction.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        turnAction.AddBinding("<XRController>{RightHand}/thumbstick");

        moveAction.Enable();
        turnAction.Enable();
    }

    void OnDisable()
    {
        moveAction?.Disable();
        moveAction?.Dispose();
        turnAction?.Disable();
        turnAction?.Dispose();
    }

    void Update()
    {
        if (head == null) return;
        float dt = Time.deltaTime;

        // 1. Turn around the player's head (not around the room centre).
        if (enableTurning)
        {
            float x = turnAction.ReadValue<Vector2>().x;
            if (Mathf.Abs(x) > deadzone)
            {
                Quaternion q = Quaternion.AngleAxis(x * turnSpeed * dt, Vector3.up);
                Vector3 pivot = head.position;
                Vector3 newPosition = pivot + q * (transform.position - pivot);
                transform.rotation = q * transform.rotation;
                body.Move(newPosition - transform.position);
            }
        }

        // 2. Keep the collision capsule under the headset.
        Vector3 local = transform.InverseTransformPoint(head.position);
        float height = Mathf.Clamp(local.y, minHeight, maxHeight);
        body.height = height;
        body.center = new Vector3(local.x, height / 2f + body.skinWidth, local.z);

        // 3. Walk in the direction the player is looking.
        Vector3 target = Vector3.zero;
        if (enableMoving)
        {
            Vector2 stick = moveAction.ReadValue<Vector2>();
            if (stick.magnitude > deadzone)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
                target = Vector3.ClampMagnitude(forward * stick.y + right * stick.x, 1f) * moveSpeed;
            }
        }
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, acceleration * dt);

        // 4. Gravity, so the player stays on the floor.
        if (body.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;
        else verticalVelocity += gravity * dt;

        body.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
    }
}
