using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class gameManagerScript : MonoBehaviour
{

    public GameObject dishPrefab;
    public float spawnX = 0f;
    public float offscreenY = 7f;
    public float targetY = 1.5f;
    public float spawnInterval = 3f;
    public float stackSpacing = 0.6f; // vertical spacing between stacked dishes

    // New: shared customizable fields for arrows/icons (set in inspector on GameManager)
    public GameObject arrowIconPrefab;
    public Sprite arrowSprite;            // single up-pointing sprite
    public float iconSpacing = 0.6f;
    public Vector3 arrivalOffset = Vector3.zero; // offset applied to target position when dish arrives
    public Vector3 iconsOffset = Vector3.zero;   // local offset for icons relative to parent
    public Vector3 iconScale = Vector3.one;      // scale for spawned icons
    public Transform arrowsParent;                // optional parent for icons (can be left null)

    // Controls how arrow count increases:
    // startingArrowCount = number of arrows on the first dish
    // incrementEvery = increase arrow count by 1 every `incrementEvery` dishes (must be >= 1)
    public int startingArrowCount = 3;
    public int incrementEvery = 1;

    // New: max dishes allowed before losing (default 8)
    public int maxDishesToLose = 8;

    // Spawn rate progression: interval will be reduced by spawnIntervalDecay each tick
    // until it reaches minSpawnInterval (the fastest). When it reaches the fastest,
    // the spawn interval is reset back to the original and difficulty / arrow count increase.
    public float minSpawnInterval = 0.5f;      // fastest allowed spawn interval (max spawn rate)
    public float spawnIntervalDecay = 0.1f;    // how much to reduce spawnInterval each tick
    // How often (seconds) we adjust the spawn interval (speed up). This is the timer-based approach.
    public float spawnRateTickInterval = 5f;
    public float spawnRateTimer = 0f;
    [HideInInspector] public int difficulty = 0;
    private float baseSpawnInterval;           // stores the original spawnInterval to reset to
     
    // Washed dishes tracking + UI
    [Header("Washed Counter UI")]
    public bool autoCreateWashedUI = true;
    public TextMeshProUGUI washedCounterText;          // TMP text
    public TMP_FontAsset washedTMPFont;                // optional TMP font asset for auto-created text
    public int washedFontSize = 24;
    public Color washedColor = Color.white;
    public string washedPrefix = "Washed: ";
    public Vector2 washedAnchorMin = new Vector2(0.01f, 0.95f);
    public Vector2 washedAnchorMax = new Vector2(0.25f, 1f);
    public TextAlignmentOptions washedAlignment = TextAlignmentOptions.MidlineLeft;

    //Gio new: Current Dishes Ui :)
    [Header("Current Dishes UI")]
    public bool autoCreateDishesUI = true;
    public TextMeshProUGUI currentDishesText;
    public int dishesFontSize = 48;
    public Color dishesColor = Color.white;
    public string dishesPrefix = "Dishes: ";
    public Vector2 dishesAnchorMin = new Vector2(0.01f, 0.88f);
    public Vector2 dishesAnchorMax = new Vector2(0.25f, 0.93f);
    public TextAlignmentOptions dishesAlignment = TextAlignmentOptions.MidlineLeft;
    public Vector2 dishesSpawnOffset = Vector2.zero;
    
    //Gio new: Current Dishes Ui :)
    [Header("Difficulty UI")]
    public bool autoCreateDifficultyUI = true;
    public TextMeshProUGUI difficultyText;
    public int difficultyFontSize = 48;
    public Color difficultyColor = Color.white;
    public string difficultyPrefix = "Difficulty: ";
    public string difficultySuffix = " / 5";
    public Vector2 difficultyAnchorMin = new Vector2(0.01f, 0.81f);
    public Vector2 difficultyAnchorMax = new Vector2(0.25f, 0.86f);
    public TextAlignmentOptions difficultyAlignment = TextAlignmentOptions.MidlineLeft;
    public Vector2 difficultySpawnOffset = Vector2.zero;


    // New: allow tuning game-over "Dishes Washed" text position from inspector
    [Header("Game Over UI")]
    public Vector2 gameOverWashedAnchorMin = new Vector2(0.1f, 0.28f);
    public Vector2 gameOverWashedAnchorMax = new Vector2(0.9f, 0.36f);
    public AudioClip brokenDishSound;

    [HideInInspector]
    public int washedCount = 0;

    private float spawnTimer;
    private List<DishController> dishStack = new List<DishController>();
    private int dishesSpawned = 0; // total dishes spawned so far (used for arrow increment logic)
    private bool isGameOver = false;
    private AudioSource audioSource;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        spawnTimer = spawnInterval;
        // remember the original spawn interval so we can reset to it later
        baseSpawnInterval = spawnInterval;
        // initialize spawn rate timer so adjustments start after tick interval
        spawnRateTimer = spawnRateTickInterval;
        if (autoCreateWashedUI && washedCounterText == null)
            CreateWashedUI();
        if (autoCreateDishesUI && currentDishesText == null)
            CreateDishesUI();
        if (autoCreateDifficultyUI && difficultyText == null)
            CreateDifficultyUI();
        //Changed from UpdateWashedUI
        UpdateAllHUD();
    }

    // Update is called once per frame
    void Update()
    {
        if (isGameOver) return; // stop spawning/processing when game over

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnDish();
            spawnTimer = spawnInterval;
        }

        // timer-driven spawn rate progression (speed up over time, independent of spawns)
        spawnRateTimer -= Time.deltaTime;
        if (spawnRateTimer <= 0f)
        {
            AdjustSpawnInterval();
            spawnRateTimer = spawnRateTickInterval;
        }
    }

    void SpawnDish()
    {
        // ensure incrementEvery is at least 1 to avoid divide-by-zero
        int safeIncrementEvery = Mathf.Max(1, incrementEvery);

        // compute how many extra arrows to add based on how many dishes have been spawned
        //int extraArrows = dishesSpawned / safeIncrementEvery;
        int seqLength = Mathf.Max(1, startingArrowCount);

        Vector3 spawnPos = new Vector3(spawnX, offscreenY, 0f);
        GameObject go = Instantiate(dishPrefab, spawnPos, Quaternion.identity);

        // create a parent transform for the arrow icons on the dish if one wasn't assigned in the inspector
        Transform parentForIcons = arrowsParent;
        if (parentForIcons == null)
        {
            GameObject arrowsGO = new GameObject("ArrowsParent");
            arrowsGO.transform.SetParent(go.transform, false); // make it a child of the spawned dish
            arrowsGO.transform.localPosition = Vector3.zero;   // iconsOffset will adjust individual icon positions
            parentForIcons = arrowsGO.transform;
        }

        DishController controller = go.GetComponent<DishController>();
        if (controller != null)
        {
            int[] seq = GenerateRandomSequence(seqLength);

            // compute target position based on current stack size (stack up upward from targetY)
            int indexInStack = dishStack.Count; // this dish will be placed at this index
            Vector3 target = new Vector3(spawnX, targetY + indexInStack * stackSpacing, 0f);

            // pass manager-owned customization values into the dish controller
            controller.Init(
                seq,
                target,
                arrowIconPrefab,
                arrowSprite,
                iconSpacing,
                arrivalOffset,
                iconsOffset,
                iconScale,
                parentForIcons
            );

            // register dish in stack; bottom is at index 0
            dishStack.Add(controller);

            // subscribe to events
            controller.onCompleted += OnDishCompleted;

            // Only the bottom dish should accept input. If bottom has already arrived, enable it.
            UpdateBottomDishActiveState();

            // increment counter after spawn
            dishesSpawned++;
            
            // NOTE: spawn interval progression is now timer-based (see AdjustSpawnInterval).
 
            //Gio New: 
            UpdateDishesUI();
            // Check loss condition: if stack size exceeds allowed limit, player loses
            if (dishStack.Count > Mathf.Max(1, maxDishesToLose))
            {
                HandleGameOver();
            }
        }
        else
        {
            // if prefab doesn't have a DishController, still increment spawn counter to keep progression consistent
            dishesSpawned++;
        }
    }

    void OnDishCompleted(DishController dish)
    {
        if (dish == null) return;

        // increment washed count (player washed a dish)
        washedCount++;
        UpdateWashedUI();

        // unsubscribe
        dish.onCompleted -= OnDishCompleted;

        // remove from stack (dish destroys itself on completion)
        int removedIndex = dishStack.IndexOf(dish);
        if (removedIndex >= 0)
        {
            dishStack.RemoveAt(removedIndex);

            // shift remaining dishes down to fill the gap: recompute their targets
            for (int i = 0; i < dishStack.Count; i++)
            {
                Vector3 newTarget = new Vector3(spawnX, targetY + i * stackSpacing, 0f);
                dishStack[i].MoveTo(newTarget);
            }
        }

        // activate the new bottom dish if it's arrived
        UpdateDishesUI();
        UpdateBottomDishActiveState();
    }

    void UpdateBottomDishActiveState()
    {
        // disable all dishes by default
        for (int i = 0; i < dishStack.Count; i++)
        {
            if (dishStack[i] != null)
                dishStack[i].SetActive(false);
        }

        // enable the bottom dish (index 0) immediately, even if it's still moving/falling.
        if (dishStack.Count > 0)
        {
            DishController bottom = dishStack[0];
            if (bottom != null)
            {
                bottom.SetActive(true);
            }
        }
    }

    void HandleGameOver()
    {
        if (audioSource != null && brokenDishSound != null)
        {
            audioSource.PlayOneShot(brokenDishSound);
        }
        isGameOver = true;
        Debug.Log("Game Over: too many dishes!");

        // disable input on all dishes and stop their activity
        for (int i = 0; i < dishStack.Count; i++)
        {
            if (dishStack[i] != null)
                dishStack[i].SetActive(false);
        }

        // Create a simple full-screen black canvas + text and fade it in, then wait for any key to restart.
        GameObject canvasGO = new GameObject("GameOverCanvas");
        var canvas = canvasGO.AddComponent<UnityEngine.Canvas>();
        canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Background Image (covers whole screen)
        GameObject bgGO = new GameObject("FadeBackground");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0f); // start transparent

        // Message Text (TextMeshPro)
        GameObject textGO = new GameObject("GameOverText");
        textGO.transform.SetParent(canvasGO.transform, false);
        var txtRT = textGO.AddComponent<RectTransform>();
        txtRT.anchorMin = new Vector2(0.1f, 0.4f);
        txtRT.anchorMax = new Vector2(0.9f, 0.6f);
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;
        var txt = textGO.AddComponent<TextMeshProUGUI>();
        txt.text = "Game over! Press any key to restart";
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = new Color(1f, 1f, 1f, 0f); // start transparent
        if (washedTMPFont != null) txt.font = washedTMPFont;
        txt.fontSize = 36;
        txt.enableWordWrapping = true;

        // Dishes washed summary (TextMeshPro)
        GameObject washedGO = new GameObject("GameOverWashed");
        washedGO.transform.SetParent(canvasGO.transform, false);
        var washedRT = washedGO.AddComponent<RectTransform>();
        // use inspector-configurable anchors
        washedRT.anchorMin = gameOverWashedAnchorMin;
        washedRT.anchorMax = gameOverWashedAnchorMax;
        washedRT.offsetMin = Vector2.zero;
        washedRT.offsetMax = Vector2.zero;
        var washedTMP = washedGO.AddComponent<TextMeshProUGUI>();
        washedTMP.text = "Dishes Washed: " + washedCount.ToString();
        washedTMP.alignment = TextAlignmentOptions.Center;
        washedTMP.color = new Color(1f, 1f, 1f, 0f); // start transparent
        if (washedTMPFont != null) washedTMP.font = washedTMPFont;
        washedTMP.fontSize = 28;
        washedTMP.enableWordWrapping = true;

        // Start fade coroutine (now passes both texts)
        StartCoroutine(GameOverRoutine(bgImg, txt, washedTMP));
    }

    System.Collections.IEnumerator GameOverRoutine(UnityEngine.UI.Image bgImage, TextMeshProUGUI messageText, TextMeshProUGUI washedText)
    {
        float fadeDuration = 1.0f;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            bgImage.color = new Color(0f, 0f, 0f, a);
            messageText.color = new Color(messageText.color.r, messageText.color.g, messageText.color.b, a);
            if (washedText != null)
                washedText.color = new Color(washedText.color.r, washedText.color.g, washedText.color.b, a);
            yield return null;
        }

        // Use an InputAction to wait for any button using the new Input System
        bool anyPressed = false;
        var anyAction = new UnityEngine.InputSystem.InputAction("AnyButton", UnityEngine.InputSystem.InputActionType.Button);
        anyAction.AddBinding("<Keyboard>/anyKey");
        anyAction.AddBinding("<Gamepad>/*");
        System.Action<UnityEngine.InputSystem.InputAction.CallbackContext> onPerformed = (ctx) => anyPressed = true;
        anyAction.performed += onPerformed;
        anyAction.Enable();

        yield return new WaitUntil(() => anyPressed);

        anyAction.performed -= onPerformed;
        anyAction.Disable();
        anyAction.Dispose();

        // Reload current scene to restart the game
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    int[] GenerateRandomSequence(int length)
    {
        int[] seq = new int[length];
        for (int i = 0; i < length; i++)
            seq[i] = Random.Range(0, 4); // 0:left, 1:up, 2:right, 3:down
        return seq;
    }

    // UI helpers
    // Configurable pixel offset applied to the spawned washed counter UI
    public Vector2 washedSpawnOffset = Vector2.zero;

    void CreateWashedUI()
    {
        // try to reuse an existing Canvas if present
        Canvas existing = FindObjectOfType<Canvas>();
        GameObject canvasGO;
        Canvas canvas;
        if (existing != null)
        {
            canvas = existing;
            canvasGO = existing.gameObject;
        }
        else
        {
            canvasGO = new GameObject("UI_Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        GameObject textGO = new GameObject("WashedCounterText");
        textGO.transform.SetParent(canvasGO.transform, false);
        washedCounterText = textGO.AddComponent<TextMeshProUGUI>();
        if (washedTMPFont != null) washedCounterText.font = washedTMPFont;
        washedCounterText.fontSize = washedFontSize;
        washedCounterText.color = washedColor;
        washedCounterText.alignment = washedAlignment;

        RectTransform rt = washedCounterText.rectTransform;
        rt.anchorMin = washedAnchorMin;
        rt.anchorMax = washedAnchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Apply the configurable offset (in local anchored position units/pixels)
        // This lets you nudge the spawned text from the anchor rectangle set above.
        rt.anchoredPosition = washedSpawnOffset;
    }

    void CreateDishesUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("No Canvas found for CurrentDishes UI. Create Washed UI first or add a Canvas.");
            return;
        }

        GameObject textGO = new GameObject("CurrentDishesText");
        textGO.transform.SetParent(canvas.transform, false);
        currentDishesText = textGO.AddComponent<TextMeshProUGUI>();
        if (washedTMPFont != null) currentDishesText.font = washedTMPFont;
        currentDishesText.fontSize = dishesFontSize;
        currentDishesText.color = dishesColor;
        currentDishesText.alignment = dishesAlignment;

        RectTransform rt = currentDishesText.rectTransform;
        rt.anchorMin = dishesAnchorMin;
        rt.anchorMax = dishesAnchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = dishesSpawnOffset;
    }

    void CreateDifficultyUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("No Canvas found for Difficulty UI. Create Washed UI first or add a Canvas.");
            return;
        }

        GameObject textGO = new GameObject("DifficultyText");
        textGO.transform.SetParent(canvas.transform, false);
        difficultyText = textGO.AddComponent<TextMeshProUGUI>();
        if (washedTMPFont != null) difficultyText.font = washedTMPFont;
        difficultyText.fontSize = difficultyFontSize;
        difficultyText.color = difficultyColor;
        difficultyText.alignment = difficultyAlignment;

        RectTransform rt = difficultyText.rectTransform;
        rt.anchorMin = difficultyAnchorMin;
        rt.anchorMax = difficultyAnchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = difficultySpawnOffset;
    }

    void UpdateWashedUI()
    {
        if (washedCounterText != null)
        {
            washedCounterText.text = washedPrefix + washedCount.ToString();
        }
    }

    void UpdateDishesUI()
    {
        if (currentDishesText != null)
        {
            currentDishesText.text = dishesPrefix + dishStack.Count.ToString() + " / " + maxDishesToLose.ToString();
        }
    }

    void UpdateDifficultyUI()
    {
        if (difficultyText != null)
        {
            // Clamp difficulty display to 5
            int displayDifficulty = Mathf.Min(difficulty, 5);
            difficultyText.text = difficultyPrefix + displayDifficulty.ToString() + difficultySuffix;
        }
    }

    void UpdateAllHUD()
    {
        UpdateWashedUI();
        UpdateDishesUI();
        UpdateDifficultyUI();
    }
    
    // Adjust spawn interval on a timer tick. When the interval reaches the configured minimum,
    // reset it to the base value, increase starting arrow count (by 1) and bump difficulty.
    void AdjustSpawnInterval()
    {
        if (spawnInterval > minSpawnInterval)
        {
            spawnInterval = Mathf.Max(minSpawnInterval, spawnInterval - spawnIntervalDecay);
            Debug.Log($"Spawn interval decreased to {spawnInterval}");
            return;
        }

        // reached the fastest rate — reset and increase difficulty/arrow count
        spawnInterval = baseSpawnInterval;
        // increase arrow count by two each time difficulty increments
        startingArrowCount = Mathf.Max(1, startingArrowCount + 1);
        difficulty++;
        //Gio new
        UpdateDifficultyUI();
        Debug.Log($"Spawn rate tick reached min. Reset spawnInterval to {baseSpawnInterval}, startingArrowCount -> {startingArrowCount}, difficulty -> {difficulty}");
    }
}
