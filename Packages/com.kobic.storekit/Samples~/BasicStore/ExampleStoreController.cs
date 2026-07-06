using UnityEngine;

namespace StoreKit.Samples
{
    /// <summary>
    /// Example game store controller. Extends <see cref="StoreControllerBase"/>, grants purchases
    /// into PlayerPrefs, and logs the flow. Replace the grant logic with your game's economy /
    /// save system, and the hooks with your game flow (screens, spinners, analytics).
    /// </summary>
    public sealed class ExampleStoreController : StoreControllerBase
    {
        private const string CoinsKey = "example.coins";
        private const string NoAdsKey = "example.no_ads";

        public int Coins => PlayerPrefs.GetInt(CoinsKey, 0);
        public bool NoAds => PlayerPrefs.GetInt(NoAdsKey, 0) == 1;

        protected override void GrantPurchase(PurchaseResult result)
        {
            // Called for every successful purchase, including restored ones. Must be idempotent
            // for non-consumables — stores may re-deliver purchases.
            switch (result.ProductId)
            {
                case "com.example.coins_100":
                    if (!result.IsRestored) // consumables are never restored; guard anyway
                    {
                        PlayerPrefs.SetInt(CoinsKey, Coins + 100);
                    }
                    break;

                case "com.example.no_ads":
                    PlayerPrefs.SetInt(NoAdsKey, 1);
                    break;

                default:
                    Debug.LogWarning($"Unhandled product: {result.ProductId}");
                    break;
            }

            PlayerPrefs.Save();
        }

        protected override void OnStoreInitialized(StoreInitializeResult result)
        {
            Debug.Log($"Store ready: {result} — {Store.Products.Count} product(s) loaded.");
        }

        protected override void OnPurchaseSucceeded(PurchaseResult result)
        {
            Debug.Log($"Purchase granted: {result.ProductId} (restored: {result.IsRestored}). Coins: {Coins}, NoAds: {NoAds}");
        }

        protected override void OnPurchaseFailed(PurchaseResult result)
        {
            Debug.Log($"Purchase failed: {result.ProductId} — {result.Failure}: {result.Message}");
        }
    }
}
