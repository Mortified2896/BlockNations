using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OfficialUnitRegistrationValidator
{
    private const string MenuItemPath = "Tools/Block Nations/Validate Official Unit Registrations";

    [MenuItem(MenuItemPath)]
    public static void ValidateActiveScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            Debug.LogError("Official unit validation failed: no active scene is loaded.");
            return;
        }

        TurnManager turnManager = Object.FindFirstObjectByType<TurnManager>();
        if (turnManager == null)
        {
            Debug.LogError($"Official unit validation failed: no TurnManager found in active scene '{activeScene.path}'.");
            return;
        }

        List<string> issues = new List<string>();
        Dictionary<string, int> seenTypeIds = new Dictionary<string, int>(System.StringComparer.Ordinal);
        Dictionary<int, string> recruitOrderOwners = new Dictionary<int, string>();
        List<TurnManager.OfficialUnitRegistration> registrations = turnManager.officialUnitRegistrations;

        if (registrations == null || registrations.Count == 0)
        {
            issues.Add("TurnManager has no official unit registrations configured.");
        }
        else
        {
            for (int i = 0; i < registrations.Count; i++)
            {
                TurnManager.OfficialUnitRegistration registration = registrations[i];
                if (registration == null)
                {
                    issues.Add($"Registration {i} is null.");
                    continue;
                }

                string normalizedTypeId = UnitRegistry.NormalizeTypeId(registration.unitTypeId);
                if (string.IsNullOrWhiteSpace(registration.unitTypeId))
                {
                    issues.Add($"Registration {i} has an empty unitTypeId.");
                }

                if (!UnitRegistry.TryGetDefinition(normalizedTypeId, out UnitDefinition definition))
                {
                    issues.Add($"Registration '{registration.unitTypeId}' does not exist in UnitRegistry.");
                }

                if (seenTypeIds.TryGetValue(normalizedTypeId, out int existingIndex))
                {
                    issues.Add($"Registration '{normalizedTypeId}' is duplicated at indexes {existingIndex} and {i}.");
                }
                else
                {
                    seenTypeIds[normalizedTypeId] = i;
                }

                if (registration.prefab == null)
                {
                    issues.Add($"Registration '{normalizedTypeId}' has no prefab assigned.");
                    continue;
                }

                Unit prefabUnit = registration.prefab.GetComponent<Unit>();
                if (prefabUnit == null)
                {
                    issues.Add($"Registration '{normalizedTypeId}' prefab '{registration.prefab.name}' has no Unit component.");
                }
                else if (!string.Equals(prefabUnit.UnitTypeId, normalizedTypeId, System.StringComparison.Ordinal))
                {
                    issues.Add(
                        $"Registration '{normalizedTypeId}' prefab '{registration.prefab.name}' serializes unitTypeId '{prefabUnit.UnitTypeId}'.");
                }

                if (definition != null &&
                    !string.Equals(UnitRegistry.NormalizeTypeId(definition.PrefabTypeId), normalizedTypeId, System.StringComparison.Ordinal))
                {
                    issues.Add(
                        $"Registration '{normalizedTypeId}' does not match UnitRegistry prefabTypeId '{definition.PrefabTypeId}'.");
                }

                if (!registration.recruitable)
                {
                    continue;
                }

                if (recruitOrderOwners.TryGetValue(registration.recruitDisplayOrder, out string existingTypeId))
                {
                    issues.Add(
                        $"Recruit display order {registration.recruitDisplayOrder} is shared by '{existingTypeId}' and '{normalizedTypeId}'.");
                }
                else
                {
                    recruitOrderOwners[registration.recruitDisplayOrder] = normalizedTypeId;
                }
            }
        }

        if (issues.Count == 0)
        {
            Debug.Log(
                $"Official unit validation passed for scene '{activeScene.path}' with {registrations.Count} registration(s).",
                turnManager);
            return;
        }

        Debug.LogError(
            $"Official unit validation found {issues.Count} issue(s) in scene '{activeScene.path}':\n- {string.Join("\n- ", issues)}",
            turnManager);
        EditorGUIUtility.PingObject(turnManager);
    }

    [MenuItem(MenuItemPath, true)]
    private static bool ValidateActiveSceneMenu()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid() && activeScene.isLoaded && !EditorApplication.isPlaying;
    }
}
