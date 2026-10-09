using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

// Explicit UXML references, shared by the existing menu and gameplay documents.
// Does not serialize game state or change turn/AI progression.
public sealed class FeedbackUITKController : IDisposable
{
    [Serializable]
    private sealed class Report
    {
        public string id, category, description, version, platform, screen, mode, screenshot;
        public int turn;
        public bool attempted;
    }

    [Serializable]
    private sealed class Receipt { public string id, error; public bool saved; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BlockNations_OpenFeedback(string report, string objectName);
    [DllImport("__Internal")] private static extern void BlockNations_CloseFeedback();
    private bool browserOpen, previousKeyboardCapture;
#endif
    public void BrowserClosed()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!browserOpen) return;
        browserOpen = false;
        WebGLInput.captureAllKeyboardInput = previousKeyboardCapture;
        button.Focus();
#endif
    }

    private const string DraftKey = "BlockNations.Feedback.Draft";
    private readonly MonoBehaviour owner;
    private readonly VisualElement root;
    private readonly Label version;
    private readonly VisualElement topBar;
    private int siblingIndex = -1;
    private readonly Func<string> screen, mode;
    private readonly Func<int> turn;
    private readonly Button button, cancel, send, remove;
    private readonly VisualElement overlay;
    private readonly DropdownField category;
    private readonly TextField description;
    private readonly Toggle include;
    private readonly Image preview;
    private readonly Label status;
    private Texture2D screenshot;
    private string screenshotBase64 = "";
    private Report draft;
    private string pendingPayload;
    private Coroutine routine;
    private bool sending, capturing, sent, disposed;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;

    public FeedbackUITKController(MonoBehaviour owner, VisualElement root, Func<string> screen, Func<string> mode, Func<int> turn)
    {
        this.owner = owner;
        this.root = root;
        version = root.Q<Label>("VersionLabel");
        topBar = root.Q<VisualElement>("TopBar");
        this.screen = screen;
        this.mode = mode;
        this.turn = turn;
        button = root.Q<Button>("FeedbackButton");
        overlay = root.Q<VisualElement>("FeedbackOverlay");
        category = root.Q<DropdownField>("FeedbackCategory");
        description = root.Q<TextField>("FeedbackDescription");
        include = root.Q<Toggle>("FeedbackIncludeScreenshot");
        preview = root.Q<Image>("FeedbackScreenshot");
        status = root.Q<Label>("FeedbackStatus");
        cancel = root.Q<Button>("FeedbackCancel");
        send = root.Q<Button>("FeedbackSend");
        remove = root.Q<Button>("FeedbackRemoveScreenshot");
        if (button == null || overlay == null || category == null || description == null || include == null || preview == null || status == null || cancel == null || send == null || remove == null)
            throw new InvalidOperationException("Feedback UXML references are incomplete.");
        // The gameplay HUD otherwise deliberately passes pointer events through.
        overlay.pickingMode = PickingMode.Position;
        button.pickingMode = PickingMode.Position;
        category.choices = new List<string> { "Bug", "Suggestion", "Other" };
        preview.scaleMode = ScaleMode.ScaleToFit;
        button.clicked += Open;
        cancel.clicked += Close;
        send.clicked += Send;
        remove.clicked += RemoveScreenshot;
        category.RegisterValueChangedCallback(OnCategoryChanged);
        description.RegisterValueChangedCallback(OnDescriptionChanged);
        include.RegisterValueChangedCallback(OnIncludeChanged);
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        overlay.RegisterCallback<KeyDownEvent>(OnKeyDown);
        RefreshSafeArea();
    }

    private void OnGeometryChanged(GeometryChangedEvent evt) { RefreshSafeArea(true); }
    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape && !sending) { Close(); evt.StopPropagation(); }
    }

    public void RefreshSafeArea(bool force = false)
    {
        if (disposed || Screen.width <= 0 || Screen.height <= 0 || root.panel == null) return;
        var size = new Vector2Int(Screen.width, Screen.height);
        if (!force && lastSafeArea == Screen.safeArea && lastScreenSize == size) return;
        lastSafeArea = Screen.safeArea;
        lastScreenSize = size;
        // Convert device pixels to panel units before applying corner offsets.
        var topLeft = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.safeArea.xMin, Screen.height - Screen.safeArea.yMax));
        var bottomRight = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.safeArea.xMax, Screen.height - Screen.safeArea.yMin));
        var corners = button.parent.worldBound;
        // Narrow browser canvases need a utility row above the turn/gold labels.
        if (topBar != null) topBar.style.paddingTop = corners.width < 640 ? new StyleLength(128) : new StyleLength(StyleKeyword.Null);
        button.style.top = Mathf.Max(0, topLeft.y - corners.yMin) + 16;
        button.style.right = Mathf.Max(0, corners.xMax - bottomRight.x) + 16;
        if (version != null)
        {
            version.style.top = Mathf.Max(0, topLeft.y - corners.yMin) + 16;
            version.style.left = Mathf.Max(0, topLeft.x - corners.xMin) + 16;
        }
        var bounds = overlay.parent.worldBound;
        overlay.style.paddingTop = Mathf.Max(0, topLeft.y - bounds.yMin) + 24;
        overlay.style.paddingBottom = Mathf.Max(0, bounds.yMax - bottomRight.y) + 24;
        overlay.style.paddingLeft = Mathf.Max(0, topLeft.x - bounds.xMin) + 24;
        overlay.style.paddingRight = Mathf.Max(0, bounds.xMax - bottomRight.x) + 24;
    }

    private void Open()
    {
        if (capturing || sending || disposed) return;
        sent = false;
        pendingPayload = null;
        try { draft = JsonUtility.FromJson<Report>(PlayerPrefs.GetString(DraftKey, "")); }
        catch { draft = null; }
        if (draft == null || string.IsNullOrEmpty(draft.id)) draft = new Report { id = Guid.NewGuid().ToString("N"), category = "Bug", description = "" };
        draft.screen = screen();
        draft.mode = mode();
        draft.turn = Mathf.Max(0, turn());
        draft.version = Application.version;
        draft.platform = Application.platform.ToString();
        category.SetValueWithoutNotify(category.choices.Contains(draft.category) ? draft.category : "Bug");
        description.SetValueWithoutNotify(draft.description ?? "");
        include.SetValueWithoutNotify(true);
        cancel.text = "Cancel";
        send.style.display = DisplayStyle.Flex;
        SetSending(false);
        capturing = true;
        button.SetEnabled(false);
        routine = owner.StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        // Capture the rendered player view before displaying the report overlay.
        yield return new WaitForEndOfFrame();
        draft.screen = screen();
        draft.mode = mode();
        draft.turn = Mathf.Max(0, turn());
#if UNITY_WEBGL && !UNITY_EDITOR
        draft.screenshot = ""; // Browser captures the rendered canvas in this frame.
        previousKeyboardCapture = WebGLInput.captureAllKeyboardInput;
        WebGLInput.captureAllKeyboardInput = false;
        browserOpen = true;
        BlockNations_OpenFeedback(JsonUtility.ToJson(draft), owner.gameObject.name);
        draft = null; // Browser drafts are owned by the browser form.
        capturing = false;
        button.SetEnabled(true);
        routine = null;
        yield break;
#endif
        string message = "";
        Texture2D full = null;
        try
        {
            ReleaseScreenshot();
            full = ScreenCapture.CaptureScreenshotAsTexture();
            if (full == null) throw new InvalidOperationException();
            float scale = Mathf.Min(1f, 1280f / Mathf.Max(full.width, full.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(full.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(full.height * scale));
            // Copy encoded screen pixels directly to preserve the screenshot's
            // colors across linear/sRGB rendering on WebGL and native players.
            var sourcePixels = full.GetPixels32();
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = sourcePixels[(y * full.height / height) * full.width + x * full.width / width];
            screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            screenshot.SetPixels32(pixels);
            screenshot.Apply();
            byte[] bytes = screenshot.EncodeToJPG(70);
            if (bytes.Length > 1000000) throw new InvalidOperationException();
            screenshotBase64 = Convert.ToBase64String(bytes);
            preview.image = screenshot;
        }
        catch
        {
            ReleaseScreenshot();
            message = "Screenshot couldn't be captured. You can still send a text report.";
        }
        finally
        {
            if (full != null) UnityEngine.Object.Destroy(full);
        }
        capturing = false;
        button.SetEnabled(true);
        include.SetEnabled(screenshot != null);
        include.SetValueWithoutNotify(screenshot != null);
        remove.style.display = screenshot != null ? DisplayStyle.Flex : DisplayStyle.None;
        preview.style.display = screenshot != null ? DisplayStyle.Flex : DisplayStyle.None;
        status.text = message;
#if !UNITY_WEBGL || UNITY_EDITOR
        status.text = "Feedback submission is available in the browser playtest. Your written draft is kept on this device.";
        send.SetEnabled(false);
#endif

        overlay.style.display = DisplayStyle.Flex;
        // The modal must sit above the other explicitly wired gameplay panels.
        siblingIndex = root.parent != null ? root.parent.IndexOf(root) : -1;
        if (siblingIndex >= 0 && siblingIndex < root.parent.childCount - 1) root.BringToFront();
        // Focus after the hidden modal has completed its layout/visibility pass.
        description.schedule.Execute(() => description.Focus()).ExecuteLater(16);
        routine = null;
    }

    private void OnCategoryChanged(ChangeEvent<string> evt) { Changed(); }
    private void OnDescriptionChanged(ChangeEvent<string> evt) { Changed(); }
    private void OnIncludeChanged(ChangeEvent<bool> evt)
    {
        preview.style.display = evt.newValue && screenshot != null ? DisplayStyle.Flex : DisplayStyle.None;
        Changed();
    }

    private void Changed()
    {
        if (sending || sent || draft == null) return;
        // An edit after a failed send starts a new logical submission. Exact retries
        // preserve the frozen payload and ID in case the server already saved it.
        if (draft.attempted) { draft.id = Guid.NewGuid().ToString("N"); draft.attempted = false; pendingPayload = null; }
        SaveDraft();
    }

    private void SaveDraft()
    {
        if (draft == null || sent) return;
        draft.category = category.value;
        draft.description = description.value;
        draft.screenshot = ""; // Attachments remain local in memory, never in preferences.
        PlayerPrefs.SetString(DraftKey, JsonUtility.ToJson(draft));
        PlayerPrefs.Save();
    }

    private void RemoveScreenshot()
    {
        if (sending) return;
        include.SetValueWithoutNotify(false);
        ReleaseScreenshot();
        include.SetEnabled(false);
        remove.style.display = DisplayStyle.None;
        preview.style.display = DisplayStyle.None;
        Changed();
    }

    private void Send()
    {
        if (sending || sent || disposed) return;
        if (string.IsNullOrWhiteSpace(description.value)) { status.text = "Please add a description."; description.Focus(); return; }
        draft.attempted = true;
        SaveDraft();
        if (pendingPayload == null)
        {
            draft.screenshot = include.value ? screenshotBase64 : "";
            pendingPayload = JsonUtility.ToJson(draft);
            draft.screenshot = "";
        }
        SetSending(true);
        status.text = "Sending…";
        routine = owner.StartCoroutine(Submit());
    }

    private IEnumerator Submit()
    {
        string endpoint = new Uri(new Uri(Application.absoluteURL), "/api/feedback").AbsoluteUri;
        using (var request = new UnityWebRequest(endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(pendingPayload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30;
            yield return request.SendWebRequest();
            Receipt receipt = null;
            try { receipt = JsonUtility.FromJson<Receipt>(request.downloadHandler.text); } catch { }
            SetSending(false);
            if (request.result == UnityWebRequest.Result.Success && receipt != null && receipt.saved && receipt.id == draft.id)
            {
                sent = true;
                PlayerPrefs.DeleteKey(DraftKey);
                PlayerPrefs.Save();
                status.text = "Feedback sent. Thank you!";
                cancel.text = "Done";
                send.style.display = DisplayStyle.None;
                category.SetEnabled(false);
                description.SetEnabled(false);
                include.SetEnabled(false);
                remove.SetEnabled(false);
            }
            else status.text = !string.IsNullOrEmpty(receipt?.error) ? receipt.error : "Couldn't confirm the report was sent. Your report is kept here; press Send to retry.";
        }
        routine = null;
    }

    private void SetSending(bool value)
    {
        sending = value;
        category.SetEnabled(!value);
        description.SetEnabled(!value);
        include.SetEnabled(!value && screenshot != null);
        remove.SetEnabled(!value);
        cancel.SetEnabled(!value);
        send.SetEnabled(!value);
    }

    private void Close()
    {
        if (sending) return;
        SaveDraft();
        overlay.style.display = DisplayStyle.None;
        RestoreSiblingOrder();
        ReleaseScreenshot();
        button.Focus();
    }

    private void ReleaseScreenshot()
    {
        preview.image = null;
        if (screenshot != null) UnityEngine.Object.Destroy(screenshot);
        screenshot = null;
        screenshotBase64 = "";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (browserOpen) { BlockNations_CloseFeedback(); BrowserClosed(); }
#endif
        if (routine != null && owner != null) owner.StopCoroutine(routine);
        SaveDraft();
        button.clicked -= Open;
        cancel.clicked -= Close;
        send.clicked -= Send;
        remove.clicked -= RemoveScreenshot;
        category.UnregisterValueChangedCallback(OnCategoryChanged);
        description.UnregisterValueChangedCallback(OnDescriptionChanged);
        include.UnregisterValueChangedCallback(OnIncludeChanged);
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        overlay.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        overlay.style.display = DisplayStyle.None;
        RestoreSiblingOrder();
        ReleaseScreenshot();
    }

    private void RestoreSiblingOrder()
    {
        if (siblingIndex >= 0 && root.parent != null)
            root.parent.Insert(Mathf.Min(siblingIndex, root.parent.childCount - 1), root);
        siblingIndex = -1;
    }
}
