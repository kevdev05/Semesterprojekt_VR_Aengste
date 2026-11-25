using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keyboard control for the therapist: choose an NPC with a number key, then press a reaction key.
///   1-6 = select one NPC, 7 = select all, Space = random looking on/off, S = random chairs on/off,
///   Backspace = stop everything.
/// Reactions only work while random looking is off.
/// New animation later: add a state + parameter to the shared Animator and one row to "Reactions" (no code needed).
/// </summary>
public class TherapistControl : MonoBehaviour
{
    public enum Kind { PlayOnce, OnOff }

    [Serializable]
    public class Npc
    {
        [Tooltip("Short description shown to the therapist.")]
        public string label;
        public Animator animator;
        [Tooltip("Mirror the head shake and thinking: shake towards the table, raise the arm on the patient's side.")]
        public bool mirrorHeadShake;
        [Tooltip("Parameters that are already on when the scene starts.")]
        public string[] startOn = new string[0];
        [Tooltip("Parameters this NPC must not use (e.g. Typing without a laptop).")]
        public string[] notAllowed = new string[0];

        [NonSerialized] public readonly HashSet<string> on = new HashSet<string>(); // what should be on
        [NonSerialized] public HeadLookAt headLook;
        [NonSerialized] public ChairSwivel chair;
        [NonSerialized] public float nextGlance, glanceEnd; // times for random looking
        [NonSerialized] public float nextChair;             // time of the next chair move
        [NonSerialized] public float chairRate;             // slider value the next move was planned with
        [NonSerialized] public bool busy;                   // a therapist's animation is running
        [NonSerialized] public int armFitLayer;             // additive correction layer (always on)

        public bool Usable => animator != null && animator.isActiveAndEnabled;
    }

    [Serializable]
    public class Reaction
    {
        public string label;
        public Key key;
        [Tooltip("Animator parameter: Trigger for Play Once, Bool for On Off.")]
        public string parameter;
        public Kind kind;
        [Tooltip("Reactions with the same group cannot run together: starting one ends the other.")]
        public string group = "";
        [Tooltip("Cannot be started while one of these parameters is on.")]
        public string[] blockedWhile = new string[0];
        [Tooltip("Ends when random looking is switched on.")]
        public bool endsWithRandom;
        [Tooltip("The NPC looks at its laptop instead of the patient, and its chair returns to the start angle and stays still.")]
        public bool usesLaptop;

        [NonSerialized] public int hash;
    }

    public Npc[] npcs = new Npc[0];
    public Reaction[] reactions = new Reaction[0];

    [Header("Keys")]
    public Key randomKey = Key.Space;
    [Tooltip("Random chairs on/off.")]
    public Key chairsKey = Key.S;
    public Key stopKey = Key.Backspace;
    [Tooltip("Shows/hides the text on the PC screen.")]
    public Key textKey = Key.F1;

    [Header("Behaviour")]
    [Tooltip("Is random looking on when the scene starts?")]
    public bool randomOnStart = true;
    [Tooltip("With several NPCs selected, each one starts up to this many seconds later.")]
    public float maxDelay = 0.6f;
    [Tooltip("Text on the PC screen (not drawn in the headset).")]
    public bool showText = true;

    [Header("Random looking")]
    [Tooltip("Seconds an NPC looks at the patient before it glances away (shortest, longest).")]
    public Vector2 lookAtPatient = new Vector2(12f, 30f);
    [Tooltip("Seconds a glance lasts (shortest, longest).")]
    public Vector2 lookAway = new Vector2(1.5f, 4f);
    [Tooltip("Largest head turn away from the patient, in degrees. Small turns are much more frequent.")]
    [Range(10f, 70f)] public float maxTurn = 55f;
    [Tooltip("How many NPCs may look away at the same time.")]
    public int maxAway = 2;
    [Tooltip("At least this many seconds between two NPCs starting to look away.")]
    public float minGap = 1.5f;

    [Header("Random chairs")]
    [Tooltip("Do the chairs move randomly when the scene starts?")]
    public bool chairsOnStart = true;
    [Tooltip("How many chairs may move at the same time. (How often a chair moves: slider on each chair's Chair Swivel.)")]
    public int maxChairs = 1;
    [Tooltip("After a therapist's animation, the chair waits at least this many seconds (shortest, longest).")]
    public Vector2 chairWait = new Vector2(3f, 6f);

    /// <summary>Do the chairs move randomly?</summary>
    public bool ChairsOn { get; private set; }

