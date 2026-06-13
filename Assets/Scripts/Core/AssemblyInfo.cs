using System.Runtime.CompilerServices;

// Allow the PlayMode test assembly to call internal test seams (e.g. TurnManager.TryExecuteTacticalCityCapturePlan)
// without expanding the public API surface of Assembly-CSharp.
[assembly: InternalsVisibleTo("BlockNations.Tests.PlayMode")]
