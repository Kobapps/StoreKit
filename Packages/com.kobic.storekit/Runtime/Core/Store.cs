using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace StoreKit
{
    /// <summary>
    /// Static facade over a single shared <see cref="IStoreService"/> — the simple entry point
    /// for most games:
    /// <code>
    /// await Store.InitializeAsync();
    /// var result = await Store.PurchaseAsync("com.mygame.coins_100");
    /// </code>
    /// The underlying service is created lazily from the default <see cref="StoreKitSettings"/>
    /// asset in Resources. Use <see cref="Configure"/> to supply settings/gateway/presenter
    /// explicitly (call it before anything else touches the facade).
    /// Static events survive service replacement — subscribers do not need to re-subscribe.
    /// </summary>
    public static class Store
    {
        private static IStoreService _service;

        /// <summary>The active service. Created lazily from default settings on first access.</summary>
        public static IStoreService Service
        {
            get
            {
                if (_service == null)
                {
                    SetService(new StoreService(StoreKitSettings.LoadDefault()));
                }

                return _service;
            }
        }

        /// <summary>The active service, or null if none was installed yet. Never creates one — safe for checks.</summary>
        public static IStoreService ServiceOrNull => _service;

        public static bool IsInitialized => _service != null && _service.IsInitialized;

        public static bool IsPurchaseInProgress => _service != null && _service.IsPurchaseInProgress;

        public static IReadOnlyList<StoreProduct> Products => Service.Products;

        /// <summary>See <see cref="IStoreService.PopupPresenter"/>.</summary>
        public static IStorePopupPresenter PopupPresenter
        {
            get => Service.PopupPresenter;
            set => Service.PopupPresenter = value;
        }

        /// <summary>See <see cref="IStoreService.Validator"/>.</summary>
        public static IPurchaseValidator Validator
        {
            get => Service.Validator;
            set => Service.Validator = value;
        }

        public static event Action<StoreInitializeResult> Initialized;
        public static event Action<StoreProduct> PurchaseStarted;
        public static event Action<PurchaseResult> PurchaseCompleted;
        public static event Action<PurchaseResult> PurchaseFailed;
        public static event Action<StoreProduct> PurchaseDeferred;
        public static event Action<RestoreResult> RestoreCompleted;

        /// <summary>
        /// Creates and installs the shared service with explicit settings and optional custom
        /// gateway / popup presenter. Call once during boot, before any other facade use.
        /// </summary>
        public static IStoreService Configure(StoreKitSettings settings, IStoreGateway gateway = null, IStorePopupPresenter popupPresenter = null)
        {
            SetService(new StoreService(settings, gateway, popupPresenter));
            return _service;
        }

        /// <summary>
        /// Script-driven boot: builds settings fluently and installs the shared service.
        /// <code>
        /// Store.Configure(store => store
        ///     .AddConsumable("com.game.coins_100")
        ///     .AddNonConsumable("com.game.no_ads"));
        /// </code>
        /// The builder starts from the default Resources asset when one exists (cloned — the
        /// asset itself is never modified), otherwise from empty settings.
        /// </summary>
        public static IStoreService Configure(Action<StoreKitSettingsBuilder> configure,
            IStoreGateway gateway = null, IStorePopupPresenter popupPresenter = null)
        {
            var builder = new StoreKitSettingsBuilder(StoreKitSettings.LoadDefault());
            configure?.Invoke(builder);
            return Configure(builder.Build(), gateway, popupPresenter);
        }

        /// <summary>Replaces the shared service. Static events are re-wired automatically.</summary>
        public static void SetService(IStoreService service)
        {
            if (_service != null)
            {
                Unwire(_service);
            }

            _service = service;
            if (_service != null)
            {
                Wire(_service);
            }
        }

        /// <summary>Resets the facade (mainly for tests / domain-reload-disabled setups).</summary>
        public static void Reset() => SetService(null);

        public static UniTask<StoreInitializeResult> InitializeAsync(CancellationToken cancellationToken = default)
            => Service.InitializeAsync(cancellationToken);

        /// <summary>Completes once the store is initialized (does not trigger initialization itself).</summary>
        public static UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken = default)
            => Service.WaitUntilInitializedAsync(cancellationToken);

        public static UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
            => Service.PurchaseAsync(productId, cancellationToken);

        public static UniTask<RestoreResult> RestorePurchasesAsync(CancellationToken cancellationToken = default)
            => Service.RestorePurchasesAsync(cancellationToken);

        public static StoreProduct GetProduct(string productId) => Service.GetProduct(productId);

        public static bool IsOwned(string productId) => Service.IsOwned(productId);

        public static void AddAnalyticsListener(IStoreAnalyticsListener listener) => Service.AddAnalyticsListener(listener);

        public static void RemoveAnalyticsListener(IStoreAnalyticsListener listener) => Service.RemoveAnalyticsListener(listener);

        private static void Wire(IStoreService service)
        {
            service.Initialized += RelayInitialized;
            service.PurchaseStarted += RelayPurchaseStarted;
            service.PurchaseCompleted += RelayPurchaseCompleted;
            service.PurchaseFailed += RelayPurchaseFailed;
            service.PurchaseDeferred += RelayPurchaseDeferred;
            service.RestoreCompleted += RelayRestoreCompleted;
        }

        private static void Unwire(IStoreService service)
        {
            service.Initialized -= RelayInitialized;
            service.PurchaseStarted -= RelayPurchaseStarted;
            service.PurchaseCompleted -= RelayPurchaseCompleted;
            service.PurchaseFailed -= RelayPurchaseFailed;
            service.PurchaseDeferred -= RelayPurchaseDeferred;
            service.RestoreCompleted -= RelayRestoreCompleted;
        }

        private static void RelayInitialized(StoreInitializeResult result) => Initialized?.Invoke(result);
        private static void RelayPurchaseStarted(StoreProduct product) => PurchaseStarted?.Invoke(product);
        private static void RelayPurchaseCompleted(PurchaseResult result) => PurchaseCompleted?.Invoke(result);
        private static void RelayPurchaseFailed(PurchaseResult result) => PurchaseFailed?.Invoke(result);
        private static void RelayPurchaseDeferred(StoreProduct product) => PurchaseDeferred?.Invoke(product);
        private static void RelayRestoreCompleted(RestoreResult result) => RestoreCompleted?.Invoke(result);
    }

    /// <summary>
    /// Binds a service instance to the static <see cref="Store"/> facade for its lifetime.
    /// Used by the DI integrations: create (or resolve) it when the container builds, and on
    /// container disposal the facade is reset — but only if it still points at that service, so
    /// a newer scope's binding is never clobbered by an older scope tearing down.
    /// </summary>
    public sealed class StoreFacadeBinding : IDisposable
    {
        private readonly IStoreService _service;

        public StoreFacadeBinding(IStoreService service)
        {
            _service = service;
            Store.SetService(service);
        }

        public void Dispose()
        {
            if (ReferenceEquals(Store.ServiceOrNull, _service))
            {
                Store.Reset();
            }
        }
    }
}
