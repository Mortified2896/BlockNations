using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class IosPbpBackgroundNotificationPostProcess
{
    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
        {
            return;
        }

        string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        if (File.Exists(projectPath))
        {
            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

            string unityFrameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(unityFrameworkTargetGuid, "BackgroundTasks.framework", false);
            project.AddFrameworkToProject(unityFrameworkTargetGuid, "UserNotifications.framework", false);
            project.WriteToFile(projectPath);
        }

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        if (!File.Exists(plistPath))
        {
            return;
        }

        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        string bundleIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS);
        if (string.IsNullOrWhiteSpace(bundleIdentifier))
        {
            bundleIdentifier = "com.blocknations.app";
        }

        string taskIdentifier = bundleIdentifier + ".pbp-refresh";

        PlistElementArray backgroundModes = GetOrCreateArray(plist.root, "UIBackgroundModes");
        AddUniqueString(backgroundModes, "fetch");

        PlistElementArray permittedIdentifiers = GetOrCreateArray(plist.root, "BGTaskSchedulerPermittedIdentifiers");
        AddUniqueString(permittedIdentifiers, taskIdentifier);

        plist.WriteToFile(plistPath);
    }

    private static PlistElementArray GetOrCreateArray(PlistElementDict root, string key)
    {
        if (root.values.TryGetValue(key, out PlistElement existing) && existing is PlistElementArray array)
        {
            return array;
        }

        return root.CreateArray(key);
    }

    private static void AddUniqueString(PlistElementArray array, string value)
    {
        for (int i = 0; i < array.values.Count; i++)
        {
            if (array.values[i] is PlistElementString existing &&
                string.Equals(existing.value, value, System.StringComparison.Ordinal))
            {
                return;
            }
        }

        array.AddString(value);
    }
}
