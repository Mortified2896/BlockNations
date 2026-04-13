using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class UITKResponsiveSizeTierControllerTests
{
    private const string ResponsiveControllerTypeName = "UITKResponsiveSizeTierController";

    [Test]
    public void IPhone7ResponsiveSize_NormalizesIntoCompactRange()
    {
        Vector2 responsiveSize = ComputeResponsiveSize(new Vector2(750f, 1334f), 326f);

        Assert.That(responsiveSize.x, Is.EqualTo(368.098f).Within(0.01f));
        Assert.That(responsiveSize.y, Is.EqualTo(654.724f).Within(0.01f));
        Assert.Less(responsiveSize.x, 428f, "Classic iPhone width should normalize below the wide-phone threshold.");
        Assert.Less(responsiveSize.y, 900f, "Classic iPhone height should normalize below the wide-phone threshold.");
    }

    [Test]
    public void IPhone7ResponsiveSize_DoesNotUseWidePhoneMenuLayout()
    {
        Vector2 responsiveSize = ComputeResponsiveSize(new Vector2(750f, 1334f), 326f);

        Assert.IsFalse(ShouldUseWidePhoneMenuLayout(responsiveSize));
    }

    [Test]
    public void LargerPhoneResponsiveSize_StillUsesWidePhoneMenuLayout()
    {
        Vector2 responsiveSize = ComputeResponsiveSize(new Vector2(1284f, 2778f), 458f);

        Assert.IsTrue(ShouldUseWidePhoneMenuLayout(responsiveSize));
    }

    private static Vector2 ComputeResponsiveSize(Vector2 safeAreaSize, float dpi)
    {
        System.Type responsiveControllerType = typeof(MainMenuUITKView).Assembly.GetType(ResponsiveControllerTypeName);
        Assert.NotNull(responsiveControllerType, $"Expected runtime type {ResponsiveControllerTypeName}.");

        MethodInfo method = responsiveControllerType.GetMethod(
            "ComputeResponsiveSize",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(Vector2), typeof(float) },
            modifiers: null);

        Assert.NotNull(method, "Expected ComputeResponsiveSize(Vector2, float) helper.");
        return (Vector2)method.Invoke(null, new object[] { safeAreaSize, dpi });
    }

    private static bool ShouldUseWidePhoneMenuLayout(Vector2 responsiveSize)
    {
        MethodInfo method = typeof(MainMenuUITKView).GetMethod(
            "ShouldUseWidePhoneMenuLayout",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method, "Expected ShouldUseWidePhoneMenuLayout(Vector2) helper.");
        return (bool)method.Invoke(null, new object[] { responsiveSize });
    }
}
