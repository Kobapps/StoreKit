#if STOREKIT_HAS_IAP
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace StoreKit
{
    /// <summary>
    /// Production gateway backed by Unity IAP (com.unity.purchasing). Handles Unity Gaming
    /// Services bootstrap, product configuration (with per-store id overrides), async purchase
    /// completion, receipt validation via <see cref="IPurchaseValidator"/> (transactions stay
    /// pending until validation finishes), Apple restore, and Apple "Ask to Buy" deferral.
    /// </summary>
    public sealed class UnityIapGateway : IStoreGateway, IDetailedStoreListener
    {
        private readonly List<StoreProduct> _products = new List<StoreProduct>();
        private readonly Dictionary<string, StoreProduct> _productsById = new Dictionary<string, StoreProduct>();

        private IStoreController _controller;
        private IExtensionProvider _extensions;

        private UniTaskCompletionSource<StoreInitializeResult> _initTcs;
        private UniTaskCompletionSource<PurchaseResult> _purchaseTcs;
        private string _pendingProductId;

        private bool _restoreInProgress;
        private int _restoredCount;

        public bool IsInitialized => _controller != null;

        public IReadOnlyList<StoreProduct> Products => _products;

        public IPurchaseValidator Validator { get; set; }

        public event Action<PurchaseResult> OutOfBandPurchase;
        public event Action<StoreProduct> PurchaseDeferred;

        public async UniTask<StoreInitializeResult> InitializeAsync(StoreKitSettings settings, CancellationToken cancellationToken = default)
        {
            if (IsInitialized)
            {
                return StoreInitializeResult.Succeeded(StoreInitializeStatus.AlreadyInitialized);
            }

            if (_initTcs != null)
            {
                return await _initTcs.Task.AttachExternalCancellation(cancellationToken);
            }

            // Unity IAP requires Unity Gaming Services to be initialized first.
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync().AsUniTask().AttachExternalCancellation(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return StoreInitializeResult.Failed(StoreInitializeStatus.Cancelled, "Initialization was cancelled.");
            }
            catch (Exception e)
            {
                return StoreInitializeResult.Failed(StoreInitializeStatus.ServicesInitializationFailed,
                    $"Unity Gaming Services initialization failed: {e.Message}");
            }

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            var configured = 0;
            foreach (var definition in settings.products)
            {
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    continue;
                }

                var type = MapType(definition.type);
                if (string.IsNullOrEmpty(definition.appleId) && string.IsNullOrEmpty(definition.googleId))
                {
                    builder.AddProduct(definition.id, type);
                }
                else
                {
                    var ids = new IDs();
                    if (!string.IsNullOrEmpty(definition.appleId))
                    {
                        ids.Add(definition.appleId, AppleAppStore.Name, MacAppStore.Name);
                    }

                    if (!string.IsNullOrEmpty(definition.googleId))
                    {
                        ids.Add(definition.googleId, GooglePlay.Name);
                    }

                    builder.AddProduct(definition.id, type, ids);
                }

                configured++;
            }

            if (configured == 0)
            {
                return StoreInitializeResult.Failed(StoreInitializeStatus.ConfigurationError, "No valid products configured.");
            }

            _initTcs = new UniTaskCompletionSource<StoreInitializeResult>();
            UnityPurchasing.Initialize(this, builder);

            var result = await _initTcs.Task.AttachExternalCancellation(cancellationToken);
            if (!result.Success)
            {
                _initTcs = null; // allow retry
            }

            return result;
        }

        public async UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                return PurchaseResult.Failed(productId, null, PurchaseFailure.StoreNotInitialized);
            }

            if (_purchaseTcs != null)
            {
                return PurchaseResult.Failed(productId, GetKnownProduct(productId), PurchaseFailure.PurchaseInProgress);
            }

            var product = _controller.products.WithID(productId);
            if (product == null)
            {
                return PurchaseResult.Failed(productId, null, PurchaseFailure.ProductNotFound);
            }

            if (!product.availableToPurchase)
            {
                return PurchaseResult.Failed(productId, GetKnownProduct(productId), PurchaseFailure.ProductUnavailable);
            }

            _purchaseTcs = new UniTaskCompletionSource<PurchaseResult>();
            _pendingProductId = productId;

            try
            {
                _controller.InitiatePurchase(product);
                return await _purchaseTcs.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The store transaction may still complete later; it will surface as out-of-band.
                _purchaseTcs = null;
                _pendingProductId = null;
                throw;
            }
        }

        public async UniTask<RestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                return RestoreResult.Failed(StoreKitMessages.Friendly(PurchaseFailure.StoreNotInitialized));
            }

            if (!IsApplePlatform())
            {
                // Google Play (and most other stores) restore automatically during initialization.
                return RestoreResult.Succeeded(0, "This platform restores purchases automatically.");
            }

            IAppleExtensions apple;
            try
            {
                apple = _extensions.GetExtension<IAppleExtensions>();
            }
            catch (Exception e)
            {
                return RestoreResult.Failed($"Apple extensions unavailable: {e.Message}");
            }

            if (apple == null)
            {
                return RestoreResult.Failed("Apple extensions unavailable.");
            }

            _restoreInProgress = true;
            _restoredCount = 0;
            var tcs = new UniTaskCompletionSource<(bool success, string error)>();
            apple.RestoreTransactions((success, error) => tcs.TrySetResult((success, error)));

            try
            {
                var (success, error) = await tcs.Task.AttachExternalCancellation(cancellationToken);

                // Give straggler transactions a moment to flow through ProcessPurchase.
                await UniTask.Delay(TimeSpan.FromSeconds(0.5), DelayType.Realtime, PlayerLoopTiming.Update, CancellationToken.None);

                return success
                    ? RestoreResult.Succeeded(_restoredCount)
                    : RestoreResult.Failed(string.IsNullOrEmpty(error) ? "The store could not restore purchases." : error);
            }
            finally
            {
                _restoreInProgress = false;
            }
        }

        // ---- IDetailedStoreListener ----

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;

            _products.Clear();
            _productsById.Clear();
            foreach (var product in controller.products.all)
            {
                var snapshot = new StoreProduct(product.definition.id, MapType(product.definition.type));
                UpdateSnapshot(snapshot, product);
                _products.Add(snapshot);
                _productsById[snapshot.Id] = snapshot;
            }

            try
            {
                var apple = extensions.GetExtension<IAppleExtensions>();
                apple?.RegisterPurchaseDeferredListener(OnApplePurchaseDeferred);
            }
            catch (Exception e)
            {
                StoreKitLog.Info($"Apple extensions not available ({e.Message}).");
            }

            _initTcs?.TrySetResult(StoreInitializeResult.Succeeded());
        }

        public void OnInitializeFailed(InitializationFailureReason error) => OnInitializeFailed(error, null);

        public void OnInitializeFailed(InitializationFailureReason error, string message)
        {
            var status = error switch
            {
                InitializationFailureReason.PurchasingUnavailable => StoreInitializeStatus.PurchasingUnavailable,
                InitializationFailureReason.NoProductsAvailable => StoreInitializeStatus.NoProductsAvailable,
                InitializationFailureReason.AppNotKnown => StoreInitializeStatus.AppNotKnown,
                _ => StoreInitializeStatus.Unknown,
            };

            _initTcs?.TrySetResult(StoreInitializeResult.Failed(status, message ?? error.ToString()));
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent)
        {
            // Keep the transaction pending while we (optionally) validate the receipt, then
            // confirm and report. This guarantees a purchase is never lost mid-validation.
            CompletePurchaseAsync(purchaseEvent.purchasedProduct).Forget();
            return PurchaseProcessingResult.Pending;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            var failure = MapFailure(failureDescription.reason);
            HandlePurchaseFailed(product, failure, failureDescription.message);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            HandlePurchaseFailed(product, MapFailure(failureReason), null);
        }

        // ---- internals ----

        private async UniTaskVoid CompletePurchaseAsync(Product product)
        {
            try
            {
                var snapshot = GetOrCreateSnapshot(product);
                UpdateSnapshot(snapshot, product);

                var valid = true;
                if (Validator != null)
                {
                    try
                    {
                        valid = await Validator.ValidateAsync(snapshot, product.receipt, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        StoreKitLog.Error($"Purchase validator threw: {e}");
                        valid = false;
                    }
                }

                // Confirm regardless of validation outcome so an invalid transaction is not
                // re-delivered forever; validation failure is reported to the game instead.
                _controller.ConfirmPendingPurchase(product);

                var result = valid
                    ? new PurchaseResult(true, snapshot.Id, snapshot, PurchaseFailure.None, string.Empty,
                        product.transactionID, product.receipt, _restoreInProgress)
                    : PurchaseResult.Failed(snapshot.Id, snapshot, PurchaseFailure.ValidationFailed);

                if (valid && _restoreInProgress)
                {
                    _restoredCount++;
                }

                if (!TryResolvePendingPurchase(snapshot.Id, result) && valid)
                {
                    OutOfBandPurchase?.Invoke(result);
                }
            }
            catch (Exception e)
            {
                StoreKitLog.Error($"Unexpected error while completing purchase: {e}");
                TryResolvePendingPurchase(product?.definition?.id,
                    PurchaseResult.Failed(product?.definition?.id, null, PurchaseFailure.Unknown, e.Message));
            }
        }

        private void HandlePurchaseFailed(Product product, PurchaseFailure failure, string message)
        {
            var productId = product?.definition?.id;
            var result = PurchaseResult.Failed(productId, GetKnownProduct(productId), failure, message);
            if (!TryResolvePendingPurchase(productId, result))
            {
                StoreKitLog.Warn($"Out-of-band purchase failure: {result}");
            }
        }

        private bool TryResolvePendingPurchase(string productId, PurchaseResult result)
        {
            if (_purchaseTcs == null || _pendingProductId == null || _pendingProductId != productId)
            {
                return false;
            }

            var tcs = _purchaseTcs;
            _purchaseTcs = null;
            _pendingProductId = null;
            return tcs.TrySetResult(result);
        }

        private void OnApplePurchaseDeferred(Product product)
        {
            var productId = product?.definition?.id;
            var snapshot = GetKnownProduct(productId);
            PurchaseDeferred?.Invoke(snapshot);

            // Unblock an awaiting PurchaseAsync — the purchase will arrive out-of-band if approved.
            TryResolvePendingPurchase(productId, PurchaseResult.Failed(productId, snapshot, PurchaseFailure.Deferred));
        }

        private StoreProduct GetKnownProduct(string productId)
            => productId != null && _productsById.TryGetValue(productId, out var product) ? product : null;

        private StoreProduct GetOrCreateSnapshot(Product product)
        {
            var id = product.definition.id;
            if (!_productsById.TryGetValue(id, out var snapshot))
            {
                snapshot = new StoreProduct(id, MapType(product.definition.type));
                _productsById[id] = snapshot;
                _products.Add(snapshot);
            }

            return snapshot;
        }

        private static void UpdateSnapshot(StoreProduct snapshot, Product product)
        {
            snapshot.StoreSpecificId = product.definition.storeSpecificId;
            snapshot.AvailableToPurchase = product.availableToPurchase;
            snapshot.HasReceipt = product.hasReceipt;
            snapshot.Receipt = product.hasReceipt ? product.receipt : null;

            var metadata = product.metadata;
            if (metadata != null)
            {
                snapshot.Title = string.IsNullOrEmpty(metadata.localizedTitle) ? snapshot.Id : metadata.localizedTitle;
                snapshot.Description = metadata.localizedDescription ?? string.Empty;
                snapshot.PriceString = metadata.localizedPriceString ?? string.Empty;
                snapshot.Price = metadata.localizedPrice;
                snapshot.IsoCurrencyCode = metadata.isoCurrencyCode ?? string.Empty;
            }
        }

        private static bool IsApplePlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.tvOS:
#if UNITY_2023_1_OR_NEWER
                case RuntimePlatform.VisionOS:
#endif
                    return true;
                default:
                    return false;
            }
        }

        private static ProductType MapType(StoreProductType type)
        {
            switch (type)
            {
                case StoreProductType.NonConsumable: return ProductType.NonConsumable;
                case StoreProductType.Subscription: return ProductType.Subscription;
                default: return ProductType.Consumable;
            }
        }

        private static StoreProductType MapType(ProductType type)
        {
            switch (type)
            {
                case ProductType.NonConsumable: return StoreProductType.NonConsumable;
                case ProductType.Subscription: return StoreProductType.Subscription;
                default: return StoreProductType.Consumable;
            }
        }

        private static PurchaseFailure MapFailure(PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case PurchaseFailureReason.PurchasingUnavailable: return PurchaseFailure.PurchasingUnavailable;
                case PurchaseFailureReason.ExistingPurchasePending: return PurchaseFailure.PurchaseInProgress;
                case PurchaseFailureReason.ProductUnavailable: return PurchaseFailure.ProductUnavailable;
                case PurchaseFailureReason.SignatureInvalid: return PurchaseFailure.SignatureInvalid;
                case PurchaseFailureReason.UserCancelled: return PurchaseFailure.UserCancelled;
                case PurchaseFailureReason.PaymentDeclined: return PurchaseFailure.PaymentDeclined;
                case PurchaseFailureReason.DuplicateTransaction: return PurchaseFailure.DuplicateTransaction;
                default: return PurchaseFailure.Unknown;
            }
        }
    }
}
#endif
