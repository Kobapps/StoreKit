using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StoreKit.Editor
{
    /// <summary>Editor conveniences: create the settings asset in the right place, reset simulated purchases.</summary>
    public static class StoreKitEditorTools
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string SettingsAssetPath = ResourcesFolder + "/" + StoreKitSettings.DefaultAssetName + ".asset";

        [MenuItem("Tools/StoreKit/Create Settings Asset")]
        public static void CreateSettingsAsset()
        {
            var existing = StoreKitSettings.LoadDefault();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[StoreKit] Settings asset already exists.", existing);
                return;
            }

            if (!Directory.Exists(ResourcesFolder))
            {
                Directory.CreateDirectory(ResourcesFolder);
            }

            var settings = ScriptableObject.CreateInstance<StoreKitSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            Debug.Log($"[StoreKit] Created settings asset at {SettingsAssetPath}. Add your products to it.", settings);
        }

        [MenuItem("Tools/StoreKit/Clear Simulated Purchases")]
        public static void ClearSimulatedPurchases()
        {
            var settings = StoreKitSettings.LoadDefault();
            if (settings == null)
            {
                Debug.LogWarning("[StoreKit] No default StoreKitSettings asset found in Resources.");
                return;
            }

            SimulatedStoreGateway.ClearPersistedOwnership(settings);
            Debug.Log("[StoreKit] Cleared simulated ownership for all configured products.");
        }
    }

    /// <summary>Adds catalog validation warnings on top of the default settings inspector.</summary>
    [CustomEditor(typeof(StoreKitSettings))]
    public sealed class StoreKitSettingsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (StoreKitSettings)target;

            foreach (var issue in Validate(settings))
            {
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }

#if !STOREKIT_HAS_IAP
            EditorGUILayout.HelpBox("com.unity.purchasing is not installed — StoreKit will always use the simulated store. Install Unity IAP for device builds.", MessageType.Info);
#endif

            DrawDefaultInspector();
        }

        private static IEnumerable<string> Validate(StoreKitSettings settings)
        {
            if (settings.products == null || settings.products.Count == 0)
            {
                yield return "No products configured. Add at least one product to the catalog.";
                yield break;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < settings.products.Count; i++)
            {
                var product = settings.products[i];
                if (product == null || string.IsNullOrWhiteSpace(product.id))
                {
                    yield return $"Product #{i} has an empty id.";
                    continue;
                }

                if (!seen.Add(product.id))
                {
                    yield return $"Duplicate product id: '{product.id}'.";
                }
            }
        }
    }
}
