using System;
using System.Text;
using BlockNations.AI;
using UnityEngine;
using UnityEngine.Networking;

// Local development experiment. Authentication stays in the already signed-in CLI.
// Production/mobile/Web builds do not contact this bridge.
public sealed class LocalLunaPlaytestPolicy : IAIActionPolicy
{
    public const string PolicyVersion = "codex-single-action-v1";
    public const string Endpoint = "http://127.0.0.1:8769";
    private readonly string model = LocalAIPlaytestSettings.Model;
    private readonly string effort = LocalAIPlaytestSettings.ReasoningEffort;
    public string Version => $"{PolicyVersion}:{model}:{effort}";

    public IAIDecision BeginDecision(AIObservation observation, int workBudget)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (observation.LegalActions.Length == 1 && observation.LegalActions[0].Kind == AIActionKind.EndTurn)
            return new AIExternalActionDecision(observation, new AICompletedActionRequest(new AIExternalActionResponse {
                HasAction = true, ActionId = 0, Model = "Rules: forced EndTurn", ReasoningEffort = "none",
                CallsRemaining = -1, Summary = "EndTurn is the only legal action; no model request was needed." }), model, effort);
        return new AIExternalActionDecision(observation, new LocalRequest(observation, model, effort), model, effort);
#else
        throw new InvalidOperationException("Luna is available only in a local development playtest.");
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Serializable]
    private sealed class RequestBody
    {
        public int ObservationSchemaVersion = AIObservation.SchemaVersion;
        public string RulesVersion = AIActionRules.RulesVersion;
        public string Model, ReasoningEffort;
        public AIObservation Observation;
    }

    private sealed class LocalRequest : IAIActionRequest
    {
        private UnityWebRequest web;
        public bool Complete { get; private set; }
        public AIExternalActionResponse Response { get; private set; }
        public LocalRequest(AIObservation observation, string model, string effort)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RequestBody {
                Observation = observation, Model = model, ReasoningEffort = effort }));
            web = new UnityWebRequest(Endpoint + "/decision", UnityWebRequest.kHttpVerbPOST) {
                uploadHandler = new UploadHandlerRaw(body), downloadHandler = new DownloadHandlerBuffer(), timeout = 80 };
            web.SetRequestHeader("Content-Type", "application/json");
            web.SendWebRequest();
        }

        public void Poll()
        {
            if (Complete || !web.isDone) return;
            try
            {
                if (!string.IsNullOrEmpty(web.downloadHandler.text))
                    Response = JsonUtility.FromJson<AIExternalActionResponse>(web.downloadHandler.text);
                if (Response == null || (web.result != UnityWebRequest.Result.Success && string.IsNullOrEmpty(Response.Error)))
                    Response = new AIExternalActionResponse { Error = "Local Luna bridge unavailable or timed out. Start the bridge and retry the playtest." };
            }
            catch (Exception)
            { Response = new AIExternalActionResponse { Error = "Local Luna bridge returned an unreadable response." }; }
            finally { Complete = true; web.Dispose(); web = null; }
        }

        public void Cancel()
        {
            if (Complete) return;
            web.Abort();
            web.Dispose();
            web = null;
            Complete = true;
            Response = new AIExternalActionResponse { Error = "Local Luna request cancelled." };
        }
    }
#endif
}
