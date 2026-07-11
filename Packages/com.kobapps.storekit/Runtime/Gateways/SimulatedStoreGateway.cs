using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// A fully simulated store for the Editor (Edit Mode and Play Mode) and for automated tests —
    /// no device, store account, or Unity IAP required. Behavior (delays, forced failures,
    /// confirmation prompt, ownership persistence) is driven by
    /// <see cref="StoreKitSettings.simulation"/>.
    /// </summary>
    public sealed class SimulatedStoreGateway : IStoreGateway
    {
        private const string PrefsPrefix = "StoreKit.Simulated.Owned.";

        private readonly List<StoreProduct> _products = new List<StoreProduct>();
        private readonly HashSet<string> _sessionOwnership = new HashSet<string>();

        private StoreKitSettings _settings;
        private SimulatedStoreOptions Options => _settings != null ? _settings.simulation : new SimulatedStoreOptions();

        private bool _purchaseInProgress;

        /// <summary>
        /// Optional confirmation prompt shown before completing a simulated purchase
        /// (wired to the popup presenter by <see cref="StoreService"/>). Return false to cancel.
        /// </summary>
        public Func<StoreProduct, CancellationToken, UniTask<bool>> ConfirmationPrompt { get; set; }

        public bool IsInitialized { get; private set; }

        public IReadOnlyList<StoreProduct> Products => _products;

        public IPurchaseValidator Validator { get; set; }

        public event Action<PurchaseResult> OutOfBandPurchase;
        public event Action<StoreProduct> PurchaseDeferred
        {
            add { }
            remove { }
        }

        public async UniTask<StoreInitializeResult> InitializeAsync(StoreKitSettings settings, CancellationToken cancellationToken = default)
        {
            if (IsInitialized)
            {
                return StoreInitializeResult.Succeeded(StoreInitializeStatus.AlreadyInitialized);
            }

            _settings = settings;
            await SimulateDelay(Options.initializeDelaySeconds, cancellationToken);

            if (Options.failInitialization)
            {
                return StoreInitializeResult.Failed(StoreInitializeStatus.PurchasingUnavailable, "Simulated initialization failure (StoreKitSettings > Simulation).");
            }

            _products.Clear();
            foreach (var definition in settings.products)
            {
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    continue;
                }

                var product = new StoreProduct(definition.id, definition.type)
                {
                    Title = string.IsNullOrEmpty(definition.simulatedTitle) ? definition.id : definition.simulatedTitle,
                    Description = "Simulated product",
                    PriceString = string.IsNullOrEmpty(definition.simulatedPrice) ? "$0.99" : definition.simulatedPrice,
                    Price = 0.99m,
                    IsoCurrencyCode = "USD",
                    AvailableToPurchase = true,
                };

                if (product.Type != StoreProductType.Consumable && IsOwnedPersisted(product.Id))
                {
                    MarkOwned(product, persist: false);
                }

                _products.Add(product);
            }

            IsInitialized = true;
            return StoreInitializeResult.Succeeded(StoreInitializeStatus.Success, "Simulated store initialized.");
        }

        public async UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                return PurchaseResult.Failed(productId, null, PurchaseFailure.StoreNotInitialized);
            }

            var product = FindProduct(productId);
            if (product == null)
            {
                return PurchaseResult.Failed(productId, null, PurchaseFailure.ProductNotFound);
            }

            if (!product.AvailableToPurchase)
            {
                return PurchaseResult.Failed(productId, product, PurchaseFailure.ProductUnavailable);
            }

            if (_purchaseInProgress)
            {
                return PurchaseResult.Failed(productId, product, PurchaseFailure.PurchaseInProgress);
            }

            if (product.Type != StoreProductType.Consumable && product.HasReceipt)
            {
                return PurchaseResult.Failed(productId, product, PurchaseFailure.DuplicateTransaction);
            }

            _purchaseInProgress = true;
            try
            {
                if (Options.askForConfirmation && ConfirmationPrompt != null)
                {
                    var confirmed = await ConfirmationPrompt(product, cancellationToken);
                    if (!confirmed)
                    {
                        return PurchaseResult.Failed(productId, product, PurchaseFailure.UserCancelled);
                    }
                }

                await SimulateDelay(Options.purchaseDelaySeconds, cancellationToken);

                if (Options.failPurchases)
                {
                    var failure = Options.purchaseFailure == PurchaseFailure.None ? PurchaseFailure.Unknown : Options.purchaseFailure;
                    return PurchaseResult.Failed(productId, product, failure, "Simulated purchase failure (StoreKitSettings > Simulation).");
                }

                var transactionId = Guid.NewGuid().ToString("N");
                var receipt = BuildSimulatedReceipt(product, transactionId);

                if (Validator != null)
                {
                    bool valid;
                    try
                    {
                        valid = await Validator.ValidateAsync(product, receipt, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        StoreKitLog.Error($"Purchase validator threw: {e}");
                        valid = false;
                    }

                    if (!valid)
                    {
                        return PurchaseResult.Failed(productId, product, PurchaseFailure.ValidationFailed);
                    }
                }

                if (product.Type != StoreProductType.Consumable)
                {
                    MarkOwned(product, persist: true);
                    product.Receipt = receipt;
                }

                return PurchaseResult.Succeeded(product, transactionId, receipt);
            }
            finally
            {
                _purchaseInProgress = false;
            }
        }

        public async UniTask<RestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                return RestoreResult.Failed(StoreKitMessages.Friendly(PurchaseFailure.StoreNotInitialized));
            }

            await SimulateDelay(Options.purchaseDelaySeconds, cancellationToken);

            var restored = 0;
            foreach (var product in _products)
            {
                if (product.Type == StoreProductType.Consumable || !product.HasReceipt)
                {
                    continue;
                }

                restored++;
                var transactionId = Guid.NewGuid().ToString("N");
                var result = new PurchaseResult(true, product.Id, product, PurchaseFailure.None,
                    string.Empty, transactionId, BuildSimulatedReceipt(product, transactionId), isRestored: true);
                OutOfBandPurchase?.Invoke(result);
            }

            return RestoreResult.Succeeded(restored, "Simulated restore complete.");
        }

        /// <summary>Clears persisted simulated ownership for the given settings' products.</summary>
        public static void ClearPersistedOwnership(StoreKitSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            foreach (var definition in settings.products)
            {
                if (definition != null && !string.IsNullOrEmpty(definition.id))
                {
                    PlayerPrefs.DeleteKey(PrefsPrefix + definition.id);
                }
            }

            PlayerPrefs.Save();
        }

        private StoreProduct FindProduct(string productId)
        {
            for (int i = 0; i < _products.Count; i++)
            {
                if (_products[i].Id == productId)
                {
                    return _products[i];
                }
            }

            return null;
        }

        private void MarkOwned(StoreProduct product, bool persist)
        {
            product.HasReceipt = true;
            _sessionOwnership.Add(product.Id);
            if (persist && Options.persistOwnership)
            {
                PlayerPrefs.SetInt(PrefsPrefix + product.Id, 1);
                PlayerPrefs.Save();
            }
        }

        private bool IsOwnedPersisted(string productId)
        {
            if (_sessionOwnership.Contains(productId))
            {
                return true;
            }

            return Options.persistOwnership && PlayerPrefs.GetInt(PrefsPrefix + productId, 0) == 1;
        }

        private static string BuildSimulatedReceipt(StoreProduct product, string transactionId)
            => $"{{\"Store\":\"Simulated\",\"TransactionID\":\"{transactionId}\",\"ProductId\":\"{product.Id}\"}}";

        private static async UniTask SimulateDelay(float seconds, CancellationToken cancellationToken)
        {
            if (seconds <= 0f)
            {
                await UniTask.Yield(cancellationToken);
                return;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
        }
    }
}
