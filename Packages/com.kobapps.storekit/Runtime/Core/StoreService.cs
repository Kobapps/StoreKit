using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// Default <see cref="IStoreService"/> implementation. See <see cref="Store"/> for the static
    /// facade. Disposable so DI containers (and tests) can tear it down cleanly — disposal
    /// unhooks gateway events and cancels in-flight popups.
    /// </summary>
    public sealed class StoreService : IStoreService, IDisposable
    {
        private static readonly IReadOnlyList<StoreProduct> EmptyProducts = Array.Empty<StoreProduct>();

        private readonly List<IStoreAnalyticsListener> _analyticsListeners = new List<IStoreAnalyticsListener>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private bool _disposed;

        private StoreKitSettings _settings;
        private IStoreGateway _gateway;
        private bool _gatewayWired;

        private IStorePopupPresenter _customPresenter;
        private DefaultStorePopupPresenter _defaultPresenter;
        private IPurchaseValidator _validator;

        private bool _initializing;
        private StoreInitializeResult _lastInitResult;
        private bool _purchaseInProgress;

        public StoreService(StoreKitSettings settings, IStoreGateway gateway = null, IStorePopupPresenter popupPresenter = null)
        {
            _settings = settings;
            _gateway = gateway;
            _customPresenter = popupPresenter;
            if (settings != null)
            {
                StoreKitLog.Verbose = settings.verboseLogging;
            }
        }

        public bool IsInitialized => _gateway != null && _gateway.IsInitialized;

        public bool IsPurchaseInProgress => _purchaseInProgress;

        public StoreKitSettings Settings => _settings;

        public IReadOnlyList<StoreProduct> Products => _gateway?.Products ?? EmptyProducts;

        public IStorePopupPresenter PopupPresenter
        {
            get => ResolvePresenter();
            set => _customPresenter = value;
        }

        public IPurchaseValidator Validator
        {
            get => _validator;
            set
            {
                _validator = value;
                if (_gateway != null)
                {
                    _gateway.Validator = value;
                }
            }
        }

        public event Action<StoreInitializeResult> Initialized;
        public event Action<StoreProduct> PurchaseStarted;
        public event Action<PurchaseResult> PurchaseCompleted;
        public event Action<PurchaseResult> PurchaseFailed;
        public event Action<StoreProduct> PurchaseDeferred;
        public event Action<RestoreResult> RestoreCompleted;

        public UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken = default)
            => UniTask.WaitUntil(() => IsInitialized, cancellationToken: cancellationToken);

        public async UniTask<StoreInitializeResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return StoreInitializeResult.Failed(StoreInitializeStatus.Unknown, "The store service has been disposed.");
            }

            if (IsInitialized)
            {
                return StoreInitializeResult.Succeeded(StoreInitializeStatus.AlreadyInitialized);
            }

            if (_initializing)
            {
                await UniTask.WaitUntil(() => !_initializing, cancellationToken: cancellationToken);
                return _lastInitResult;
            }

            if (_settings == null)
            {
                _settings = StoreKitSettings.LoadDefault();
            }

            if (_settings == null || _settings.products == null || _settings.products.Count == 0)
            {
                var error = StoreInitializeResult.Failed(StoreInitializeStatus.ConfigurationError,
                    "No StoreKitSettings with products found. Create one via Assets > Create > StoreKit > Settings and place it in a Resources folder as 'StoreKitSettings'.");
                StoreKitLog.Error(error.Message);
                _lastInitResult = error;
                RaiseInitialized(error);
                return error;
            }

            StoreKitLog.Verbose = _settings.verboseLogging;
            _initializing = true;
            try
            {
                NotifyAnalytics(l => l.OnStoreInitializeStarted());

                EnsureGateway();

                var attempt = 0;
                StoreInitializeResult result;
                while (true)
                {
                    result = await _gateway.InitializeAsync(_settings, cancellationToken);
                    if (result.Success || !_settings.autoRetryInitialization || attempt >= _settings.maxInitializationRetries || !IsTransient(result.Status))
                    {
                        break;
                    }

                    attempt++;
                    var delay = _settings.initializationRetryDelaySeconds * Mathf.Pow(2f, attempt - 1);
                    StoreKitLog.Warn($"Initialization failed ({result.Status}: {result.Message}). Retrying in {delay:0.#}s (attempt {attempt}/{_settings.maxInitializationRetries}).");
                    await UniTask.Delay(TimeSpan.FromSeconds(delay), DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
                }

                _lastInitResult = result;
                if (result.Success)
                {
                    StoreKitLog.Info($"Store initialized with {Products.Count} product(s).");
                }
                else
                {
                    StoreKitLog.Error($"Store initialization failed: {result.Status} — {result.Message}");
                }

                RaiseInitialized(result);
                return result;
            }
            finally
            {
                _initializing = false;
            }
        }

        public async UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return PurchaseResult.Failed(productId, null, PurchaseFailure.StoreNotInitialized, "The store service has been disposed.");
            }

            if (string.IsNullOrEmpty(productId))
            {
                return FailPurchase(PurchaseResult.Failed(productId, null, PurchaseFailure.ProductNotFound, "Product id is null or empty."), showPopup: false);
            }

            if (!IsInitialized)
            {
                return FailPurchase(PurchaseResult.Failed(productId, null, PurchaseFailure.StoreNotInitialized), showPopup: true);
            }

            if (_purchaseInProgress)
            {
                // Guard against double-taps; no popup for this one.
                return FailPurchase(PurchaseResult.Failed(productId, GetProduct(productId), PurchaseFailure.PurchaseInProgress), showPopup: false);
            }

            var product = GetProduct(productId);
            if (product == null)
            {
                return FailPurchase(PurchaseResult.Failed(productId, null, PurchaseFailure.ProductNotFound,
                    $"Product '{productId}' is not in the StoreKit catalog."), showPopup: true);
            }

            _purchaseInProgress = true;
            try
            {
                StoreKitLog.Info($"Purchase started: {productId}");
                SafeInvoke(PurchaseStarted, product);
                NotifyAnalytics(l => l.OnPurchaseStarted(product));

                var result = await _gateway.PurchaseAsync(productId, cancellationToken);
                HandlePurchaseResult(result, suppressPopup: false);
                return result;
            }
            finally
            {
                _purchaseInProgress = false;
            }
        }

        public async UniTask<RestoreResult> RestorePurchasesAsync(CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                var notReady = RestoreResult.Failed(StoreKitMessages.Friendly(PurchaseFailure.StoreNotInitialized));
                SafeInvoke(RestoreCompleted, notReady);
                NotifyAnalytics(l => l.OnRestoreCompleted(notReady));
                ShowRestorePopup(notReady);
                return notReady;
            }

            StoreKitLog.Info("Restore purchases started.");
            NotifyAnalytics(l => l.OnRestoreStarted());

            var result = await _gateway.RestoreAsync(cancellationToken);
            StoreKitLog.Info($"Restore finished: {result}");

            SafeInvoke(RestoreCompleted, result);
            NotifyAnalytics(l => l.OnRestoreCompleted(result));
            ShowRestorePopup(result);
            return result;
        }

        public StoreProduct GetProduct(string productId)
        {
            var products = Products;
            for (int i = 0; i < products.Count; i++)
            {
                if (products[i].Id == productId)
                {
                    return products[i];
                }
            }

            return null;
        }

        public bool IsOwned(string productId) => GetProduct(productId)?.HasReceipt ?? false;

        public void AddAnalyticsListener(IStoreAnalyticsListener listener)
        {
            if (listener != null && !_analyticsListeners.Contains(listener))
            {
                _analyticsListeners.Add(listener);
            }
        }

        public void RemoveAnalyticsListener(IStoreAnalyticsListener listener)
        {
            _analyticsListeners.Remove(listener);
        }

        private void EnsureGateway()
        {
            if (_gateway == null)
            {
                _gateway = CreateDefaultGateway();
            }

            if (!_gatewayWired)
            {
                _gateway.Validator = _validator;
                _gateway.OutOfBandPurchase += OnOutOfBandPurchase;
                _gateway.PurchaseDeferred += OnGatewayPurchaseDeferred;
                if (_gateway is SimulatedStoreGateway simulated)
                {
                    simulated.ConfirmationPrompt = ShowSimulatedConfirmationAsync;
                }

                _gatewayWired = true;
            }
        }

        private IStoreGateway CreateDefaultGateway()
        {
#if UNITY_EDITOR
            if (_settings.useSimulatedStoreInEditor)
            {
                StoreKitLog.Info("Using simulated store gateway (Editor).");
                return new SimulatedStoreGateway();
            }
#endif
#if STOREKIT_HAS_IAP
            return new UnityIapGateway();
#else
            StoreKitLog.Warn("com.unity.purchasing is not installed — falling back to the simulated store gateway.");
            return new SimulatedStoreGateway();
#endif
        }

        private static bool IsTransient(StoreInitializeStatus status)
        {
            switch (status)
            {
                case StoreInitializeStatus.ServicesInitializationFailed:
                case StoreInitializeStatus.NoProductsAvailable:
                case StoreInitializeStatus.Unknown:
                    return true;
                default:
                    return false;
            }
        }

        private void RaiseInitialized(StoreInitializeResult result)
        {
            SafeInvoke(Initialized, result);
            NotifyAnalytics(l => l.OnStoreInitialized(result));
        }

        private void OnOutOfBandPurchase(PurchaseResult result)
        {
            StoreKitLog.Info($"Out-of-band purchase: {result}");
            // Restored purchases are summarized by the restore popup; don't show one popup per item.
            HandlePurchaseResult(result, suppressPopup: result.IsRestored);
        }

        private void OnGatewayPurchaseDeferred(StoreProduct product)
        {
            StoreKitLog.Info($"Purchase deferred: {product?.Id}");
            SafeInvoke(PurchaseDeferred, product);
            if (_settings != null && _settings.showDeferredPopup)
            {
                ShowPopup(StorePopupKind.PurchaseDeferred, _settings.popupTexts.purchaseDeferredTitle, _settings.popupTexts.purchaseDeferredMessage);
            }
        }

        private void HandlePurchaseResult(PurchaseResult result, bool suppressPopup)
        {
            if (result.Success)
            {
                StoreKitLog.Info($"Purchase completed: {result}");
                SafeInvoke(PurchaseCompleted, result);
                NotifyAnalytics(l => l.OnPurchaseCompleted(result));

                if (!suppressPopup && _settings != null && _settings.showSuccessPopup)
                {
                    ShowPopup(StorePopupKind.PurchaseSuccess, _settings.popupTexts.purchaseSuccessTitle,
                        Format(_settings.popupTexts.purchaseSuccessMessage, result.Product?.Title ?? result.ProductId));
                }
            }
            else
            {
                FailPurchase(result, showPopup: !suppressPopup, raiseDeferredInstead: true);
            }
        }

        private PurchaseResult FailPurchase(PurchaseResult result, bool showPopup, bool raiseDeferredInstead = false)
        {
            if (result.Failure == PurchaseFailure.Deferred && raiseDeferredInstead)
            {
                // Deferred purchases already raise PurchaseDeferred + their own popup.
                SafeInvoke(PurchaseFailed, result);
                NotifyAnalytics(l => l.OnPurchaseFailed(result));
                return result;
            }

            StoreKitLog.Warn($"Purchase failed: {result}");
            SafeInvoke(PurchaseFailed, result);
            NotifyAnalytics(l => l.OnPurchaseFailed(result));

            if (showPopup && _settings != null)
            {
                if (result.Failure == PurchaseFailure.UserCancelled)
                {
                    if (_settings.showCancelledPopup)
                    {
                        ShowPopup(StorePopupKind.PurchaseCancelled, _settings.popupTexts.purchaseCancelledTitle, _settings.popupTexts.purchaseCancelledMessage);
                    }
                }
                else if (_settings.showFailurePopup)
                {
                    ShowPopup(StorePopupKind.PurchaseFailed, _settings.popupTexts.purchaseFailedTitle,
                        Format(_settings.popupTexts.purchaseFailedMessage, result.Message));
                }
            }

            return result;
        }

        private void ShowRestorePopup(RestoreResult result)
        {
            if (_settings == null || !_settings.showRestorePopup)
            {
                return;
            }

            if (result.Success)
            {
                ShowPopup(StorePopupKind.RestoreSuccess, _settings.popupTexts.restoreSuccessTitle,
                    Format(_settings.popupTexts.restoreSuccessMessage, result.RestoredCount));
            }
            else
            {
                ShowPopup(StorePopupKind.RestoreFailed, _settings.popupTexts.restoreFailedTitle,
                    Format(_settings.popupTexts.restoreFailedMessage, result.Message));
            }
        }

        private void ShowPopup(StorePopupKind kind, string title, string message)
        {
            var presenter = ResolvePresenter();
            if (presenter == null)
            {
                return;
            }

            var confirm = _settings != null ? _settings.popupTexts.confirmButton : "OK";
            var request = new StorePopupRequest(kind, title, message, confirm);
            ShowPopupSafeAsync(presenter, request, _lifetimeCts.Token).Forget();
        }

        private static async UniTaskVoid ShowPopupSafeAsync(IStorePopupPresenter presenter, StorePopupRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await presenter.ShowAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                StoreKitLog.Error($"Popup presenter threw: {e}");
            }
        }

        private async UniTask<bool> ShowSimulatedConfirmationAsync(StoreProduct product, CancellationToken cancellationToken)
        {
            var presenter = ResolvePresenter();
            if (presenter == null)
            {
                return true;
            }

            var texts = _settings != null ? _settings.popupTexts : new StorePopupTexts();
            var request = new StorePopupRequest(
                StorePopupKind.Confirmation,
                texts.simulatedConfirmTitle,
                Format(texts.simulatedConfirmMessage, product?.Title, product?.PriceString),
                texts.buyButton,
                texts.cancelButton);

            try
            {
                return await presenter.ShowAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                StoreKitLog.Error($"Popup presenter threw during confirmation: {e}");
                return true;
            }
        }

        private IStorePopupPresenter ResolvePresenter()
        {
            if (_customPresenter != null)
            {
                return _customPresenter;
            }

            if (_settings != null && _settings.enableDefaultPopups)
            {
                return _defaultPresenter ??= new DefaultStorePopupPresenter();
            }

            return null;
        }

        private static string Format(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private void NotifyAnalytics(Action<IStoreAnalyticsListener> callback)
        {
            for (int i = _analyticsListeners.Count - 1; i >= 0; i--)
            {
                try
                {
                    callback(_analyticsListeners[i]);
                }
                catch (Exception e)
                {
                    StoreKitLog.Error($"Analytics listener threw: {e}");
                }
            }
        }

        /// <summary>
        /// Tears the service down: unhooks gateway events, drops analytics listeners, and cancels
        /// in-flight popups. Called automatically by DI containers when the owning scope is
        /// disposed. The underlying gateway is left as-is (Unity IAP has no shutdown API).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_gateway != null && _gatewayWired)
            {
                _gateway.OutOfBandPurchase -= OnOutOfBandPurchase;
                _gateway.PurchaseDeferred -= OnGatewayPurchaseDeferred;
                if (_gateway is SimulatedStoreGateway simulated)
                {
                    simulated.ConfirmationPrompt = null;
                }

                _gatewayWired = false;
            }

            _analyticsListeners.Clear();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }

        private void SafeInvoke<T>(Action<T> handler, T argument)
        {
            if (handler == null)
            {
                return;
            }

            foreach (var d in handler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(argument);
                }
                catch (Exception e)
                {
                    StoreKitLog.Error($"Event handler threw: {e}");
                }
            }
        }
    }
}
