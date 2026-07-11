using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// Fluent, script-driven configuration for StoreKit — an alternative (or override layer) to
    /// the <see cref="StoreKitSettings"/> asset:
    /// <code>
    /// var settings = StoreKitSettings.Builder()
    ///     .AddConsumable("com.game.coins_100")
    ///     .AddNonConsumable("com.game.no_ads", appleId: "com.game.ios.no_ads")
    ///     .AddSubscription("com.game.vip")
    ///     .WithPopups(success: true, failure: true, cancelled: false)
    ///     .WithInitializationRetry(maxRetries: 5, baseDelaySeconds: 1f)
    ///     .Build();
    ///
    /// Store.Configure(settings);
    /// </code>
    /// Start from an existing asset with <c>StoreKitSettings.Builder(asset)</c> — the asset is
    /// cloned, so script overrides never modify it. Configure before <c>InitializeAsync</c>;
    /// catalog changes made after initialization only apply to later initializations.
    /// </summary>
    public sealed class StoreKitSettingsBuilder
    {
        private readonly StoreKitSettings _settings;

        /// <summary>Starts from empty settings.</summary>
        public StoreKitSettingsBuilder()
        {
            _settings = StoreKitSettings.Create();
        }

        /// <summary>Starts from a copy of <paramref name="baseSettings"/> (the original is never modified).</summary>
        public StoreKitSettingsBuilder(StoreKitSettings baseSettings)
        {
            if (baseSettings != null)
            {
                _settings = UnityEngine.Object.Instantiate(baseSettings);
                _settings.name = baseSettings.name + " (Runtime Copy)";
                _settings.hideFlags = HideFlags.DontSave;
            }
            else
            {
                _settings = StoreKitSettings.Create();
            }
        }

        // ---- Catalog ----

        /// <summary>Adds (or replaces, matching by id) a product definition.</summary>
        public StoreKitSettingsBuilder AddProduct(StoreProductDefinition definition)
        {
            _settings.AddProduct(definition);
            return this;
        }

        /// <summary>Adds (or replaces) a product by id and type.</summary>
        public StoreKitSettingsBuilder AddProduct(string id, StoreProductType type, string appleId = null,
            string googleId = null, string simulatedTitle = null, string simulatedPrice = "$0.99")
        {
            _settings.AddProduct(id, type, appleId, googleId, simulatedTitle, simulatedPrice);
            return this;
        }

        public StoreKitSettingsBuilder AddConsumable(string id, string appleId = null, string googleId = null,
            string simulatedTitle = null, string simulatedPrice = "$0.99")
            => AddProduct(id, StoreProductType.Consumable, appleId, googleId, simulatedTitle, simulatedPrice);

        public StoreKitSettingsBuilder AddNonConsumable(string id, string appleId = null, string googleId = null,
            string simulatedTitle = null, string simulatedPrice = "$0.99")
            => AddProduct(id, StoreProductType.NonConsumable, appleId, googleId, simulatedTitle, simulatedPrice);

        public StoreKitSettingsBuilder AddSubscription(string id, string appleId = null, string googleId = null,
            string simulatedTitle = null, string simulatedPrice = "$0.99")
            => AddProduct(id, StoreProductType.Subscription, appleId, googleId, simulatedTitle, simulatedPrice);

        /// <summary>Replaces the entire catalog.</summary>
        public StoreKitSettingsBuilder SetProducts(IEnumerable<StoreProductDefinition> definitions)
        {
            _settings.SetProducts(definitions);
            return this;
        }

        public StoreKitSettingsBuilder RemoveProduct(string id)
        {
            _settings.RemoveProduct(id);
            return this;
        }

        public StoreKitSettingsBuilder ClearProducts()
        {
            _settings.products.Clear();
            return this;
        }

        // ---- Popups ----

        /// <summary>Master switch for the built-in popups (a custom presenter bypasses this).</summary>
        public StoreKitSettingsBuilder EnableDefaultPopups(bool enabled = true)
        {
            _settings.enableDefaultPopups = enabled;
            return this;
        }

        /// <summary>Disables all built-in popups.</summary>
        public StoreKitSettingsBuilder DisablePopups() => EnableDefaultPopups(false);

        /// <summary>Toggles individual popup kinds; pass null to leave a kind unchanged.</summary>
        public StoreKitSettingsBuilder WithPopups(bool? success = null, bool? failure = null, bool? cancelled = null,
            bool? deferred = null, bool? restore = null)
        {
            if (success.HasValue) _settings.showSuccessPopup = success.Value;
            if (failure.HasValue) _settings.showFailurePopup = failure.Value;
            if (cancelled.HasValue) _settings.showCancelledPopup = cancelled.Value;
            if (deferred.HasValue) _settings.showDeferredPopup = deferred.Value;
            if (restore.HasValue) _settings.showRestorePopup = restore.Value;
            return this;
        }

        /// <summary>Edits the popup texts in place (e.g. for localization).</summary>
        public StoreKitSettingsBuilder WithPopupTexts(Action<StorePopupTexts> configure)
        {
            configure?.Invoke(_settings.popupTexts);
            return this;
        }

        // ---- Reliability ----

        /// <summary>Configures automatic initialization retry with exponential backoff.</summary>
        public StoreKitSettingsBuilder WithInitializationRetry(int maxRetries = 3, float baseDelaySeconds = 2f)
        {
            _settings.autoRetryInitialization = true;
            _settings.maxInitializationRetries = Mathf.Max(0, maxRetries);
            _settings.initializationRetryDelaySeconds = Mathf.Max(0f, baseDelaySeconds);
            return this;
        }

        public StoreKitSettingsBuilder WithoutInitializationRetry()
        {
            _settings.autoRetryInitialization = false;
            return this;
        }

        // ---- Editor / diagnostics ----

        /// <summary>Whether the Editor uses the simulated store (default true).</summary>
        public StoreKitSettingsBuilder UseSimulatedStoreInEditor(bool enabled = true)
        {
            _settings.useSimulatedStoreInEditor = enabled;
            return this;
        }

        /// <summary>Edits the Editor simulation options in place (delays, forced failures, confirmation, persistence).</summary>
        public StoreKitSettingsBuilder WithSimulation(Action<SimulatedStoreOptions> configure)
        {
            configure?.Invoke(_settings.simulation);
            return this;
        }

        public StoreKitSettingsBuilder WithVerboseLogging(bool enabled = true)
        {
            _settings.verboseLogging = enabled;
            return this;
        }

        /// <summary>Returns the configured settings instance.</summary>
        public StoreKitSettings Build() => _settings;
    }
}
