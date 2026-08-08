using UnityEditor;
using UnityEditor.SceneManagement;

// EditorSceneManager.playModeStartScene is a purely in-memory Editor setting - Unity never writes
// it to any ProjectSettings/UserSettings/Library file, so it silently resets to null every time
// the Editor process restarts (confirmed: grepping the whole project for it after a restart found
// zero references anywhere on disk). Without it, pressing Play while a sub-scene like Dungeon.unity
// happens to be the only one open runs that scene directly, with no Bootstrap/MapManager in the
// picture at all - every MultiAreaMap-derived controller's Update() then NREs every single frame
// trying to reach MapManager.Instance.Player. [InitializeOnLoad] re-applies the setting on every
// domain reload (Editor startup AND every script recompile), so it can never silently drop out
// again the way a one-off script-console assignment did.
[InitializeOnLoad]
static class PlayModeStartSceneSetup
{
    static PlayModeStartSceneSetup()
    {
        var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Bootstrap.unity");
        if (bootstrap != null)
            EditorSceneManager.playModeStartScene = bootstrap;
    }
}
