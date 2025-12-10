using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Simple hover feedback for UGUI buttons: color swap (no layout jitter).
/// </summary>
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Image targetImage;
    private Color normalColor = Color.white;
    private Color hoverColor = Color.white;

    public void Configure(Image image, Color normal, Color hover)
    {
        targetImage = image;
        normalColor = normal;
        hoverColor = hover;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetImage != null)
        {
            targetImage.color = hoverColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetImage != null)
        {
            targetImage.color = normalColor;
        }
    }
}
