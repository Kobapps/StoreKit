using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoreKit
{
    /// <summary>
    /// Base for a per-product UI controller (e.g. a shop button). Bind it to a product id, hook
    /// its <see cref="Purchase"/> to a button, and override <see cref="OnProductUpdated"/> to
    /// refresh price/title/owned state. The controller keeps itself in sync with store
    /// initialization and purchases automatically.
    /// </summary>
    public abstract class ProductControllerBase : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The product id from the StoreKit catalog this controller represents.")]
        private string _productId;

        private bool _busy;

        public string ProductId
        {
            get => _productId;
            set
            {
                _productId = value;
                Refresh();
            }
        }

        /// <summary>Live product snapshot, or null while the store is not initialized.</summary>
        public StoreProduct Product => Store.IsInitialized ? Store.GetProduct(_productId) : null;

        /// <summary>True while a purchase initiated from this controller is in flight.</summary>
        public bool IsBusy => _busy;

        /// <summary>True when the store holds a receipt for this product.</summary>
        public bool IsOwned => Product?.HasReceipt ?? false;

        protected virtual void OnEnable()
        {
            Store.Initialized += HandleStoreChanged;
            Store.PurchaseCompleted += HandlePurchaseCompleted;
            Refresh();
        }

        protected virtual void OnDisable()
        {
            Store.Initialized -= HandleStoreChanged;
            Store.PurchaseCompleted -= HandlePurchaseCompleted;
        }

        /// <summary>Fire-and-forget purchase of this controller's product — bind to a Button's onClick.</summary>
        public void Purchase() => PurchaseAsync().Forget();

        /// <summary>Awaitable purchase of this controller's product.</summary>
        public async UniTask<PurchaseResult> PurchaseAsync()
        {
            if (_busy)
            {
                return PurchaseResult.Failed(_productId, Product, PurchaseFailure.PurchaseInProgress);
            }

            _busy = true;
            OnPurchaseStarted();
            try
            {
                var result = await Store.PurchaseAsync(_productId, destroyCancellationToken);
                OnPurchaseFinished(result);
                return result;
            }
            finally
            {
                _busy = false;
                Refresh();
            }
        }

        /// <summary>Re-reads the product from the store and calls <see cref="OnProductUpdated"/>.</summary>
        public void Refresh() => OnProductUpdated(Product);

        private void HandleStoreChanged(StoreInitializeResult result) => Refresh();

        private void HandlePurchaseCompleted(PurchaseResult result)
        {
            if (result.ProductId == _productId)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Update the bound UI. <paramref name="product"/> is null while the store is not
        /// initialized (show a loading/disabled state).
        /// </summary>
        protected abstract void OnProductUpdated(StoreProduct product);

        /// <summary>Called when a purchase from this controller starts (e.g. show a spinner, disable the button).</summary>
        protected virtual void OnPurchaseStarted() { }

        /// <summary>Called when a purchase from this controller finishes, success or failure.</summary>
        protected virtual void OnPurchaseFinished(PurchaseResult result) { }
    }
}
