using System.Threading;
using Cysharp.Threading.Tasks;

namespace StoreKit
{
    /// <summary>Semantic category of a store popup, so custom presenters can style or route per kind.</summary>
    public enum StorePopupKind
    {
        PurchaseSuccess = 0,
        PurchaseFailed = 1,
        PurchaseCancelled = 2,
        PurchaseDeferred = 3,
        RestoreSuccess = 4,
        RestoreFailed = 5,
        /// <summary>Two-button confirmation (used by the simulated store's purchase prompt).</summary>
        Confirmation = 6,
        Info = 7,
    }

    /// <summary>A popup request passed to the active <see cref="IStorePopupPresenter"/>.</summary>
    public readonly struct StorePopupRequest
    {
        public StorePopupKind Kind { get; }
        public string Title { get; }
        public string Message { get; }
        public string ConfirmText { get; }
        /// <summary>Cancel button label; null or empty means a single-button popup.</summary>
        public string CancelText { get; }

        public bool HasCancelButton => !string.IsNullOrEmpty(CancelText);

        public StorePopupRequest(StorePopupKind kind, string title, string message, string confirmText, string cancelText = null)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            ConfirmText = string.IsNullOrEmpty(confirmText) ? "OK" : confirmText;
            CancelText = cancelText;
        }
    }

    /// <summary>
    /// Presents store popups. Replace the default device popups with your own UI by assigning a
    /// custom implementation to <see cref="Store.PopupPresenter"/> (or the service's
    /// <see cref="IStoreService.PopupPresenter"/>); disable popups entirely by turning off
    /// <see cref="StoreKitSettings.enableDefaultPopups"/> without assigning a custom presenter.
    /// </summary>
    public interface IStorePopupPresenter
    {
        /// <summary>
        /// Shows the popup and completes when it is dismissed. Returns true when confirmed,
        /// false when cancelled (only meaningful for <see cref="StorePopupRequest.HasCancelButton"/> requests).
        /// </summary>
        UniTask<bool> ShowAsync(StorePopupRequest request, CancellationToken cancellationToken = default);
    }
}
