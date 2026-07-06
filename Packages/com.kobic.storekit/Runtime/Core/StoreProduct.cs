namespace StoreKit
{
    /// <summary>
    /// Store-agnostic snapshot of a product. Instances are created and kept up to date by the
    /// active <see cref="IStoreGateway"/>; treat them as read-only from game code.
    /// </summary>
    public sealed class StoreProduct
    {
        /// <summary>The cross-store product id (the id used in <see cref="StoreProductDefinition"/>).</summary>
        public string Id { get; }

        public StoreProductType Type { get; }

        /// <summary>The id used on the currently active store, when it differs from <see cref="Id"/>.</summary>
        public string StoreSpecificId { get; set; }

        /// <summary>Localized title from the store (or the simulated title in the Editor).</summary>
        public string Title { get; set; }

        /// <summary>Localized description from the store.</summary>
        public string Description { get; set; }

        /// <summary>Localized, formatted price string (e.g. "$4.99").</summary>
        public string PriceString { get; set; }

        /// <summary>Localized decimal price.</summary>
        public decimal Price { get; set; }

        /// <summary>ISO 4217 currency code (e.g. "USD").</summary>
        public string IsoCurrencyCode { get; set; }

        /// <summary>Whether the store reports this product as purchasable right now.</summary>
        public bool AvailableToPurchase { get; set; }

        /// <summary>Whether the store holds a receipt for this product (owned non-consumable / active subscription).</summary>
        public bool HasReceipt { get; set; }

        /// <summary>Raw store receipt for this product, if any.</summary>
        public string Receipt { get; set; }

        public StoreProduct(string id, StoreProductType type)
        {
            Id = id;
            Type = type;
            StoreSpecificId = id;
            Title = id;
            Description = string.Empty;
            PriceString = string.Empty;
            IsoCurrencyCode = string.Empty;
        }

        public override string ToString() => $"StoreProduct({Id}, {Type}, {PriceString}, available={AvailableToPurchase}, owned={HasReceipt})";
    }
}
