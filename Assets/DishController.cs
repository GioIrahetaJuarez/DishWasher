using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class DishController : MonoBehaviour
{
    public float moveSpeed = 4f;

    // Provided by GameManager at Init (not inspector)
    GameObject arrowIconPrefab;
    Sprite arrowSprite;
    float iconSpacing = 0.6f;
    Vector3 arrivalOffset = Vector3.zero;
    Vector3 iconsOffset = Vector3.zero;
    Vector3 iconScale = Vector3.one;
    Transform arrowsParent; // optional parent for icons

    // input & sequence state
    InputAction inputAction;
    int[] sequence;
    int currentIndex;
    bool acceptingInput = false;

    // movement & icons
    private Vector3 targetPos;
    private bool moving = false;
    private List<GameObject> icons = new List<GameObject>();
    private bool hasArrived = false;

    // notify manager when done / arrived
    public Action<DishController> onCompleted;
    public Action<DishController> onArrived;

    // New Init signature: manager passes customization values here.
    public void Init(
        int[] sequence,
        Vector3 target,
        GameObject arrowIconPrefab,
        Sprite arrowSprite,
        float iconSpacing,
        Vector3 arrivalOffset,
        Vector3 iconsOffset,
        Vector3 iconScale,
        Transform arrowsParent = null)
    {
        // store values for use in SetupIcons and movement
        this.sequence = sequence;
        this.arrowIconPrefab = arrowIconPrefab;
        this.arrowSprite = arrowSprite;
        this.iconSpacing = iconSpacing;
        this.arrivalOffset = arrivalOffset;
        this.iconsOffset = iconsOffset;
        this.iconScale = iconScale;
        this.arrowsParent = arrowsParent;

        targetPos = target + arrivalOffset;
        hasArrived = false;

        CreateInputAction();
        SetupIcons(sequence);

        moving = true;
        acceptingInput = false;
        currentIndex = 0;
    }

    void OnDestroy()
    {
        if (inputAction != null)
        {
            inputAction.performed -= OnInputPerformed;
            inputAction.Disable();
            inputAction.Dispose();
            inputAction = null;
        }
    }

    void CreateInputAction()
    {
        if (inputAction != null) return;

        inputAction = new InputAction("DishInput", InputActionType.Button);

        // WASD
        inputAction.AddBinding("<Keyboard>/w");
        inputAction.AddBinding("<Keyboard>/a");
        inputAction.AddBinding("<Keyboard>/s");
        inputAction.AddBinding("<Keyboard>/d");

        // Arrow keys
        inputAction.AddBinding("<Keyboard>/upArrow");
        inputAction.AddBinding("<Keyboard>/leftArrow");
        inputAction.AddBinding("<Keyboard>/downArrow");
        inputAction.AddBinding("<Keyboard>/rightArrow");

        inputAction.performed += OnInputPerformed;
        // enabling/disabling is controlled externally via SetActive()
    }

    void OnInputPerformed(InputAction.CallbackContext ctx)
    {
        if (!acceptingInput) return;
        if (ctx.control == null) return;

        string key = ctx.control.name; // "w", "a", "s", "d", "upArrow", etc.
        int code = -1;

        if (key == "a" || key == "leftArrow") code = 0;   // left
        else if (key == "w" || key == "upArrow") code = 1; // up
        else if (key == "d" || key == "rightArrow") code = 2; // right
        else if (key == "s" || key == "downArrow") code = 3; // down

        if (code == -1) return;

        HandleInput(code);
    }

    void HandleInput(int code)
    {
        if (currentIndex >= sequence.Length) return;

        if (sequence[currentIndex] == code)
        {
            // correct input: mark icon (e.g., disable or tint)
            MarkIconComplete(currentIndex);
            currentIndex++;

            if (currentIndex >= sequence.Length)
            {
                FinishDish(true);
            }
        }
        else
        {
            // incorrect input — reset progress
            ResetProgress();
        }
    }

    void ResetProgress()
    {
        currentIndex = 0;
        for (int i = 0; i < icons.Count; i++)
        {
            var sr = icons[i].GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = Color.white;
            }
        }

        // optional: feedback (sound/flash) could be added here
    }

    void MarkIconComplete(int index)
    {
        if (index < 0 || index >= icons.Count) return;
        var icon = icons[index];
        var sr = icon.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(1f, 1f, 1f, 0.35f); // visually dim completed arrow
        }
    }

    // Backwards-compatible Init (if something else calls the old signature)
    public void Init(int[] sequence, Vector3 target)
    {
        Init(sequence, target, arrowIconPrefab, arrowSprite, iconSpacing, arrivalOffset, iconsOffset, iconScale, arrowsParent);
    }

    void SetupIcons(int[] seq)
    {
        foreach (var go in icons) Destroy(go);
        icons.Clear();
        if (arrowIconPrefab == null) return;

        // choose parent for icons; allow arrowsParent to be null (defaults to this transform)
        Transform parent = arrowsParent != null ? arrowsParent : transform;

        float totalWidth = (seq.Length - 1) * iconSpacing;
        float startX = -totalWidth * 0.5f;

        for (int i = 0; i < seq.Length; i++)
        {
            GameObject icon = Instantiate(arrowIconPrefab, parent);
            icon.transform.localPosition = new Vector3(startX + i * iconSpacing, 0f, 0f) + iconsOffset;
            icon.transform.localScale = iconScale;

            SpriteRenderer sr = icon.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = arrowSprite;                          // use single up-pointing sprite
                icon.transform.localEulerAngles = new Vector3(0f, 0f, GetZRotationFor(seq[i]));
            }
            icons.Add(icon);
        }
    }

    float GetZRotationFor(int code)
    {
        // seq mapping: 0:left, 1:up, 2:right, 3:down
        switch (code)
        {
            case 0: return 90f;   // left = rotate 90 degrees CCW from up
            case 1: return 0f;    // up = default
            case 2: return -90f;  // right = rotate 90 degrees CW (or -90)
            case 3: return 180f;  // down = flip
            default: return 0f;
        }
    }

    void Update()
    {
        if (!moving) return;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
        if (Vector3.SqrMagnitude(transform.position - targetPos) < 0.0001f)
        {
            moving = false;
            hasArrived = true;
            onArrived?.Invoke(this);
            // input remains disabled until manager activates this dish via SetActive
        }
    }

    // Called by manager to enable/disable player input for this dish.
    public void SetActive(bool active)
    {
        acceptingInput = active;
        if (inputAction != null)
        {
            if (active) inputAction.Enable();
            else inputAction.Disable();
        }
    }

    // Called by manager to move the dish to a new stack position (used when stack shifts)
    public void MoveTo(Vector3 newTarget)
    {
        targetPos = newTarget + arrivalOffset;
        moving = true;
        hasArrived = false;
        SetActive(false);
    }

    public bool HasArrived => hasArrived;

    void FinishDish(bool success)
    {
        acceptingInput = false;
        if (inputAction != null) inputAction.Disable();

        // optional: play success animation, sound, etc.

        onCompleted?.Invoke(this);

        Destroy(gameObject, 0.05f);
    }
}