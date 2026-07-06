using UnityEngine;
using UnityEngine.UI;

namespace StoreKit.Samples
{
    /// <summary>
    /// Example shop button. Extends <see cref="ProductControllerBase"/> and keeps a uGUI Button
    /// and price label in sync with the store. Wire the Button's onClick to <c>Purchase()</c>
    /// (done automatically in Awake when a Button is assigned).
    /// </summary>
    public sealed class ExampleProductButton : ProductControllerBase
    {
        [SerializeField] private Button _button;
        [SerializeField] private Text _titleLabel;
        [SerializeField] private Text _priceLabel;

        private void Awake()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(Purchase);
            }
        }

        protected override void OnProductUpdated(StoreProduct product)
        {
            if (product == null)
            {
                // Store not initialized yet — show a disabled/loading state.
                SetInteractable(false);
                SetText(_priceLabel, "...");
                return;
            }

            if (_titleLabel != null)
            {
                SetText(_titleLabel, product.Title);
            }

            if (IsOwned && product.Type != StoreProductType.Consumable)
            {
                SetInteractable(false);
                SetText(_priceLabel, "Owned");
            }
            else
            {
                SetInteractable(product.AvailableToPurchase && !IsBusy);
                SetText(_priceLabel, product.PriceString);
            }
        }

        protected override void OnPurchaseStarted() => SetInteractable(false);

        protected override void OnPurchaseFinished(PurchaseResult result) => Refresh();

        private void SetInteractable(bool value)
        {
            if (_button != null)
            {
                _button.interactable = value;
            }
        }

        private static void SetText(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
