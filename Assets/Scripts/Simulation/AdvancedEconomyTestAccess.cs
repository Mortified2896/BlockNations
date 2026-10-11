using System.Runtime.CompilerServices;

// The existing Editor fixtures live in Unity's default Editor assembly. Keep economy
// state setters internal while allowing shortage/casualty scenarios to be configured.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
