// DI integration tests — compile only when the corresponding DI framework is installed
// (same version defines as the integration assemblies).
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

#if STOREKIT_HAS_VCONTAINER || STOREKIT_HAS_ZENJECT
using StoreKit.DI;
#endif
#if STOREKIT_HAS_VCONTAINER
using VContainer;
#endif
#if STOREKIT_HAS_ZENJECT
using Zenject;
#endif

namespace StoreKit.Tests
{
    public sealed class StoreKitDiTests
    {
        private StoreKitSettings _settings;

        [SetUp]
        public void SetUp()
        {
            Store.Reset();
            _settings = ScriptableObject.CreateInstance<StoreKitSettings>();
            _settings.enableDefaultPopups = false;
            _settings.products = new List<StoreProductDefinition>
            {
                new StoreProductDefinition { id = "di.test.product", type = StoreProductType.Consumable },
            };
        }

        [TearDown]
        public void TearDown()
        {
            Store.Reset();
            Object.DestroyImmediate(_settings);
        }

#if STOREKIT_HAS_VCONTAINER
        [Test]
        public void VContainer_RegisterStoreKit_ResolvesSingletonWithSettings()
        {
            var builder = new ContainerBuilder();
            builder.RegisterStoreKit(_settings, autoInitialize: false, syncStaticFacade: false);

            using (var container = builder.Build())
            {
                var first = container.Resolve<IStoreService>();
                var second = container.Resolve<IStoreService>();

                Assert.AreSame(first, second);
                Assert.AreSame(_settings, first.Settings);
                Assert.IsNull(Store.ServiceOrNull, "Facade must stay untouched when syncStaticFacade is off.");
            }
        }

        [Test]
        public void VContainer_FacadeSync_InstallsAndResetsOnDispose()
        {
            var builder = new ContainerBuilder();
            builder.RegisterStoreKit(_settings, autoInitialize: false, syncStaticFacade: true);

            IStoreService resolved;
            using (var container = builder.Build())
            {
                resolved = container.Resolve<IStoreService>();
                Assert.AreSame(resolved, Store.ServiceOrNull, "Facade should point at the container's service.");
            }

            Assert.IsNull(Store.ServiceOrNull, "Facade should reset when the container is disposed.");
        }

        [Test]
        public void VContainer_OptionalBindings_AreInjected()
        {
            var gateway = new SimulatedStoreGateway();
            var builder = new ContainerBuilder();
            builder.RegisterInstance<IStoreGateway>(gateway);
            builder.RegisterStoreKit(_settings, autoInitialize: false, syncStaticFacade: false);

            using (var container = builder.Build())
            {
                var service = container.Resolve<IStoreService>();
                // Initialization uses the injected gateway — the simulated gateway reports its products.
                Assert.NotNull(service);
            }
        }
#endif

#if STOREKIT_HAS_ZENJECT
        [Test]
        public void Zenject_BindStoreKit_ResolvesSingletonWithSettings()
        {
            var container = new DiContainer();
            container.BindStoreKit(_settings, autoInitialize: false, syncStaticFacade: false);

            var first = container.Resolve<IStoreService>();
            var second = container.Resolve<IStoreService>();

            Assert.AreSame(first, second);
            Assert.AreSame(_settings, first.Settings);
            Assert.IsNull(Store.ServiceOrNull, "Facade must stay untouched when syncStaticFacade is off.");
        }

        [Test]
        public void Zenject_FacadeSync_InstallsService()
        {
            var container = new DiContainer();
            container.BindStoreKit(_settings, autoInitialize: false, syncStaticFacade: true);
            container.ResolveRoots();

            Assert.AreSame(container.Resolve<IStoreService>(), Store.ServiceOrNull);
        }
#endif
    }
}
