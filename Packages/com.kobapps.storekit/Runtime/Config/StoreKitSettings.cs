using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// Configuration for StoreKit: the product catalog, popup behavior, initialization retry
    /// policy, and Editor simulation options. Create via
    /// <c>Assets &gt; Create &gt; StoreKit &gt; Settings</c> (or <c>Tools &gt; StoreKit &gt; Create Settings Asset</c>)
    /// and place it in a <c>Resources</c> folder named <c>StoreKitSettings</c> so it is picked up automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "StoreKit/Settings", fileName = DefaultAssetName)]
    public sealed class StoreKitSettings : ScriptableObject
    {
        public const string DefaultAssetName = "StoreKitSettings";

        [Header("Product Catalog")]
        [Tooltip("All products the store should know about.")]
        public List<StoreProductDefinition> products = new List<StoreProductDefinition>();

        [Header("Default Popups")]
        [Tooltip("Master switch for StoreKit's built-in device popups. Assigning a custom IStorePopupPresenter bypasses this switch.")]
        public bool enableDefaultPopups = true;
        public bool showSuccessPopup = true;
        public bool showFailurePopup = true;
        [Tooltip("Show a popup when the user cancels a purchase. Off by default — cancellation is usually silent.")]
        public bool showCancelledPopup = false;
        [Tooltip("Show a popup when a purchase is deferred (e.g. Apple Ask to Buy).")]
        public bool showDeferredPopup = true;
        public bool showRestorePopup = true;
        public StorePopupTexts popupTexts = new StorePopupTexts();

        [Header("Reliability")]
        [Tooltip("Automatically retry initialization with exponential backoff on transient failures (connectivity, services).")]
        public bool autoRetryInitialization = true;
        [Range(0, 10)]
        public int maxInitializationRetries = 3;
        [Tooltip("Base delay for the first retry; doubles on each subsequent attempt.")]
        public float initializationRetryDelaySeconds = 2f;

        [Header("Editor")]
        [Tooltip("Use the simulated store when running in the Editor (works in Edit Mode and Play Mode, no device or store account needed).")]
        public bool useSimulatedStoreInEditor = true;
        public SimulatedStoreOptions simulation = new SimulatedStoreOptions();

        [Header("Diagnostics")]
        public bool verboseLogging = false;

        /// <summary>Loads the default settings asset from any Resources folder (<c>Resources/StoreKitSettings</c>).</summary>
        public static StoreKitSettings LoadDefault() => Resources.Load<StoreKitSettings>(DefaultAssetName);

        /// <summary>
        /// Creates an empty settings instance for pure script-driven configuration
        /// (not saved as an asset). Prefer <see cref="Builder()"/> for a fluent API.
        /// </summary>
        public static StoreKitSettings Create()
        {
            var settings = CreateInstance<StoreKitSettings>();
            settings.name = "StoreKitSettings (Runtime)";
            settings.hideFlags = HideFlags.DontSave;
            return settings;
        }

        /// <summary>Starts a fluent builder for script-driven configuration. See <see cref="StoreKitSettingsBuilder"/>.</summary>
        public static StoreKitSettingsBuilder Builder() => new StoreKitSettingsBuilder();

        /// <summary>
        /// Starts a fluent builder from a copy of an existing settings asset — script overrides
        /// never modify the original asset.
        /// </summary>
        public static StoreKitSettingsBuilder Builder(StoreKitSettings baseSettings) => new StoreKitSettingsBuilder(baseSettings);

        /// <summary>Returns the definition for a product id, or null.</summary>
        public StoreProductDefinition FindProduct(string productId)
        {
            for (int i = 0; i < products.Count; i++)
            {
                if (products[i] != null && products[i].id == productId)
                {
                    return products[i];
                }
            }

            return null;
        }

        // ---- Script API for the catalog. Changes only take effect for initializations that
        // ---- happen afterwards — configure before InitializeAsync.

        /// <summary>Adds (or replaces, matching by id) a product definition.</summary>
        public StoreKitSettings AddProduct(StoreProductDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.id))
            {
                Debug.LogWarning("[StoreKit] Ignoring product definition with an empty id.");
                return this;
            }

            RemoveProduct(definition.id);
            products.Add(definition);
            return this;
        }

        /// <summary>Adds (or replaces) a product by id and type.</summary>
        public StoreKitSettings AddProduct(string id, StoreProductType type, string appleId = null, string googleId = null,
            string simulatedTitle = null, string simulatedPrice = "$0.99")
            => AddProduct(new StoreProductDefinition(id, type, appleId, googleId, simulatedTitle, simulatedPrice));

        /// <summary>Removes a product definition by id. Returns true when something was removed.</summary>
        public bool RemoveProduct(string productId)
        {
            var removed = false;
            for (int i = products.Count - 1; i >= 0; i--)
            {
                if (products[i] != null && products[i].id == productId)
                {
                    products.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        /// <summary>Replaces the entire catalog.</summary>
        public StoreKitSettings SetProducts(IEnumerable<StoreProductDefinition> definitions)
        {
            products.Clear();
            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    AddProduct(definition);
                }
            }

            return this;
        }
    }

    /// <summary>A single product entry in the catalog.</summary>
    [Serializable]
    public sealed class StoreProductDefinition
    {
        public StoreProductDefinition()
        {
        }

        /// <summary>Script-friendly constructor.</summary>
        public StoreProductDefinition(string id, StoreProductType type, string appleId = null, string googleId = null,
            string simulatedTitle = null, string simulatedPrice = "$0.99")
        {
            this.id = id;
            this.type = type;
            this.appleId = appleId;
            this.googleId = googleId;
            this.simulatedTitle = simulatedTitle;
            this.simulatedPrice = simulatedPrice;
        }

        [Tooltip("Cross-store product id. Used as-is unless a store-specific override is set below.")]
        public string id;

        public StoreProductType type = StoreProductType.Consumable;

        [Header("Store-Specific Id Overrides (optional)")]
        [Tooltip("Apple App Store product id, if it differs from the cross-store id.")]
        public string appleId;
        [Tooltip("Google Play product id, if it differs from the cross-store id.")]
        public string googleId;

        [Header("Editor Simulation")]
        [Tooltip("Title shown by the simulated store in the Editor.")]
        public string simulatedTitle;
        [Tooltip("Price shown by the simulated store in the Editor.")]
        public string simulatedPrice = "$0.99";
    }

    /// <summary>Behavior of the simulated store used in the Editor (and when Unity IAP is not installed).</summary>
    [Serializable]
    public sealed class SimulatedStoreOptions
    {
        [Tooltip("Ask for confirmation via popup before completing a simulated purchase.")]
        public bool askForConfirmation = true;
        [Min(0f)]
        public float initializeDelaySeconds = 0.5f;
        [Min(0f)]
        public float purchaseDelaySeconds = 0.5f;
        [Tooltip("Force initialization to fail, to test failure flows.")]
        public bool failInitialization = false;
        [Tooltip("Force purchases to fail with the failure reason below, to test failure flows.")]
        public bool failPurchases = false;
        public PurchaseFailure purchaseFailure = PurchaseFailure.UserCancelled;
        [Tooltip("Persist simulated ownership of non-consumables/subscriptions across sessions (PlayerPrefs).")]
        public bool persistOwnership = true;
    }

    /// <summary>Texts used by the default popups. "{0}"/"{1}" placeholders are described per field.</summary>
    [Serializable]
    public sealed class StorePopupTexts
    {
        public string purchaseSuccessTitle = "Purchase Complete";
        [Tooltip("{0} = product title")]
        public string purchaseSuccessMessage = "{0} was purchased successfully.";
        public string purchaseFailedTitle = "Purchase Failed";
        [Tooltip("{0} = friendly failure reason")]
        public string purchaseFailedMessage = "{0}";
        public string purchaseCancelledTitle = "Purchase Cancelled";
        public string purchaseCancelledMessage = "The purchase was cancelled.";
        public string purchaseDeferredTitle = "Purchase Pending";
        public string purchaseDeferredMessage = "Your purchase is awaiting approval.";
        public string restoreSuccessTitle = "Restore Complete";
        [Tooltip("{0} = number of restored purchases")]
        public string restoreSuccessMessage = "{0} purchase(s) restored.";
        public string restoreFailedTitle = "Restore Failed";
        [Tooltip("{0} = failure reason")]
        public string restoreFailedMessage = "{0}";
        public string confirmButton = "OK";
        public string simulatedConfirmTitle = "Simulated Purchase";
        [Tooltip("{0} = product title, {1} = price")]
        public string simulatedConfirmMessage = "Buy {0} for {1}?";
        public string buyButton = "Buy";
        public string cancelButton = "Cancel";
    }
}
