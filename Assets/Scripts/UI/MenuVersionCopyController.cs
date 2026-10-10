using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

// Owns the existing menu labels' interaction and temporary copy feedback.
public sealed class MenuVersionCopyController : IDisposable
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CopyToClipboardWithResult(string text, string receiver, string method);
#endif

    private readonly string receiver;
    private readonly Label[] labels;
    private IVisualElementScheduledItem resetItem;
    private bool copying;
    private bool disposed;

    public MenuVersionCopyController(string receiver, params Label[] labels)
    {
        this.receiver = receiver;
        this.labels = labels;
        foreach (Label label in labels)
        {
            if (label == null) continue;
            label.pickingMode = PickingMode.Position;
            label.tooltip = "Tap to copy version";
            label.RegisterCallback<ClickEvent>(OnClicked);
        }
    }

    private void OnClicked(ClickEvent evt)
    {
        if (disposed || copying || evt.button != 0) return;
        evt.StopPropagation();
        resetItem?.Pause();
        resetItem = null;
        copying = true;
        string text = MenuVersionLabel.BuildVersionText();
#if UNITY_WEBGL && !UNITY_EDITOR
        SetText("Copying…");
        CopyToClipboardWithResult(text, receiver, "OnMenuVersionCopyResult");
#else
        try { Complete(ClipboardUtility.TryCopy(text)); }
        catch (Exception) { Complete(false); }
#endif
    }

    public void Complete(bool success)
    {
        if (disposed || !copying) return;
        copying = false;
        SetText(success ? "Copied!" : "Couldn't copy — tap to retry");
        foreach (Label label in labels)
        {
            if (label == null || label.panel == null) continue;
            resetItem = label.schedule.Execute(() =>
            {
                resetItem = null;
                SetText(MenuVersionLabel.BuildVersionText());
            }).StartingIn(2000);
            break;
        }
    }

    private void SetText(string text)
    {
        foreach (Label label in labels)
            if (label != null) label.text = text;
    }

    public void Dispose()
    {
        disposed = true;
        resetItem?.Pause();
        resetItem = null;
        foreach (Label label in labels)
        {
            if (label == null) continue;
            label.UnregisterCallback<ClickEvent>(OnClicked);
            label.text = MenuVersionLabel.BuildVersionText();
        }
    }
}
