using System.Linq;
using Antura.Profile;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Antura.Core
{
    public class ApplicationConfig : ScriptableObject
    {
#if UNITY_EDITOR

        public static ApplicationConfig FindMainConfig()
        {
            var configPath = $"Assets/_config/ApplicationConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ApplicationConfig>(configPath);
            if (config == null)
            {
                Debug.LogError($"Could not find ApplicationConfig at path '{configPath}'");
                return null;
            }
            return config;
        }

#endif

        public static ApplicationConfig I => AppManager.I.ApplicationConfig;

        public AppEditionConfig LoadedAppEdition;
        public ContentEditionConfig ContentEdition
        {
            get
            {
                var loadedContentID = AppManager.I.AppSettings.ContentID;
                if (loadedContentID == LearningContentID.None)
                {
                    Debug.Log("No saved content ID. Choosing the first available.");
                    AppManager.I.AppSettingsManager.SetLearningContentID(LoadedAppEdition.ContentEditions[0].ContentID);
                    return LoadedAppEdition.ContentEditions[0];
                }
                var config = LoadedAppEdition.ContentEditions.FirstOrDefault(x => x.ContentID == loadedContentID);
                if (config == null)
                {
                    Debug.Log($"No content with ID {loadedContentID} could be found. Reverting to the first available.");
                    AppManager.I.AppSettingsManager.SetLearningContentID(LoadedAppEdition.ContentEditions[0].ContentID);
                    return LoadedAppEdition.ContentEditions[0];
                }
                return config;
            }
        }

        [Header("Settings")]

        /// <summary>
        /// Version of the application. Displayed in the Home scene.
        /// Major.Minor.Patch.Build
        /// </summary>
        [Tooltip("Major.Minor.Patch.Build")]
        public string AppVersion = "0.0.0.0";

        [Tooltip("add compilation symbol: MODULE_NOTIFICATIONS")]
        public bool EnableNotifications;

        public bool OnlineAnalyticsEnabled = false;

        public TextAsset CreditsText;


        public string GetAppVersionString()
        {
            var VersionArray = AppVersion.Split('.');
            string v = string.Format("{0}.{1}.{2} ({3})", VersionArray[0], VersionArray[1], VersionArray[2], VersionArray[3]);
            return v;
        }
    }
}
