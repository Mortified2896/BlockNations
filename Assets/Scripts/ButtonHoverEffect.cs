using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Simple hover feedback for UGUI buttons: color swap (no layout jitter).
/// </summary>
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Image targetImage;
    private Color normalColor = Color.white;
    private Color hoverColor = Color.white;
    private Color pressedColor = Color.white;

    public void Configure(Image image, Color normal, Color hover, Color pressed)
    {
        targetImage = image;
        normalColor = normal;
        hoverColor = hover;
        pressedColor = pressed;
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

    public void OnPointerDown(PointerEventData eventData)
    {
        if (targetImage != null)
        {
            targetImage.color = pressedColor;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (targetImage != null)
        {
            // When pointer is released, treat as hover if still over the button.
            targetImage.color = hoverColor;
        }
    }
}
