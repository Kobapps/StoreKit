using System;

namespace StoreKit
{
    /// <summary>Store-agnostic product type.</summary>
    public enum StoreProductType
    {
        Consumable = 0,
        NonConsumable = 1,
        Subscription = 2,
    }

    /// <summary>Outcome status of store initialization.</summary>
    public enum StoreInitializeStatus
    {
        Success = 0,
        AlreadyInitialized = 1,
        /// <summary>Purchasing is disabled on the device (e.g. parental controls).</summary>
        PurchasingUnavailable = 2,
        /// <summary>No StoreKitSettings or no products configured.</summary>
        ConfigurationError = 3,
        /// <summary>Unity Gaming Services failed to initialize (usually connectivity).</summary>
        ServicesInitializationFailed = 4,
        /// <summary>The store returned no products (usually connectivity or store setup).</summary>
        NoProductsAvailable = 5,
        /// <summary>The app is not known to the store.</summary>
        AppNotKnown = 6,
        Cancelled = 7,
        Unknown = 8,
    }

    /// <summary>Store-agnostic purchase failure reason.</summary>
    public enum PurchaseFailure
    {
        None = 0,
        StoreNotInitialized = 1,
        ProductNotFound = 2,
        ProductUnavailable = 3,
        /// <summary>Another purchase is already being processed.</summary>
        PurchaseInProgress = 4,
        UserCancelled = 5,
        PaymentDeclined = 6,
        DuplicateTransaction = 7,
        SignatureInvalid = 8,
        /// <summary>The registered <see cref="IPurchaseValidator"/> rejected the receipt.</summary>
        ValidationFailed = 9,
        PurchasingUnavailable = 10,
        /// <summary>The purchase is pending external approval (e.g. Apple Ask to Buy).
        /// When approved, the purchase completes later and is raised as an out-of-band success.</summary>
        Deferred = 11,
        Cancelled = 12,
        Unknown = 13,
    }

    /// <summary>Result of <c>InitializeAsync</c>.</summary>
    public readonly struct StoreInitializeResult
    {
        public bool Success { get; }
        public StoreInitializeStatus Status { get; }
        public string Message { get; }

        public StoreInitializeResult(bool success, StoreInitializeStatus status, string message)
        {
            Success = success;
            Status = status;
            Message = message ?? string.Empty;
        }

        public static StoreInitializeResult Succeeded(StoreInitializeStatus status = StoreInitializeStatus.Success, string message = "")
            => new StoreInitializeResult(true, status, message);

        public static StoreInitializeResult Failed(StoreInitializeStatus status, string message = "")
            => new StoreInitializeResult(false, status, message);

        public override string ToString() => $"StoreInitializeResult(Success={Success}, Status={Status}, Message=\"{Message}\")";
    }

    /// <summary>Result of a purchase attempt (or an out-of-band purchase such as a restore).</summary>
    public readonly struct PurchaseResult
    {
        public bool Success { get; }
        public string ProductId { get; }
        /// <summary>The product, when known. May be null for failures that happen before product resolution.</summary>
        public StoreProduct Product { get; }
        public PurchaseFailure Failure { get; }
        public string Message { get; }
        public string TransactionId { get; }
        /// <summary>Raw store receipt payload (Unity IAP unified receipt JSON, or a simulated receipt in the Editor).</summary>
        public string Receipt { get; }
        /// <summary>True when this purchase arrived as part of a restore flow.</summary>
        public bool IsRestored { get; }

        public PurchaseResult(bool success, string productId, StoreProduct product, PurchaseFailure failure,
            string message, string transactionId, string receipt, bool isRestored)
        {
            Success = success;
            ProductId = productId ?? string.Empty;
            Product = product;
            Failure = failure;
            Message = message ?? string.Empty;
            TransactionId = transactionId ?? string.Empty;
            Receipt = receipt ?? string.Empty;
            IsRestored = isRestored;
        }

        public static PurchaseResult Succeeded(StoreProduct product, string transactionId, string receipt, bool isRestored = false)
            => new PurchaseResult(true, product?.Id, product, PurchaseFailure.None, string.Empty, transactionId, receipt, isRestored);

        public static PurchaseResult Failed(string productId, StoreProduct product, PurchaseFailure failure, string message = null)
            => new PurchaseResult(false, productId, product, failure, message ?? StoreKitMessages.Friendly(failure), null, null, false);

        public override string ToString() => Success
            ? $"PurchaseResult(Success, {ProductId}, tx={TransactionId}, restored={IsRestored})"
            : $"PurchaseResult(Failed, {ProductId}, {Failure}, \"{Message}\")";
    }

    /// <summary>Result of a restore-purchases request.</summary>
    public readonly struct RestoreResult
    {
        public bool Success { get; }
        /// <summary>Number of purchases that were re-delivered during this restore.</summary>
        public int RestoredCount { get; }
        public string Message { get; }

        public RestoreResult(bool success, int restoredCount, string message)
        {
            Success = success;
            RestoredCount = restoredCount;
            Message = message ?? string.Empty;
        }

        public static RestoreResult Succeeded(int restoredCount, string message = "")
            => new RestoreResult(true, restoredCount, message);

        public static RestoreResult Failed(string message)
            => new RestoreResult(false, 0, message);

        public override string ToString() => $"RestoreResult(Success={Success}, Restored={RestoredCount}, \"{Message}\")";
    }

    /// <summary>User-facing default messages for purchase failures. Override text via <see cref="StorePopupTexts"/> or a custom popup presenter.</summary>
    public static class StoreKitMessages
    {
        public static string Friendly(PurchaseFailure failure)
        {
            switch (failure)
            {
                case PurchaseFailure.None: return string.Empty;
                case PurchaseFailure.UserCancelled: return "The purchase was cancelled.";
                case PurchaseFailure.PaymentDeclined: return "Payment was declined.";
                case PurchaseFailure.ProductNotFound:
                case PurchaseFailure.ProductUnavailable: return "This item is currently unavailable.";
                case PurchaseFailure.PurchasingUnavailable: return "Purchasing is disabled on this device.";
                case PurchaseFailure.DuplicateTransaction: return "You already own this item.";
                case PurchaseFailure.SignatureInvalid:
                case PurchaseFailure.ValidationFailed: return "The purchase could not be verified.";
                case PurchaseFailure.StoreNotInitialized: return "The store is not ready yet. Please try again in a moment.";
                case PurchaseFailure.PurchaseInProgress: return "Another purchase is already in progress.";
                case PurchaseFailure.Deferred: return "Your purchase is awaiting approval.";
                case PurchaseFailure.Cancelled: return "The purchase was interrupted.";
                default: return "Something went wrong. Please try again.";
            }
        }
    }
}
