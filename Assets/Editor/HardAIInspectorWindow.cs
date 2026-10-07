using System.Text;
using BlockNations.AI;
using UnityEditor;
using UnityEngine;

public sealed class HardAIInspectorWindow : EditorWindow
{
    private Vector2 scroll;
    private int selectedCandidate;
    private double lastRefresh;

    [MenuItem("Window/Block Nations/Hard AI Inspector")]
    public static void Open() => GetWindow<HardAIInspectorWindow>("Hard AI Inspector");

    private void OnEnable()
    {
        EditorApplication.update += Refresh;
        SceneView.duringSceneGui += DrawPlan;
    }
    private void OnDisable()
    {
        EditorApplication.update -= Refresh;
        SceneView.duringSceneGui -= DrawPlan;
        HardAIDiagnostics.PauseBeforeAction = false;
    }
    private void Refresh()
    {
        if (EditorApplication.timeSinceStartup - lastRefresh < 0.2) return;
        lastRefresh = EditorApplication.timeSinceStartup;
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Development analysis. Scores are heuristic values, not win probabilities. Plans use only the acting seat's observation; later steps are predictions and are rechecked before execution.", MessageType.Info);
        HardAIDiagnostics.PauseBeforeAction = EditorGUILayout.Toggle("Pause before executing", HardAIDiagnostics.PauseBeforeAction);
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !HardAIDiagnostics.PauseBeforeAction))
            if (GUILayout.Button("Execute one action")) HardAIDiagnostics.RequestStep();
        IAIDecision search = HardAIDiagnostics.Search;
        AIObservation observation = HardAIDiagnostics.Observation;
        if (search == null || observation == null)
        {
            EditorGUILayout.LabelField("Waiting for a Hard Tactician decision. No tournament is started by this window.", EditorStyles.wordWrappedLabel);
            return;
        }
        EditorGUILayout.LabelField($"{HardAIDiagnostics.PolicyVersion} · seat {observation.Seat + 1} · turn {HardAIDiagnostics.Turn}");
        EditorGUILayout.LabelField($"Work {search.WorkCompleted}/{search.WorkBudget} · completed depth {search.Depth} · {HardAIDiagnostics.ElapsedSeconds:F2} s");
        EditorGUILayout.LabelField(search.Complete ? search.StopReason : "Searching; candidates are provisional");
        EditorGUILayout.LabelField($"Last executed: {HardAIDiagnostics.LastAction ?? "none"}");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < search.Candidates.Count; i++)
        {
            AICandidatePlan plan = search.Candidates[i];
            if (GUILayout.Toggle(selectedCandidate == i, $"#{i + 1} · score {plan.Score} · {plan.Actions.Length} actions", "Button")) selectedCandidate = i;
            EditorGUILayout.LabelField(plan.Evaluation.ToString(), EditorStyles.wordWrappedLabel);
            StringBuilder sequence = new StringBuilder();
            for (int step = 0; step < plan.Actions.Length; step++)
                sequence.Append(step + 1).Append(". ").Append(HardAIRuntime.Describe(observation, plan.Actions[step])).Append('\n');
            EditorGUILayout.LabelField(sequence.ToString(), EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Observed enemies and remembered cities", EditorStyles.boldLabel);
        foreach (AIUnitState unit in observation.Units)
            if (observation.IsHostileSeat(unit.Seat)) EditorGUILayout.LabelField($"Visible {unit.Type}, seat {unit.Seat + 1}, ({unit.X},{unit.Y})");
        foreach (AICityState city in observation.Cities)
            EditorGUILayout.LabelField($"City ({city.X},{city.Y}), seat {city.Seat + 1}: {(city.CurrentlyVisible ? "current" : "last observed")}");
        EditorGUILayout.EndScrollView();
    }

    private void DrawPlan(SceneView view)
    {
        IAIDecision search = HardAIDiagnostics.Search;
        AIObservation observation = HardAIDiagnostics.Observation;
        TurnManager manager = TurnManager.Instance;
        if (!EditorApplication.isPlaying || search == null || observation == null || manager == null || search.Candidates.Count == 0) return;
        AICandidatePlan plan = search.Candidates[Mathf.Clamp(selectedCandidate, 0, search.Candidates.Count - 1)];
        AITacticalState state = new AITacticalState(observation);
        for (int i = 0; i < plan.Actions.Length; i++)
        {
            AIAction action = plan.Actions[i];
            if (action.Kind == AIActionKind.EndTurn) continue;
            int x = action.Destination % observation.Width, y = action.Destination / observation.Width;
            if (!manager.gridManager.TryGetTile(x, y, out TileVisibility tile)) continue;
            Vector3 end = tile.transform.position;
            Handles.color = action.Kind == AIActionKind.Attack ? Color.red : Color.cyan;
            if (action.Kind != AIActionKind.Recruit && action.Actor < state.Units.Length)
            {
                AIUnitState unit = state.Units[action.Actor];
                if (manager.gridManager.TryGetTile(unit.X, unit.Y, out TileVisibility origin))
                    Handles.DrawAAPolyLine(3, origin.transform.position, end);
            }
            Handles.Label(end, $"{i + 1}: {action.Kind}");
            state = state.After(action);
        }
    }
}
