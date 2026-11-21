using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class RandomObjectActivator : MonoBehaviour
{
    [Header("Objects")]
    [SerializeField] private List<GameObject> objects = new List<GameObject>();

    [Header("Input (new Input System)")]
    [SerializeField]
    private InputAction activateAction =
        new InputAction("Activate", InputActionType.Button, "<Keyboard>/g");

    [Header("Settings")]
    [Tooltip("How many of the most recently enabled objects are excluded from the next pick.")]
    [SerializeField, Min(0)] private int historySize = 3;

    private readonly List<GameObject> history = new List<GameObject>();
    private GameObject current;
    private bool initialized;

    private void OnEnable()
    {
        activateAction.performed += OnActivatePerformed;
        activateAction.Enable();
        Activate();
    }

    private void OnDisable()
    {
        activateAction.performed -= OnActivatePerformed;
        activateAction.Disable();
    }

    private void OnActivatePerformed(InputAction.CallbackContext context)
    {
        Activate();
    }

    public void Activate()
    {
        // First call: only disable everything.
        if (!initialized)
        {
            foreach (GameObject obj in objects)
            {
                if (obj != null) obj.SetActive(false);
            }

            initialized = true;
        }

        GameObject next = PickRandomObject();
        if (next == null) return;

        // Disable the currently enabled object, then enable the new one.
        if (current != null) current.SetActive(false);

        next.SetActive(true);
        current = next;
        AddToHistory(next);
    }

    private GameObject PickRandomObject()
    {
        // Candidates are all objects that are not in the recent history.
        List<GameObject> candidates = objects.FindAll(o => o != null && !history.Contains(o));

        // Not enough objects to avoid the history: only avoid the current one.
        if (candidates.Count == 0)
        {
            candidates = objects.FindAll(o => o != null && o != current);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("RandomObjectActivator: no object available to enable.", this);
            return null;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void AddToHistory(GameObject obj)
    {
        history.Remove(obj);
        history.Add(obj);

        while (history.Count > historySize)
        {
            history.RemoveAt(0);
        }
    }
}