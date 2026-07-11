// VContainer integration. Compiles only when jp.hadashikick.vcontainer is installed
// (STOREKIT_HAS_VCONTAINER is set automatically via the asmdef version define). If you vendor
// VContainer outside UPM, add the STOREKIT_VCONTAINER scripting define instead.
#if STOREKIT_HAS_VCONTAINER || STOREKIT_VCONTAINER
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace StoreKit.DI
{
    /// <summary>
    /// VContainer registration for StoreKit. In your <c>LifetimeScope.Configure</c>:
    /// <code>
    /// protected override void Configure(IContainerBuilder builder)
    /// {
    ///     builder.RegisterStoreKit(mySettings); // settings optional — falls back to Resources
    /// }
    /// </code>
    /// Then inject <see cref="IStoreService"/> anywhere. Optional bindings picked up from the
    /// container when registered: <see cref="IStoreGateway"/>, <see cref="IStorePopupPresenter"/>,
    /// <see cref="IPurchaseValidator"/>.
    /// </summary>
    public static class StoreKitVContainerExtensions
    {
        /// <summary>
        /// Registers <see cref="IStoreService"/> as a singleton.
        /// </summary>
        /// <param name="settings">Explicit settings; when null the default asset is loaded from Resources.</param>
        /// <param name="autoInitialize">Initialize the store automatically when the scope starts (entry point).</param>
        /// <param name="syncStaticFacade">
        /// Also install the resolved service into the static <see cref="Store"/> facade for the
        /// scope's lifetime, so facade-based code (e.g. <see cref="ProductControllerBase"/>) uses
        /// the container-managed service. Reset automatically when the scope is disposed.
        /// </param>
        public static void RegisterStoreKit(this IContainerBuilder builder, StoreKitSettings settings = null,
            bool autoInitialize = true, bool syncStaticFacade = true)
        {
            builder.Register<IStoreService>(resolver =>
            {
                var service = new StoreService(
                    settings != null ? settings : StoreKitSettings.LoadDefault(),
                    Optional<IStoreGateway>(resolver),
                    Optional<IStorePopupPresenter>(resolver));

                var validator = Optional<IPurchaseValidator>(resolver);
                if (validator != null)
                {
                    service.Validator = validator;
                }

                return service;
            }, Lifetime.Singleton);

            if (autoInitialize)
            {
                builder.RegisterEntryPoint<StoreKitVContainerInitializer>();
            }

            if (syncStaticFacade)
            {
                builder.Register<StoreFacadeBinding>(Lifetime.Singleton);
                builder.RegisterBuildCallback(resolver => resolver.Resolve<StoreFacadeBinding>());
            }
        }

        /// <summary>
        /// Script-driven registration: builds settings fluently (starting from the default
        /// Resources asset when one exists, cloned) and registers <see cref="IStoreService"/>.
        /// </summary>
        public static void RegisterStoreKit(this IContainerBuilder builder, Action<StoreKitSettingsBuilder> configure,
            bool autoInitialize = true, bool syncStaticFacade = true)
        {
            var settingsBuilder = new StoreKitSettingsBuilder(StoreKitSettings.LoadDefault());
            configure?.Invoke(settingsBuilder);
            builder.RegisterStoreKit(settingsBuilder.Build(), autoInitialize, syncStaticFacade);
        }

        private static T Optional<T>(IObjectResolver resolver) where T : class
            => resolver.TryResolve<T>(out var value) ? value : null;
    }

    /// <summary>Entry point that kicks off store initialization when the scope starts.</summary>
    public sealed class StoreKitVContainerInitializer : IStartable, IDisposable
    {
        private readonly IStoreService _service;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public StoreKitVContainerInitializer(IStoreService service)
        {
            _service = service;
        }

        public void Start() => _service.InitializeAsync(_cts.Token).Forget();

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
#endif
