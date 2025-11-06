using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;              // added for IEnumerator
using System.Collections.Generic;
using System.Linq;using Random = UnityEngine.Random;


 public class DishController : MonoBehaviour
 {
     public float moveSpeed = 4f;
     // sponge prefab that will sweep over the dish when a correct input is made
     public GameObject spongePrefab;
     // local distance the sponge travels from start to end (half-distance each side)
     public float spongeTravel = 0.6f;
     // optional scale/rotation applied to spawned sponge
     public Vector3 spongeLocalScale = Vector3.one;
    public float spongeRotationZ = 0f;
    public AudioClip[] scrubSound;
    // track active sponge instances so we can wait for them before completing the dish
    private List<GameObject> activeSponges = new List<GameObject>();
    // exit animation: how far down to move, and how long the shrink+move takes
    public float exitMoveDistance = 5f;
    public float exitDuration = 0.5f;
    private AudioSource audioSource;
    
 
     // Static registry so we can decide which dishes are the bottom two
     static List<DishController> allDishes = new List<DishController>();
     
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
    // shake feedback
    private Coroutine shakeCoroutine;
    private Vector3 shakeOriginalLocal;
 
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
        // ensure only the bottom two dishes show icons after icons are created
        UpdateBottomTwoIcons();
 
         moving = true;
         acceptingInput = false;
         currentIndex = 0;
     }
 
     void OnDestroy()
     {
        // remove from registry so visibility recalculates for remaining dishes
        if (allDishes.Contains(this)) allDishes.Remove(this);
        UpdateBottomTwoIcons();
 
         if (inputAction != null)
         {
             inputAction.performed -= OnInputPerformed;
             inputAction.Disable();
             inputAction.Dispose();
             inputAction = null;
         }
     }
    void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogError("DishController: AudioSource component is MISSING!");
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
         // ignore input if this dish isn't currently accepting input
         if (!acceptingInput) return;
         if (sequence == null || sequence.Length == 0) return;
         // ignore invalid codes
         if (code < 0) return;
 
         // Only process a single correct advance per call; guards above help avoid premature completion.
         if (sequence[currentIndex] == code)
        {
            if (audioSource != null && scrubSound != null && scrubSound.Length > 0)
            {
                int randomIndex = Random.Range(0, scrubSound.Length);
                AudioClip clipToPlay = scrubSound[randomIndex];
                audioSource.PlayOneShot(clipToPlay);
            }
             MarkIconComplete(currentIndex);
             float shakeDur = 0.18f;
             float shakeMag = 0.12f;
             Shake(shakeDur, shakeMag);
             // spawn sponge that sweeps in the direction of the input while the dish shakes
             SpawnSponge(code, shakeDur);
             currentIndex++;
             if (currentIndex >= sequence.Length)
             {
                 // completed full sequence — begin finish sequence that waits for sponges to finish
                 FinishDish(true);
             }
         }
         else
         {
             // incorrect input handling (keep existing behavior if present)
             // e.g. provide shake/error feedback, reset progress, etc.
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
 
     // Called by manager: show or hide this dish's arrow icons (use to show only bottom two)
     public void SetIconsActive(bool active)
     {
         for (int i = 0; i < icons.Count; i++)
         {
             if (icons[i] != null)
                 icons[i].SetActive(active);
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
            // positions changed — recalc which two dishes are considered "bottom"
            UpdateBottomTwoIcons();
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
        // stack changed -> recalc bottom two immediately (targets changed)
        UpdateBottomTwoIcons();
     }
 
     public bool HasArrived => hasArrived;
 
     void FinishDish(bool success)
     {
         // disable input immediately
         acceptingInput = false;
         if (inputAction != null) inputAction.Disable();
 
         // notify manager right away (manager may start bookkeeping)
         onCompleted?.Invoke(this);
 
         // start routine to wait for sponges/shake then play exit animation and destroy
         StartCoroutine(FinishAndExitRoutine());
     }
 
     IEnumerator FinishAndExitRoutine()
     {
         // wait until any shake and sponge routines finish
         while (shakeCoroutine != null || activeSponges.Count > 0)
             yield return null;
 
         // optionally small delay to ensure visual clarity
         yield return new WaitForSeconds(0.02f);
 
         // play exit animation: move down and shrink
         Vector3 startPos = transform.position;
         Vector3 endPos = startPos + Vector3.down * exitMoveDistance;
         Vector3 startScale = transform.localScale;
         Vector3 endScale = Vector3.zero;
         float elapsed = 0f;
         while (elapsed < exitDuration)
         {
             elapsed += Time.deltaTime;
             float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / exitDuration));
             transform.position = Vector3.Lerp(startPos, endPos, t);
             transform.localScale = Vector3.Lerp(startScale, endScale, t);
             yield return null;
         }
 
         // final cleanup
         Destroy(gameObject);
     }
 
    void OnEnable()
    {
        // register and recalc when a dish spawns/enables
        if (!allDishes.Contains(this)) allDishes.Add(this);
        UpdateBottomTwoIcons();
    }
 
    void OnDisable()
    {
        // unregister and recalc when a dish disables
        if (allDishes.Contains(this)) allDishes.Remove(this);
        UpdateBottomTwoIcons();
    }
 
    // Recalculate which two dishes are the bottom-most (lowest Y) and show only their icons.
    static void UpdateBottomTwoIcons()
    {
        if (allDishes == null || allDishes.Count == 0) return;
        // sort ascending by world Y (bottom = smallest Y)
        var sorted = allDishes.OrderBy(d => d.transform.position.y).ToList();
        int showCount = Math.Min(2, sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var controller = sorted[i];
            bool show = i < showCount;
            controller.SetIconsActive(show);
        }
    }

    // public API to trigger a short shake on this dish (duration in seconds, magnitude in world units)
    public void Shake(float duration = 0.18f, float magnitude = 0.12f)
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }
        // record current local position so we restore it after shaking
        shakeOriginalLocal = transform.localPosition;
        shakeCoroutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        Vector3 originalLocal = shakeOriginalLocal;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // random offset in XY plane
            Vector2 offset2 = UnityEngine.Random.insideUnitCircle * magnitude;
            transform.localPosition = originalLocal + new Vector3(offset2.x, offset2.y, 0f);
            yield return null;
        }
        transform.localPosition = originalLocal;
        shakeCoroutine = null;
    }
 
    // Spawn a sponge prefab as a child and move it across the dish in the direction of the input.
    // code: 0=left,1=up,2=right,3=down
    void SpawnSponge(int code, float duration)
    {
        if (spongePrefab == null) return;
        Vector3 dir;
        switch (code)
        {
            case 0: dir = Vector3.left; break;
            case 1: dir = Vector3.up; break;
            case 2: dir = Vector3.right; break;
            case 3: dir = Vector3.down; break;
            default: dir = Vector3.up; break;
        }

        // instantiate sponge as a child so it follows dish movement/shake
        GameObject sp = Instantiate(spongePrefab, transform);
        // register so we can wait for it before completing the dish
        activeSponges.Add(sp);
        sp.transform.localScale = spongeLocalScale;
        // orient sponge to the input direction (use existing helper for arrow rotation)
        sp.transform.localEulerAngles = new Vector3(0f, 0f, spongeRotationZ + GetZRotationFor(code));
 
        // start slightly off the dish on the opposite side and move through to the other side
        Vector3 start = -dir * spongeTravel;
        Vector3 end = dir * spongeTravel;
        sp.transform.localPosition = start;
        StartCoroutine(MoveSpongeRoutine(sp, start, end, duration));
    }
 
     IEnumerator MoveSpongeRoutine(GameObject sp, Vector3 start, Vector3 end, float duration)
     {
         if (sp == null) yield break;
         float elapsed = 0f;
         while (elapsed < duration)
         {
             elapsed += Time.deltaTime;
             float t = Mathf.Clamp01(elapsed / duration);
             sp.transform.localPosition = Vector3.Lerp(start, end, t);
             yield return null;
         }
         if (sp != null) sp.transform.localPosition = end;
         // small tail so the sponge remains visible a fraction of a second
         yield return new WaitForSeconds(0.05f);
        // unregister then destroy
         if (sp != null)
         {
             activeSponges.Remove(sp);
             Destroy(sp);
         }
     }

 }