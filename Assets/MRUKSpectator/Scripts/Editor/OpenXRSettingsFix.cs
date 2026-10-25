using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MRUKSpectator.Editor
{
    /// <summary>
    /// Workaround for Unity 6000.6.0f1 + com.unity.xr.openxr@1.18.0 bug where
    /// OpenXRFeatureSetManager throws KeyNotFoundException: 'Android' while the
    /// active platform is Standalone (Windows), causing endless GUIClip errors
    /// and a blank OpenXR Project Settings tab.
    ///
    /// Running this from Assets/ ensures it survives any Library/ or PackageCache wipes.
    /// </summary>
    [InitializeOnLoad]
    public static class OpenXRSettingsFix
    {
        static OpenXRSettingsFix()
        {
            EditorApplication.delayCall += ApplyFix;
        }

        private static void ApplyFix()
        {
            try
            {
                var mgrType = Type.GetType("UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager, Unity.XR.OpenXR.Editor");
                if (mgrType == null) return;

                // 1. Force InitializeFeatureSets() to load all platform definitions
                var initMethod = mgrType.GetMethod("InitializeFeatureSets", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                initMethod?.Invoke(null, null);

                // 2. Ensure s_FeatureSetState contains BuildTargetGroup.Android so OnGUI doesn't throw KeyNotFoundException
                var stateField = mgrType.GetField("s_FeatureSetState", BindingFlags.Static | BindingFlags.NonPublic);
                if (stateField != null)
                {
                    var dict = stateField.GetValue(null) as System.Collections.IDictionary;
                    if (dict != null)
                    {
                        var androidGroup = BuildTargetGroup.Android;
                        if (!dict.Contains(androidGroup))
                        {
                            var featureInfoType = Type.GetType("UnityEditor.XR.OpenXR.Features.FeatureHelpersInternal+AllFeatureInfo, Unity.XR.OpenXR.Editor");
                            if (featureInfoType != null)
                            {
                                var defaultInfo = Activator.CreateInstance(featureInfoType);
                                dict[androidGroup] = defaultInfo;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenXRSettingsFix] Note: OpenXR fix init: {ex.Message}");
            }
        }
    }
}