    readonly List<HeadLookAt> heads = new List<HeadLookAt>();
    float lastGlance = -100f;

    /// <summary>Is random looking switched on?</summary>
    public bool RandomOn { get; private set; }

    const int NobodySelected = -1;
    int selected = NobodySelected;      // index in npcs; npcs.Length = all
    string message = "";
    readonly GUIContent text = new GUIContent();
    GUIStyle style;

    struct Planned
    {
        public float time;
        public Npc npc;
        public Reaction trigger;        // null: apply the on/off states
    }

    readonly List<Planned> planned = new List<Planned>();
    readonly List<KeyValuePair<Animator, int>> firedTriggers = new List<KeyValuePair<Animator, int>>();

    static readonly int MirrorHash = Animator.StringToHash("MirrorHeadShake");
    static readonly int OffsetHash = Animator.StringToHash("IdleOffset");

    void Awake()
    {
        useGUILayout = false;
        foreach (var r in reactions) r.hash = Animator.StringToHash(r.parameter);
    }

    void Start()
    {
        for (int i = 0; i < npcs.Length; i++)
        {
            var n = npcs[i];
            if (!n.Usable) continue;
            n.headLook = n.animator.GetComponent<HeadLookAt>();
            if (n.headLook != null) heads.Add(n.headLook);
            n.nextGlance = Time.time + UnityEngine.Random.Range(2f, lookAtPatient.y);
            n.chair = n.animator.GetComponentInParent<ChairSwivel>();
            FirstChairMove(n);
            n.armFitLayer = n.animator.GetLayerIndex("ArmFit");
            n.animator.SetBool(MirrorHash, n.mirrorHeadShake);
            n.animator.SetFloat(OffsetHash, i / (float)npcs.Length); // so they don't breathe in sync
            foreach (var r in reactions)
                if (!HasParameter(n.animator, r.hash))
                    Debug.LogWarning($"TherapistControl: '{n.animator.name}' has no Animator parameter '{r.parameter}'.", this);
            BackToStart(n);
            Apply(n);
        }
        RandomOn = randomOnStart;
        ChairsOn = chairsOnStart;
        RefreshText();
    }

    void Update()
    {
        // A reaction that could not start at once (NPC busy) must not start later by surprise.
        foreach (var t in firedTriggers)
            if (t.Key != null) t.Key.ResetTrigger(t.Value);
        firedTriggers.Clear();

        for (int i = planned.Count - 1; i >= 0; i--)
        {
            var p = planned[i];
            if (Time.time < p.time) continue;
            planned.RemoveAt(i);
            if (p.trigger == null) Apply(p.npc); else Fire(p.npc, p.trigger);
        }

        UpdateGlances();
        UpdateChairs();

        var kb = Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i <= npcs.Length && i < 9; i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame)
                Select(i);

        if (kb[randomKey].wasPressedThisFrame) SetRandom(!RandomOn);
        if (kb[chairsKey].wasPressedThisFrame) SetChairs(!ChairsOn);
        if (kb[stopKey].wasPressedThisFrame) StopAll();
        if (kb[textKey].wasPressedThisFrame) showText = !showText;

