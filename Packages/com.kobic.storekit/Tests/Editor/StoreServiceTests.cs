using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace StoreKit.Tests
{
    /// <summary>
    /// Edit-mode tests exercising the full service pipeline against the simulated gateway:
    /// initialization, purchases, failures, ownership, restore, events, and analytics fan-out.
    /// </summary>
    public sealed class StoreServiceTests
    {
        private const string Coins = "test.coins_100";
        private const string NoAds = "test.no_ads";
        private const string Sub = "test.vip_sub";

        private StoreKitSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<StoreKitSettings>();
            _settings.enableDefaultPopups = false;
            _settings.autoRetryInitialization = false;
            _settings.products = new List<StoreProductDefinition>
            {
                new StoreProductDefinition { id = Coins, type = StoreProductType.Consumable, simulatedPrice = "$0.99" },
                new StoreProductDefinition { id = NoAds, type = StoreProductType.NonConsumable, simulatedPrice = "$2.99" },
                new StoreProductDefinition { id = Sub, type = StoreProductType.Subscription, simulatedPrice = "$4.99" },
            };
            _settings.simulation = new SimulatedStoreOptions
            {
                askForConfirmation = false,
                initializeDelaySeconds = 0f,
                purchaseDelaySeconds = 0f,
                persistOwnership = false,
            };
        }

        [TearDown]
        public void TearDown()
        {
            SimulatedStoreGateway.ClearPersistedOwnership(_settings);
            Object.DestroyImmediate(_settings);
        }

        private StoreService CreateService() => new StoreService(_settings, new SimulatedStoreGateway());

        [UnityTest]
        public IEnumerator Initialize_Succeeds_AndPopulatesProducts() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            var result = await service.InitializeAsync();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(service.IsInitialized);
            Assert.AreEqual(3, service.Products.Count);
            Assert.NotNull(service.GetProduct(Coins));
            Assert.AreEqual("$2.99", service.GetProduct(NoAds).PriceString);
        });

        [UnityTest]
        public IEnumerator Initialize_WithNoProducts_FailsWithConfigurationError() => UniTask.ToCoroutine(async () =>
        {
            _settings.products.Clear();
            var service = CreateService();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var result = await service.InitializeAsync();
                Assert.IsFalse(result.Success);
                Assert.AreEqual(StoreInitializeStatus.ConfigurationError, result.Status);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });

        [UnityTest]
        public IEnumerator Initialize_SimulatedFailure_ReportsFailure() => UniTask.ToCoroutine(async () =>
        {
            _settings.simulation.failInitialization = true;
            var service = CreateService();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var result = await service.InitializeAsync();
                Assert.IsFalse(result.Success);
                Assert.IsFalse(service.IsInitialized);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });

        [UnityTest]
        public IEnumerator Purchase_Consumable_Succeeds() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            await service.InitializeAsync();

            var result = await service.PurchaseAsync(Coins);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(Coins, result.ProductId);
            Assert.IsNotEmpty(result.TransactionId);
            Assert.IsNotEmpty(result.Receipt);
            Assert.IsFalse(service.IsOwned(Coins), "Consumables must not be marked as owned.");
        });

        [UnityTest]
        public IEnumerator Purchase_BeforeInitialize_FailsWithStoreNotInitialized() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            var result = await service.PurchaseAsync(Coins);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailure.StoreNotInitialized, result.Failure);
        });

        [UnityTest]
        public IEnumerator Purchase_UnknownProduct_FailsWithProductNotFound() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            await service.InitializeAsync();

            var result = await service.PurchaseAsync("does.not.exist");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailure.ProductNotFound, result.Failure);
        });

        [UnityTest]
        public IEnumerator Purchase_SimulatedCancel_FailsWithUserCancelled() => UniTask.ToCoroutine(async () =>
        {
            _settings.simulation.failPurchases = true;
            _settings.simulation.purchaseFailure = PurchaseFailure.UserCancelled;
            var service = CreateService();
            await service.InitializeAsync();

            var result = await service.PurchaseAsync(Coins);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailure.UserCancelled, result.Failure);
        });

        [UnityTest]
        public IEnumerator Purchase_NonConsumableTwice_FailsWithDuplicateTransaction() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            await service.InitializeAsync();

            var first = await service.PurchaseAsync(NoAds);
            var second = await service.PurchaseAsync(NoAds);

            Assert.IsTrue(first.Success);
            Assert.IsTrue(service.IsOwned(NoAds));
            Assert.IsFalse(second.Success);
            Assert.AreEqual(PurchaseFailure.DuplicateTransaction, second.Failure);
        });

        [UnityTest]
        public IEnumerator Purchase_Concurrent_SecondFailsWithPurchaseInProgress() => UniTask.ToCoroutine(async () =>
        {
            _settings.simulation.purchaseDelaySeconds = 0.2f;
            var service = CreateService();
            await service.InitializeAsync();

            var first = service.PurchaseAsync(Coins);
            var second = await service.PurchaseAsync(Coins);
            var firstResult = await first;

            Assert.IsTrue(firstResult.Success);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(PurchaseFailure.PurchaseInProgress, second.Failure);
        });

        [UnityTest]
        public IEnumerator Restore_RedeliversOwnedNonConsumables_ThroughPurchaseCompleted() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            await service.InitializeAsync();
            await service.PurchaseAsync(NoAds);
            await service.PurchaseAsync(Sub);

            var restoredEvents = new List<PurchaseResult>();
            service.PurchaseCompleted += r =>
            {
                if (r.IsRestored)
                {
                    restoredEvents.Add(r);
                }
            };

            var result = await service.RestorePurchasesAsync();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.RestoredCount);
            Assert.AreEqual(2, restoredEvents.Count);
        });

        [UnityTest]
        public IEnumerator Validator_Rejection_FailsPurchaseWithValidationFailed() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            service.Validator = new RejectingValidator();
            await service.InitializeAsync();

            var result = await service.PurchaseAsync(Coins);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PurchaseFailure.ValidationFailed, result.Failure);
        });

        [UnityTest]
        public IEnumerator Events_And_Analytics_FireInOrder() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            var analytics = new RecordingAnalytics();
            service.AddAnalyticsListener(analytics);

            var events = new List<string>();
            service.Initialized += _ => events.Add("initialized");
            service.PurchaseStarted += _ => events.Add("started");
            service.PurchaseCompleted += _ => events.Add("completed");
            service.PurchaseFailed += _ => events.Add("failed");

            await service.InitializeAsync();
            await service.PurchaseAsync(Coins);

            CollectionAssert.AreEqual(new[] { "initialized", "started", "completed" }, events);
            CollectionAssert.AreEqual(
                new[] { "init_started", "initialized", "purchase_started", "purchase_completed" },
                analytics.Calls);
        });

        [UnityTest]
        public IEnumerator Analytics_ListenerException_DoesNotBreakPurchase() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            service.AddAnalyticsListener(new ThrowingAnalytics());

            LogAssert.ignoreFailingMessages = true;
            try
            {
                await service.InitializeAsync();
                var result = await service.PurchaseAsync(Coins);
                Assert.IsTrue(result.Success);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });

        [UnityTest]
        public IEnumerator ConcurrentInitialize_SharesSingleInitialization() => UniTask.ToCoroutine(async () =>
        {
            _settings.simulation.initializeDelaySeconds = 0.2f;
            var service = CreateService();

            var initializedCount = 0;
            service.Initialized += _ => initializedCount++;

            var first = service.InitializeAsync();
            var second = service.InitializeAsync();
            var results = await UniTask.WhenAll(first, second);

            Assert.IsTrue(results.Item1.Success);
            Assert.IsTrue(results.Item2.Success);
            Assert.AreEqual(1, initializedCount);
        });

        [UnityTest]
        public IEnumerator WaitUntilInitialized_CompletesAfterInitialize() => UniTask.ToCoroutine(async () =>
        {
            _settings.simulation.initializeDelaySeconds = 0.1f;
            var service = CreateService();

            var waiter = service.WaitUntilInitializedAsync();
            var init = service.InitializeAsync();
            await waiter;

            Assert.IsTrue(service.IsInitialized);
            Assert.IsTrue((await init).Success);
        });

        [UnityTest]
        public IEnumerator Dispose_RejectsFurtherOperations() => UniTask.ToCoroutine(async () =>
        {
            var service = CreateService();
            await service.InitializeAsync();
            service.Dispose();

            var purchase = await service.PurchaseAsync(Coins);
            var init = await service.InitializeAsync();

            Assert.IsFalse(purchase.Success);
            Assert.IsFalse(init.Success);
        });

        private sealed class RejectingValidator : IPurchaseValidator
        {
            public UniTask<bool> ValidateAsync(StoreProduct product, string receipt, CancellationToken cancellationToken)
                => UniTask.FromResult(false);
        }

        private sealed class RecordingAnalytics : StoreAnalyticsListenerBase
        {
            public readonly List<string> Calls = new List<string>();

            public override void OnStoreInitializeStarted() => Calls.Add("init_started");
            public override void OnStoreInitialized(StoreInitializeResult result) => Calls.Add("initialized");
            public override void OnPurchaseStarted(StoreProduct product) => Calls.Add("purchase_started");
            public override void OnPurchaseCompleted(PurchaseResult result) => Calls.Add("purchase_completed");
            public override void OnPurchaseFailed(PurchaseResult result) => Calls.Add("purchase_failed");
        }

        private sealed class ThrowingAnalytics : StoreAnalyticsListenerBase
        {
            public override void OnPurchaseCompleted(PurchaseResult result) => throw new System.InvalidOperationException("boom");
            public override void OnStoreInitialized(StoreInitializeResult result) => throw new System.InvalidOperationException("boom");
        }
    }
}
