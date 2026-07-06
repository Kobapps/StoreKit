namespace StoreKit
{
    /// <summary>
    /// Analytics hook for store activity. Register implementations via
    /// <see cref="Store.AddAnalyticsListener"/> and forward the callbacks to your analytics SDK
    /// (Firebase, GameAnalytics, Unity Analytics, ...). Exceptions thrown by listeners are caught
    /// and logged so they can never break purchase flows.
    /// </summary>
    public interface IStoreAnalyticsListener
    {
        void OnStoreInitializeStarted();
        void OnStoreInitialized(StoreInitializeResult result);
        void OnPurchaseStarted(StoreProduct product);
        void OnPurchaseCompleted(PurchaseResult result);
        void OnPurchaseFailed(PurchaseResult result);
        void OnRestoreStarted();
        void OnRestoreCompleted(RestoreResult result);
    }

    /// <summary>Convenience base class — override only the callbacks you care about.</summary>
    public abstract class StoreAnalyticsListenerBase : IStoreAnalyticsListener
    {
        public virtual void OnStoreInitializeStarted() { }
        public virtual void OnStoreInitialized(StoreInitializeResult result) { }
        public virtual void OnPurchaseStarted(StoreProduct product) { }
        public virtual void OnPurchaseCompleted(PurchaseResult result) { }
        public virtual void OnPurchaseFailed(PurchaseResult result) { }
        public virtual void OnRestoreStarted() { }
        public virtual void OnRestoreCompleted(RestoreResult result) { }
    }
}
