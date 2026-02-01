#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class UIClickInterceptorDebugger : MonoBehaviour
{
    private const int MaxHitsToLog = 10;
    private static bool s_bootstrapped;
    private readonly List<RaycastResult> _results = new List<RaycastResult>(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped)
        {
            return;
        }

        s_bootstrapped = true;

        if (FindObjectOfType<UIClickInterceptorDebugger>() != null)
        {
            return;
        }

        var go = new GameObject("UIClickInterceptorDebugger");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<UIClickInterceptorDebugger>();
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug.LogError("[UIClickInterceptorDebugger] No EventSystem found in the scene.");
            return;
        }

        var mousePosition = Input.mousePosition;
        var pointerData = new PointerEventData(eventSystem)
        {
            position = mousePosition
        };

        _results.Clear();
        eventSystem.RaycastAll(pointerData, _results);

        var builder = new StringBuilder(512);
        builder.AppendLine("[UIClickInterceptorDebugger] UI click raycast");
        builder.Append("Mouse: ").Append(mousePosition).AppendLine();
        builder.Append("Pointer over UI: ").Append(eventSystem.IsPointerOverGameObject()).AppendLine();
        builder.Append("Raycast hits: ").Append(_results.Count).AppendLine();
        builder.Append("Selected: ").Append(FormatGameObject(eventSystem.currentSelectedGameObject)).AppendLine();

        var limit = Mathf.Min(MaxHitsToLog, _results.Count);
        for (var i = 0; i < limit; i++)
        {
            var hit = _results[i];
            builder.Append(i + 1).Append(". ");
            builder.Append(hit.gameObject != null ? hit.gameObject.name : "<null>");
            builder.Append(" | Path: ").Append(GetTransformPath(hit.gameObject != null ? hit.gameObject.transform : null));
            builder.Append(" | Module: ").Append(hit.module != null ? hit.module.GetType().Name : "<null>");
            builder.Append(" | Sorting: ").Append(FormatSorting(hit.sortingLayer, hit.sortingOrder));
            builder.Append(" | Distance: ").Append(hit.distance.ToString("F3"));
            builder.AppendLine();
        }

        Debug.Log(builder.ToString());
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        var parts = new Stack<string>();
        while (transform != null)
        {
            parts.Push(transform.name);
            transform = transform.parent;
        }

        return string.Join("/", parts);
    }

    private static string FormatGameObject(GameObject obj)
    {
        return obj != null ? obj.name : "<none>";
    }

    private static string FormatSorting(int sortingLayerId, int sortingOrder)
    {
        var layerName = SortingLayer.IDToName(sortingLayerId);
        if (string.IsNullOrEmpty(layerName))
        {
            layerName = sortingLayerId.ToString();
        }

        return layerName + " / " + sortingOrder;
    }
}
#endif
