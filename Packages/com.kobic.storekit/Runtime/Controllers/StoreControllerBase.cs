using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// Drop-in base for a game's store controller. Add a subclass to any scene: it initializes
    /// the store, listens to purchase events for its whole lifetime, and funnels every successful
    /// purchase — direct, restored, or out-of-band — into <see cref="GrantPurchase"/>.
    ///
    /// Override the virtual hooks to connect the store to your game flow (unlock screens, refresh
    /// currency, hide loading spinners, ...). Granting must be idempotent: stores can re-deliver
    /// a purchase (e.g. after a crash or on reinstall).
    /// </summary>
    public abstract class StoreControllerBase : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional explicit settings. When empty, the default StoreKitSettings asset from Resources is used.")]
        private StoreKitSettings _settings;

        [SerializeField]
        [Tooltip("Initialize the store automatically on Start.")]
        private bool _initializeOnStart = true;

        public bool IsStoreInitialized => Store.IsInitialized;

        protected StoreKitSettings Settings => _settings;

        protected virtual void Awake()
        {
            if (_settings != null)
            {
                // Only install our settings when no service exists yet — never clobber a service
                // that was already configured (e.g. installed by a DI container or a boot flow).
                if (Store.ServiceOrNull == null)
                {
                    Store.Configure(_settings);
                }
                else if (!ReferenceEquals(Store.ServiceOrNull.Settings, _settings))
                {
                    Debug.LogWarning($"[StoreKit] {GetType().Name}: settings override ignored — a store service is already active with different settings.", this);
                }
            }

            Store.Initialized += HandleInitialized;
            Store.PurchaseStarted += HandlePurchaseStarted;
            Store.PurchaseCompleted += HandlePurchaseCompleted;
            Store.PurchaseFailed += HandlePurchaseFailed;
            Store.PurchaseDeferred += HandlePurchaseDeferred;
            Store.RestoreCompleted += HandleRestoreCompleted;
        }

        protected virtual void OnDestroy()
        {
            Store.Initialized -= HandleInitialized;
            Store.PurchaseStarted -= HandlePurchaseStarted;
            Store.PurchaseCompleted -= HandlePurchaseCompleted;
            Store.PurchaseFailed -= HandlePurchaseFailed;
            Store.PurchaseDeferred -= HandlePurchaseDeferred;
            Store.RestoreCompleted -= HandleRestoreCompleted;
        }

        protected virtual void Start()
        {
            if (_initializeOnStart)
            {
                InitializeStoreAsync().Forget();
            }
        }

        /// <summary>Initializes the store (no-op when already initialized).</summary>
        public UniTask<StoreInitializeResult> InitializeStoreAsync()
            => Store.InitializeAsync(destroyCancellationToken);

        /// <summary>Awaitable purchase. Prefer this from game flow code.</summary>
        public UniTask<PurchaseResult> PurchaseAsync(string productId)
            => Store.PurchaseAsync(productId, destroyCancellationToken);

        /// <summary>Fire-and-forget purchase, convenient for UnityEvent/button bindings.</summary>
        public void Purchase(string productId) => PurchaseAsync(productId).Forget();

        /// <summary>Awaitable restore (iOS). See <see cref="IStoreService.RestorePurchasesAsync"/>.</summary>
        public UniTask<RestoreResult> RestorePurchasesAsync()
            => Store.RestorePurchasesAsync(destroyCancellationToken);

        /// <summary>Fire-and-forget restore, convenient for UnityEvent/button bindings.</summary>
        public void RestorePurchases() => RestorePurchasesAsync().Forget();

        private void HandlePurchaseCompleted(PurchaseResult result)
        {
            GrantPurchase(result);
            OnPurchaseSucceeded(result);
        }

        private void HandleInitialized(StoreInitializeResult result) => OnStoreInitialized(result);
        private void HandlePurchaseStarted(StoreProduct product) => OnPurchaseStarted(product);
        private void HandlePurchaseFailed(PurchaseResult result) => OnPurchaseFailed(result);
        private void HandlePurchaseDeferred(StoreProduct product) => OnPurchaseDeferred(product);
        private void HandleRestoreCompleted(RestoreResult result) => OnRestoreCompleted(result);

        /// <summary>
        /// Grant the purchased content to the player. Called for every successful purchase,
        /// including restored ones (<see cref="PurchaseResult.IsRestored"/>). Must be idempotent
        /// for non-consumables.
        /// </summary>
        protected abstract void GrantPurchase(PurchaseResult result);

        protected virtual void OnStoreInitialized(StoreInitializeResult result) { }
        protected virtual void OnPurchaseStarted(StoreProduct product) { }
        /// <summary>Called after <see cref="GrantPurchase"/> for every successful purchase.</summary>
        protected virtual void OnPurchaseSucceeded(PurchaseResult result) { }
        protected virtual void OnPurchaseFailed(PurchaseResult result) { }
        protected virtual void OnPurchaseDeferred(StoreProduct product) { }
        protected virtual void OnRestoreCompleted(RestoreResult result) { }
    }
}
