using System;
using System.Linq;
using BlockNations.AI;
using Unity.InferenceEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TrainingSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/LocalTraining.unity";

    [MenuItem("Tools/Block Nations/Local ML Training/Create Training Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before creating the arena.");
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        // Saving to a new path preserves the product scene and all its inspector references.
        EditorSceneManager.SaveScene(scene, ScenePath);
        TurnManager manager = Root<TurnManager>(scene, "TurnManager");
        GridManager grid = Root<GridManager>(scene, "GridManager");
        Camera camera = Root<Camera>(scene, "Main Camera");
        Set(manager, "externallyDrivenMatch", true);
        Set(manager, "externalHumanSeatIndex", -1);
        Set(manager, "autoSaveEnabled", false);
        Set(manager, "autoEndTurnWhenNoActions", false);
        Set(manager, "playMusicOnStart", false);
        Set(manager, "enableAIVsAIDebugMode", false);
        Set(grid, "externallyDrivenBoard", true);
        grid.width = grid.height = LearnedActionSchema.BoardSize;
        camera.orthographic = true;
        camera.orthographicSize = 6.5f;
        camera.transform.position = new Vector3(0, 0, -10);
        CameraController controller = camera.GetComponent<CameraController>();
        if (controller != null) controller.enabled = false;

        GameObject arenaObject = new GameObject("LocalTrainingArena");
        TrainingArena arena = arenaObject.AddComponent<TrainingArena>();
        Set(arena, "turnManager", manager);
        Set(arena, "boardCamera", camera);
        SerializedObject arenaFields = new SerializedObject(arena);
        SerializedProperty seatReferences = arenaFields.FindProperty("seats");
        seatReferences.arraySize = 2;
        for (int seat = 0; seat < 2; seat++)
        {
            GameObject actor = new GameObject("Learning Seat " + seat);
            actor.transform.SetParent(arenaObject.transform);
            BehaviorParameters behavior = actor.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = LearnedActionSchema.BehaviorName;
            behavior.TeamId = seat; // ML opponent routing, independent of game team rules.
            behavior.BrainParameters.VectorObservationSize = LearnedActionSchema.ObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(LearnedActionSchema.ActionCount);
            behavior.UseChildSensors = behavior.UseChildActuators = false;
            behavior.DeterministicInference = true;
            behavior.InferenceDevice = InferenceDevice.Burst;
            TrainingSeatAgent agent = actor.AddComponent<TrainingSeatAgent>();
            agent.MaxStep = 0;
            Set(agent, "arena", arena);
            Set(agent, "seatIndex", seat);
            seatReferences.GetArrayElementAtIndex(seat).objectReferenceValue = agent;
        }
        arenaFields.ApplyModifiedPropertiesWithoutUndo();
        ConfigurePresentation(scene, human: false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ML Training] Created explicitly wired 11x11 arena: " + ScenePath);
    }

    public static TrainingArena Prepare(string statusPath, int seed, bool curriculum, bool requireTrainer,
        ModelAsset model = null, bool human = false)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop the current game before preparing training.");
        if (!System.IO.File.Exists(ScenePath)) Create();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TrainingArena arena = Root<TrainingArena>(scene, "LocalTrainingArena");
        TurnManager manager = Root<TurnManager>(scene, "TurnManager");
        Set(arena, "statusPath", statusPath);
        Set(arena, "seed", seed);
        Set(arena, "useCurriculum", curriculum);
        Set(arena, "requireTrainer", requireTrainer);
        Set(arena, "humanSeatIndex", human ? 0 : -1);
        Set(manager, "externalHumanSeatIndex", human ? 0 : -1);
        foreach (BehaviorParameters behavior in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BehaviorParameters>(true)))
        {
            behavior.BehaviorName = LearnedActionSchema.BehaviorName;
            behavior.BehaviorType = requireTrainer ? BehaviorType.Default : model != null ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
            behavior.Model = model;
        }
        ConfigurePresentation(scene, human);
        return arena;
    }

    public static void ShowBoard()
    {
        HardAIReviewMenu.UseLaptopGameView();
        EditorWindow board = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        // A previously detached phone preview retains its narrow host window even
        // when its pane is maximized. Give that host a usable laptop rectangle.
        if (!board.docked)
        {
            board.maximized = false;
            board.position = new Rect(60, 60, 1200, 720);
        }
        board.Focus();
    }

    private static void ConfigurePresentation(Scene scene, bool human)
    {
        string[] roots = { "GameplayUnitPanelUITK", "UITK_GameplayHUD_SafeArea", "GameplayBottomHudUITK", "GameplayTopHudUITK",
            "UnitUIManager", "GameplayCityPanelUITK", "CityUIManager", "UITK_GameplayHUD_Root", "GameMenuActions" };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (Array.IndexOf(roots, root.name) >= 0) root.SetActive(human);
            if (root.name == "UnitSelectionManager" || root.name == "HoverManager")
                foreach (MonoBehaviour component in root.GetComponents<MonoBehaviour>()) component.enabled = human;
        }
    }

    private static T Root<T>(Scene scene, string name) where T : Component
    {
        GameObject root = scene.GetRootGameObjects().SingleOrDefault(candidate => candidate.name == name);
        T component = root != null ? root.GetComponent<T>() : null;
        if (component == null) throw new InvalidOperationException("Scene wiring is missing " + name + " / " + typeof(T).Name);
        return component;
    }

    public static void Set(UnityEngine.Object target, string property, object value)
    {
        SerializedObject fields = new SerializedObject(target);
        SerializedProperty field = fields.FindProperty(property);
        if (field == null) throw new InvalidOperationException("Missing serialized field " + property);
        if (value is bool flag) field.boolValue = flag;
        else if (value is int number) field.intValue = number;
        else if (value is string text) field.stringValue = text;
        else field.objectReferenceValue = (UnityEngine.Object)value;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }
}
