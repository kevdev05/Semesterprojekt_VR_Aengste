using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Switches an Animator Bool parameter on/off with a keyboard key.
/// Example: press T to make a character start typing, press T again to go back to sitting.
/// Put this on the character that has the Animator.
/// </summary>
public class AnimatorKeyToggle : MonoBehaviour
{
    [Tooltip("The character's Animator. Leave empty to use the Animator on this object.")]
    public Animator animator;

    [Tooltip("Name of the Bool parameter in the Animator Controller (must match exactly).")]
    public string parameterName = "Typing";

    [Tooltip("Keyboard key the therapist presses.")]
    public Key toggleKey = Key.T;

    [Tooltip("Should the character start in the ON state (e.g. already typing)?")]
    public bool startOn = false;

    [Tooltip("Optional: components to switch OFF while the parameter is ON, e.g. 'Head Look At' so the character looks at the laptop instead of the patient.")]
    public Behaviour[] disableWhileOn;

    bool isOn;
    int parameterHash;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        parameterHash = Animator.StringToHash(parameterName);

        if (animator == null)
        {
            Debug.LogWarning($"AnimatorKeyToggle on '{name}': no Animator found.", this);
            enabled = false;
            return;
        }

        bool found = false;
        foreach (var p in animator.parameters)
            if (p.nameHash == parameterHash && p.type == AnimatorControllerParameterType.Bool) found = true;

        if (!found)
            Debug.LogWarning($"AnimatorKeyToggle on '{name}': the Animator Controller has no Bool parameter called '{parameterName}'.", this);
    }

    void Start()
    {
        Set(startOn);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();
    }

    /// <summary>Switch between on and off. Can also be called from a UI Button.</summary>
    public void Toggle()
    {
        Set(!isOn);
    }

    /// <summary>Set the state directly.</summary>
    public void Set(bool on)
    {
        isOn = on;
        animator.SetBool(parameterHash, on);

        if (disableWhileOn != null)
            foreach (var b in disableWhileOn)
                if (b != null) b.enabled = !on;
    }
}
