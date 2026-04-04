using UnityEngine;

[DisallowMultipleComponent]
public class UnitPresentationClamp : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform presentationRoot;
    [SerializeField] private SpriteRenderer presentationRenderer;

    [Header("Clamp")]
    [SerializeField] private float maxPresentationWidth = 0.9f;
    [SerializeField] private float maxPresentationHeight = 0.9f;
    [SerializeField] private Vector3 baseLocalScale = Vector3.one;

    private void Awake()
    {
        ApplyClamp();
    }

    private void OnEnable()
    {
        ApplyClamp();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyClamp();
    }
#endif

    public void ApplyClamp()
    {
        if (presentationRoot == null || presentationRenderer == null)
        {
            return;
        }

        Sprite sprite = presentationRenderer.sprite;
        if (sprite == null)
        {
            presentationRoot.localScale = baseLocalScale;
            return;
        }

        Vector2 spriteSize = sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f)
        {
            presentationRoot.localScale = baseLocalScale;
            return;
        }

        float widthScale = maxPresentationWidth > 0f ? maxPresentationWidth / spriteSize.x : 1f;
        float heightScale = maxPresentationHeight > 0f ? maxPresentationHeight / spriteSize.y : 1f;
        float clampScale = Mathf.Min(1f, widthScale, heightScale);

        presentationRoot.localScale = baseLocalScale * clampScale;
    }
}
