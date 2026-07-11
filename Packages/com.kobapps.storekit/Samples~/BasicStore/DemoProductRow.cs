using UnityEngine;
using UnityEngine.UI;

namespace StoreKit.Samples
{
    /// <summary>
    /// A shop-row controller built at runtime by <see cref="StoreDemo"/>. Extends
    /// <see cref="ProductControllerBase"/>, so the store keeps its price / owned / busy state in
    /// sync automatically — the row only has to render. This is the "wired from code" counterpart
    /// to <see cref="ExampleProductButton"/> (which wires the same base class from the inspector).
    /// </summary>
    public sealed class DemoProductRow : ProductControllerBase
    {
        private Button _button;
        private Text _priceLabel;

        /// <summary>Connects the runtime-created button and price label, then hooks the click.</summary>
        public void Bind(Button button, Text priceLabel)
        {
            _button = button;
            _priceLabel = priceLabel;
            if (_button != null)
            {
                _button.onClick.AddListener(Purchase);
            }

            Refresh();
        }

        protected override void OnProductUpdated(StoreProduct product)
        {
            if (_priceLabel == null)
            {
                return; // Bind() has not run yet (base OnEnable calls this immediately on AddComponent).
            }

            if (product == null)
            {
                SetInteractable(false);
                _priceLabel.text = "…";
                return;
            }

            if (IsOwned && product.Type != StoreProductType.Consumable)
            {
                SetInteractable(false);
                _priceLabel.text = "Owned";
            }
            else
            {
                SetInteractable(product.AvailableToPurchase && !IsBusy);
                _priceLabel.text = product.PriceString;
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
    }
}
