using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace StoreKit
{
    /// <summary>
    /// Abstraction over the underlying store implementation. StoreKit ships with
    /// <see cref="UnityIapGateway"/> (real Unity IAP) and <see cref="SimulatedStoreGateway"/>
    /// (Editor / no-IAP simulation). Implement this to plug in a custom store backend or a test double.
    /// </summary>
    public interface IStoreGateway
    {
        bool IsInitialized { get; }

        /// <summary>Products known to the gateway, in the order they were configured.</summary>
        IReadOnlyList<StoreProduct> Products { get; }

        /// <summary>Optional receipt validator invoked before a purchase is confirmed and reported as successful.</summary>
        IPurchaseValidator Validator { get; set; }

        /// <summary>
        /// Raised for successful purchases that were not initiated by an in-flight
        /// <see cref="PurchaseAsync"/> call: restored transactions, deferred purchases that were
        /// later approved, promotional purchases, and pending transactions delivered at startup.
        /// </summary>
        event Action<PurchaseResult> OutOfBandPurchase;

        /// <summary>Raised when a purchase becomes deferred (e.g. Apple "Ask to Buy").</summary>
        event Action<StoreProduct> PurchaseDeferred;

        UniTask<StoreInitializeResult> InitializeAsync(StoreKitSettings settings, CancellationToken cancellationToken = default);

        UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken cancellationToken = default);

        UniTask<RestoreResult> RestoreAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Hook for receipt validation (local or server-side). Runs after the store reports a purchase
    /// and before StoreKit confirms the transaction and reports success. Returning false fails the
    /// purchase with <see cref="PurchaseFailure.ValidationFailed"/>; the transaction is still
    /// confirmed with the store so it is not re-delivered.
    /// </summary>
    public interface IPurchaseValidator
    {
        UniTask<bool> ValidateAsync(StoreProduct product, string receipt, CancellationToken cancellationToken);
    }
}
