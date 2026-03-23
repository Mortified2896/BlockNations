using UnityEngine;

/// <summary>
/// Owns visibility for the shared bottom strip roots so only one mode is active at a time.
/// </summary>
public class BottomStripController : MonoBehaviour
{
    public enum BottomStripMode
    {
        DefaultHud = 0,
        UnitUi = 1,
        CityUi = 2
    }

    public static BottomStripController Instance { get; private set; }

    [Header("Bottom Strip Roots")]
    [SerializeField] private GameObject defaultHudRoot;
    [SerializeField] private GameObject bottomPanelsRoot;
    [SerializeField] private BottomStripMode initialMode = BottomStripMode.DefaultHud;

    public BottomStripMode CurrentMode { get; private set; } = BottomStripMode.DefaultHud;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ApplyMode(initialMode, force: true);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetMode(BottomStripMode mode)
    {
        ApplyMode(mode, force: false);
    }

    public void ReleaseMode(BottomStripMode mode)
    {
        if (CurrentMode != mode)
            return;

        ApplyMode(BottomStripMode.DefaultHud, force: false);
    }

    private void ApplyMode(BottomStripMode mode, bool force)
    {
        if (!force && CurrentMode == mode)
            return;

        CurrentMode = mode;

        bool showDefaultHud = mode == BottomStripMode.DefaultHud;
        SetActiveIfChanged(defaultHudRoot, showDefaultHud);
        SetActiveIfChanged(bottomPanelsRoot, !showDefaultHud);
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target == null || target.activeSelf == active)
            return;

        target.SetActive(active);
    }
}
