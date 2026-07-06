using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace StoreKit.Tests
{
    /// <summary>Tests for the script-driven configuration API (StoreKitSettings.Builder / script catalog methods).</summary>
    public sealed class StoreKitSettingsBuilderTests
    {
        [Test]
        public void Builder_BuildsCatalogAndOptions()
        {
            var settings = StoreKitSettings.Builder()
                .AddConsumable("s.coins", simulatedPrice: "$1.99")
                .AddNonConsumable("s.noads", appleId: "s.ios.noads", googleId: "s.android.noads")
                .AddSubscription("s.vip")
                .WithPopups(success: false, cancelled: true)
                .DisablePopups()
                .WithInitializationRetry(maxRetries: 5, baseDelaySeconds: 1.5f)
                .WithSimulation(o => o.purchaseDelaySeconds = 0f)
                .WithVerboseLogging()
                .Build();

            Assert.AreEqual(3, settings.products.Count);
            Assert.AreEqual(StoreProductType.Consumable, settings.FindProduct("s.coins").type);
            Assert.AreEqual("$1.99", settings.FindProduct("s.coins").simulatedPrice);
            Assert.AreEqual("s.ios.noads", settings.FindProduct("s.noads").appleId);
            Assert.AreEqual("s.android.noads", settings.FindProduct("s.noads").googleId);
            Assert.AreEqual(StoreProductType.Subscription, settings.FindProduct("s.vip").type);
            Assert.IsFalse(settings.showSuccessPopup);
            Assert.IsTrue(settings.showCancelledPopup);
            Assert.IsFalse(settings.enableDefaultPopups);
            Assert.IsTrue(settings.autoRetryInitialization);
            Assert.AreEqual(5, settings.maxInitializationRetries);
            Assert.AreEqual(1.5f, settings.initializationRetryDelaySeconds);
            Assert.AreEqual(0f, settings.simulation.purchaseDelaySeconds);
            Assert.IsTrue(settings.verboseLogging);

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Builder_FromBaseSettings_ClonesWithoutModifyingOriginal()
        {
            var original = ScriptableObject.CreateInstance<StoreKitSettings>();
            original.AddProduct("base.product", StoreProductType.Consumable);

            var overridden = StoreKitSettings.Builder(original)
                .AddConsumable("script.product")
                .RemoveProduct("base.product")
                .Build();

            Assert.AreNotSame(original, overridden);
            Assert.AreEqual(1, original.products.Count, "The original asset must not be modified.");
            Assert.AreEqual(1, overridden.products.Count);
            Assert.NotNull(overridden.FindProduct("script.product"));
            Assert.IsNull(overridden.FindProduct("base.product"));

            Object.DestroyImmediate(original);
            Object.DestroyImmediate(overridden);
        }

        [Test]
        public void AddProduct_ReplacesExistingId_AndIgnoresEmptyIds()
        {
            var settings = StoreKitSettings.Create();
            settings.AddProduct("p.one", StoreProductType.Consumable);
            settings.AddProduct("p.one", StoreProductType.NonConsumable); // replace, not duplicate
            settings.AddProduct(new StoreProductDefinition(null, StoreProductType.Consumable)); // ignored

            Assert.AreEqual(1, settings.products.Count);
            Assert.AreEqual(StoreProductType.NonConsumable, settings.FindProduct("p.one").type);

            Object.DestroyImmediate(settings);
        }

        [UnityTest]
        public IEnumerator BuilderSettings_DriveAFullPurchaseFlow() => UniTask.ToCoroutine(async () =>
        {
            var settings = StoreKitSettings.Builder()
                .AddConsumable("flow.coins", simulatedTitle: "Coins", simulatedPrice: "$2.49")
                .DisablePopups()
                .WithoutInitializationRetry()
                .WithSimulation(o =>
                {
                    o.askForConfirmation = false;
                    o.initializeDelaySeconds = 0f;
                    o.purchaseDelaySeconds = 0f;
                    o.persistOwnership = false;
                })
                .Build();

            var service = new StoreService(settings, new SimulatedStoreGateway());
            try
            {
                var init = await service.InitializeAsync();
                Assert.IsTrue(init.Success);

                var product = service.GetProduct("flow.coins");
                Assert.AreEqual("Coins", product.Title);
                Assert.AreEqual("$2.49", product.PriceString);

                var purchase = await service.PurchaseAsync("flow.coins");
                Assert.IsTrue(purchase.Success);
            }
            finally
            {
                service.Dispose();
                Object.DestroyImmediate(settings);
            }
        });
    }
}