        foreach (var r in reactions)
            if (r.key != Key.None && kb[r.key].wasPressedThisFrame) React(r);
    }

    /// <summary>Random looking: every NPC glances away now and then, but only a few at the same time.</summary>
    void UpdateGlances()
    {
        int away = 0;
        foreach (var n in npcs)
            if (n.headLook != null && n.headLook.IsGlancing && !n.headLook.AtLaptop) away++;

        foreach (var n in npcs)
        {
            var h = n.headLook;
            if (h == null || !n.Usable || !h.isActiveAndEnabled) continue;

            if (h.IsGlancing)
            {
                if (Time.time < n.glanceEnd) continue;
                h.EndGlance(RandomOn ? 0f : 1.5f);
                n.nextGlance = Time.time + RandomIn(lookAtPatient);
                continue;
            }

            if (!RandomOn || Time.time < n.nextGlance) continue;
            bool tooMany = !h.AtLaptop && away >= maxAway;
            if (tooMany || Time.time < lastGlance + minGap || !h.StartGlance(heads, maxTurn))
            {
                n.nextGlance = Time.time + UnityEngine.Random.Range(0.5f, 2f); // try again a bit later
                continue;
            }
            n.glanceEnd = Time.time + h.TurnDuration + RandomIn(lookAway);
            lastGlance = Time.time;
            if (!h.AtLaptop) away++;
        }
    }

    /// <summary>Random chairs: a chair turns a little now and then, but never during a therapist's animation.</summary>
    void UpdateChairs()
    {
        int moving = 0;
        foreach (var n in npcs)
            if (n.chair != null && n.chair.IsMoving) moving++;

        foreach (var n in npcs)
        {
            if (n.chair == null || !n.Usable) continue;
            if (n.chairRate != n.chair.movesPerMinute) FirstChairMove(n);   // slider changed while playing

            bool typing = n.headLook != null && n.headLook.AtLaptop;
            if (typing || IsAnimating(n))
            {
                if (typing) n.chair.ReturnToStart();
                else if (!n.busy) n.chair.Stop();
                n.busy = true;
                continue;
            }
            if (n.busy)     // the animation has just ended
            {
                n.busy = false;
                n.nextChair = Mathf.Max(n.nextChair, Time.time + RandomIn(chairWait));
            }

            if (!ChairsOn || Time.time < n.nextChair) continue;
            if (moving >= maxChairs)
            {
                n.nextChair = Time.time + UnityEngine.Random.Range(1f, 5f); // try again a bit later
                continue;
            }
            n.chair.StartMove();
            n.nextChair = Time.time + Mathf.Max(n.chair.RandomInterval(), n.chair.Duration + 1f);
            moving++;
        }
    }

    /// <summary>Plan a chair's first move: some time within its normal interval, so the chairs don't start together.</summary>
    static void FirstChairMove(Npc n)
    {
        if (n.chair == null) return;
        n.chairRate = n.chair.movesPerMinute;
        n.nextChair = Time.time + 2f + n.chair.RandomInterval() * UnityEngine.Random.Range(0.1f, 0.8f);
    }

    /// <summary>
    /// Is a reaction playing? True while any layer above the base plays a clip or the sitting pose changes
    /// (crossed legs count only while changing). Additive correction layers are always on and don't count.
    /// </summary>
    static bool IsAnimating(Npc n)
    {
        var a = n.animator;
        if (a.IsInTransition(0)) return true;
        for (int i = 1; i < a.layerCount; i++)
        {
            if (i == n.armFitLayer) continue;
            if (a.GetCurrentAnimatorClipInfoCount(i) > 0 || a.IsInTransition(i)) return true;
        }
        return false;
    }

    /// <summary>Random chairs on/off. Off: moving chairs slow down and stay where they are.</summary>
    public void SetChairs(bool on)
    {
        ChairsOn = on;
        foreach (var n in npcs)
        {
            if (n.chair == null) continue;
            if (on) FirstChairMove(n);
            else if (!n.busy) n.chair.Stop();
        }
        Say("");
    }

    static float RandomIn(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

    /// <summary>0 = first NPC; the number of NPCs = all of them.</summary>
    public void Select(int index)
    {
        selected = index;
        Say("");
    }

    public void SetRandom(bool on)
    {
        RandomOn = on;
        if (on)
        {
            planned.RemoveAll(p => p.trigger != null);
            foreach (var n in npcs)
            {
                foreach (var r in reactions)
                    if (r.endsWithRandom) n.on.Remove(r.parameter);
                Plan(n, null, true);
                n.nextGlance = Time.time + UnityEngine.Random.Range(2f, lookAtPatient.y);
            }
        }
        else
        {
            foreach (var n in npcs) // all heads slowly back, not exactly together
                n.glanceEnd = Mathf.Min(n.glanceEnd, Time.time + UnityEngine.Random.Range(0f, maxDelay));
        }
        Say("");
    }

    /// <summary>Everybody back to how they sat at the start.</summary>
    public void StopAll()
    {
        planned.Clear();
        foreach (var n in npcs)
        {
            BackToStart(n);
            Plan(n, null, true);
        }
        Say("Stopped.");
    }

    void React(Reaction r)
    {
        if (RandomOn) { Say($"Random looking is ON: press {randomKey} first."); return; }
        if (selected == NobodySelected) { Say("Choose an NPC first (number key)."); return; }

        var list = new List<Npc>();
        bool turnOn = false;
        string reason = "";
        for (int i = 0; i < npcs.Length; i++)
        {
            if (selected != npcs.Length && selected != i) continue;
            var n = npcs[i];
            if (!n.Usable) continue;
            if (Array.IndexOf(n.notAllowed, r.parameter) >= 0)
            {
                reason = $"{r.label}: not available for {i + 1}.";
                continue;
            }
            bool isOn = n.on.Contains(r.parameter);
            string block = isOn ? null : Array.Find(r.blockedWhile, n.on.Contains);
            if (block != null)
            {
                reason = $"{r.label}: not possible for {i + 1} during {LabelOf(block)}.";
                continue;
            }
            list.Add(n);
            if (!isOn) turnOn = true;
        }
        if (list.Count == 0) { Say(reason); return; }

        foreach (var n in list)
        {
            if (r.kind == Kind.OnOff)
            {
                if (turnOn) TurnOn(n, r); else n.on.Remove(r.parameter);
                Plan(n, null, list.Count > 1);
            }
            else Plan(n, r, list.Count > 1);
        }
        Say(r.kind == Kind.OnOff ? r.label + (turnOn ? " on" : " off") : r.label);
    }

    void TurnOn(Npc n, Reaction r)
    {
        if (r.group != "")
            foreach (var other in reactions)
                if (other != r && other.group == r.group) n.on.Remove(other.parameter);
        n.on.Add(r.parameter);
    }

    static void BackToStart(Npc n)
    {
        n.on.Clear();
        foreach (var p in n.startOn) n.on.Add(p);
    }

    void Plan(Npc n, Reaction trigger, bool delayed)
    {
        if (!n.Usable) return;
        planned.Add(new Planned
        {
            time = Time.time + (delayed ? UnityEngine.Random.Range(0f, maxDelay) : 0f),
            npc = n,
            trigger = trigger
        });
    }

    void Apply(Npc n)
    {
        if (!n.Usable) return;
        bool laptop = false;
        foreach (var r in reactions)
        {
            if (r.kind != Kind.OnOff) continue;
            bool on = n.on.Contains(r.parameter);
            n.animator.SetBool(r.hash, on);
            if (on && r.usesLaptop) laptop = true;
        }
        if (n.headLook != null) n.headLook.LookAtLaptop(laptop);
    }

    void Fire(Npc n, Reaction r)
    {
        if (!n.Usable) return;
        n.animator.SetTrigger(r.hash);
        firedTriggers.Add(new KeyValuePair<Animator, int>(n.animator, r.hash));
    }

    string LabelOf(string parameter)
    {
        foreach (var r in reactions)
            if (r.parameter == parameter) return r.label;
        return parameter;
    }

    static bool HasParameter(Animator a, int hash)
    {
        foreach (var p in a.parameters)
            if (p.nameHash == hash) return true;
        return false;
    }

    // ------------------------------------------------ text on the PC screen

    void Say(string what)
    {
        message = what;
        RefreshText();
    }

    void RefreshText()
    {
        var sb = new StringBuilder();
        sb.Append("Random looking: ").AppendLine(RandomOn ? $"ON  ({randomKey} = off)" : $"OFF  ({randomKey} = on)");
        sb.Append("Random chairs: ").AppendLine(ChairsOn ? $"ON  ({chairsKey} = off)" : $"OFF  ({chairsKey} = on)");
        for (int i = 0; i < npcs.Length; i++)
        {
            bool sel = selected == i || selected == npcs.Length;
            sb.Append(sel ? "> " : "   ").Append(i + 1).Append("  ").Append(npcs[i].label);
            string separator = "   [";
            foreach (var r in reactions)
                if (npcs[i].on.Contains(r.parameter))
                {
                    sb.Append(separator).Append(r.label);
                    separator = ", ";
                }
            if (separator == ", ") sb.Append(']');
            sb.AppendLine();
        }
        sb.Append(selected == npcs.Length ? "> " : "   ").Append(npcs.Length + 1).AppendLine("  ALL");
        for (int i = 0; i < reactions.Length; i++)
            sb.Append(reactions[i].key).Append(" = ").Append(reactions[i].label).Append(i % 3 == 2 ? "\n" : "    ");
        sb.AppendLine().Append(stopKey).Append(" = stop everything    ").Append(textKey).Append(" = hide text");
        if (message != "") sb.AppendLine().Append(message);
        text.text = sb.ToString();
    }

    void OnGUI()
    {
        if (!showText || Event.current.type != EventType.Repaint) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8)
            };
            style.normal.textColor = Color.white;
        }
        style.fontSize = Mathf.Max(12, Screen.height / 60);
        float width = Mathf.Min(Screen.width * 0.4f, style.fontSize * 30f);
        var area = new Rect(10f, 10f, width, style.CalcHeight(text, width));
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(area, text, style);
    }
}
