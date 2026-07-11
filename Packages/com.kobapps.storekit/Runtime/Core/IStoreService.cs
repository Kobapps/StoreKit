using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace StoreKit
{
    /// <summary>
    /// The main StoreKit service: initialization, products, purchases, restore, popups and
    /// analytics fan-out. Use the static <see cref="Store"/> facade for the common single-store
    /// case, or create <see cref="StoreService"/> instances directly for advanced setups and tests.
    /// </summary>
    public interface IStoreService
    {
        bool IsInitialized { get; }
        bool IsPurchaseInProgress { get; }

        StoreKitSettings Settings { get; }

        /// <summary>Products known to the store, in catalog order. Empty until initialized.</summary>
        IReadOnlyList<StoreProduct> Products { get; }

        /// <summary>
        /// The popup presenter used for store popups. Defaults to
        /// <see cref="DefaultStorePopupPresenter"/> when <see cref="StoreKitSettings.enableDefaultPopups"/>
        /// is on. Assign a custom presenter to replace the default popups, or set null together
        /// with <c>enableDefaultPopups = false</c> to disable popups entirely.
        /// </summary>
        IStorePopupPresenter PopupPresenter { get; set; }

        /// <summary>Optional receipt validator applied to every purchase before it is reported successful.</summary>
        IPurchaseValidator Validator { get; set; }

        /// <summary>Raised once initialization finishes (success or failure).</summary>
        event Action<StoreInitializeResult> Initialized;
        /// <summary>Raised when a purchase attempt starts.</summary>
        event Action<StoreProduct> PurchaseStarted;
        /// <summary>Raised for every successful purchase, including restored and other out-of-band purchases. Grant content here (idempotently).</summary>
        event Action<PurchaseResult> PurchaseCompleted;
        /// <summary>Raised for every failed purchase (including user cancellation).</summary>
        event Action<PurchaseResult> PurchaseFailed;
        /// <summary>Raised when a purchase is deferred pending external approval (e.g. Apple Ask to Buy).</summary>
        event Action<StoreProduct> PurchaseDeferred;
        /// <summary>Raised when a restore request finishes.</summary>
        event Action<RestoreResult> RestoreCompleted;

        /// <summary>
        /// Initializes the store (connects to the store, fetches products). Safe to call multiple
        /// times; concurrent calls await the same in-flight initialization. Retries transient
        /// failures automatically per <see cref="StoreKitSettings"/>.
        /// </summary>
        UniTask<StoreInitializeResult> InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Completes once the store is initialized. Convenient for game flows that must wait for
        /// the store without triggering initialization themselves.
        /// </summary>
        UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Purchases a product and completes when the store finishes processing (including receipt
        /// validation when a <see cref="Validator"/> is set). Never throws for store failures —
        /// inspect <see cref="PurchaseResult"/>.
        /// </summary>
        UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores previous purchases. On iOS this triggers the App Store restore flow (required
        /// by Apple review for apps with non-consumables); on Google Play pending purchases are
        /// restored automatically at initialization, so this simply reports success.
        /// Restored purchases are delivered through <see cref="PurchaseCompleted"/> with
        /// <see cref="PurchaseResult.IsRestored"/> set.
        /// </summary>
        UniTask<RestoreResult> RestorePurchasesAsync(CancellationToken cancellationToken = default);

        StoreProduct GetProduct(string productId);

        /// <summary>True when the store holds a receipt for the product (owned non-consumable / active subscription).</summary>
        bool IsOwned(string productId);

        void AddAnalyticsListener(IStoreAnalyticsListener listener);
        void RemoveAnalyticsListener(IStoreAnalyticsListener listener);
    }
}
