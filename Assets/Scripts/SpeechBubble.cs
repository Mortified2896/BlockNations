using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class SpeechBubble
{
    private static Sprite cachedWhiteSprite;

    private static Sprite GetWhiteSprite()
    {
        if (cachedWhiteSprite != null)
            return cachedWhiteSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cachedWhiteSprite;
    }

    public static void Show(Transform target, string text, float seconds = 2.2f, Vector3 worldOffset = default)
    {
        if (target == null)
            return;

        if (worldOffset == default)
            worldOffset = new Vector3(0f, 0.9f, 0f);

        target.gameObject.AddComponent<SpeechBubbleRunner>().ShowInternal(text, seconds, worldOffset);
    }

    private class SpeechBubbleRunner : MonoBehaviour
    {
        private Coroutine routine;

        public void ShowInternal(string text, float seconds, Vector3 worldOffset)
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = StartCoroutine(Run(text, seconds, worldOffset));
        }

        private IEnumerator Run(string text, float seconds, Vector3 worldOffset)
        {
            GameObject root = new GameObject("SpeechBubble", typeof(RectTransform));
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = worldOffset;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 900;
            root.AddComponent<CanvasScaler>();

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(3.2f, 1.2f);
            rt.localScale = Vector3.one * 0.25f;

            GameObject bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(root.transform, false);
            RectTransform bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            Image bgImg = bg.GetComponent<Image>();
            bgImg.sprite = GetWhiteSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0.08f, 0.10f, 0.14f, 0.92f);

            Outline outline = bg.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(3f, -3f);

            GameObject txt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txt.transform.SetParent(root.transform, false);
            RectTransform txtRt = txt.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(14f, 10f);
            txtRt.offsetMax = new Vector2(-14f, -10f);

            TextMeshProUGUI tmp = txt.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 44f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;

            yield return new WaitForSeconds(seconds);
            if (root != null)
                Destroy(root);
            routine = null;
        }
    }
}
