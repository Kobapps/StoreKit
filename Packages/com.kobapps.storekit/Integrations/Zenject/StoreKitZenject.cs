// Zenject / Extenject integration. Compiles when com.svermeulen.extenject is installed via UPM
// (STOREKIT_HAS_ZENJECT is set automatically via the asmdef version define). For Asset Store /
// vendored Zenject installs (which still ship the "Zenject" asmdef), add the STOREKIT_ZENJECT
// scripting define to enable this assembly.
#if STOREKIT_HAS_ZENJECT || STOREKIT_ZENJECT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace StoreKit.DI
{
    /// <summary>
    /// Zenject bindings for StoreKit. Either add <see cref="StoreKitMonoInstaller"/> to your
    /// SceneContext/ProjectContext, install <see cref="StoreKitInstaller"/> from another
    /// installer, or call <c>Container.BindStoreKit(settings)</c> directly.
    /// Then inject <see cref="IStoreService"/> anywhere. Optional bindings picked up from the
    /// container when bound: <see cref="IStoreGateway"/>, <see cref="IStorePopupPresenter"/>,
    /// <see cref="IPurchaseValidator"/>.
    /// </summary>
    public static class StoreKitZenjectExtensions
    {
        /// <summary>
        /// Binds <see cref="IStoreService"/> as a singleton.
        /// </summary>
        /// <param name="settings">Explicit settings; when null the default asset is loaded from Resources.</param>
        /// <param name="autoInitialize">Initialize the store automatically when the kernel starts (IInitializable).</param>
        /// <param name="syncStaticFacade">
        /// Also install the resolved service into the static <see cref="Store"/> facade for the
        /// container's lifetime, so facade-based code (e.g. <see cref="ProductControllerBase"/>)
        /// uses the container-managed service. Reset automatically when the container is disposed.
        /// </param>
        public static void BindStoreKit(this DiContainer container, StoreKitSettings settings = null,
            bool autoInitialize = true, bool syncStaticFacade = true)
        {
            container.Bind<IStoreService>()
                .FromMethod(ctx =>
                {
                    var service = new StoreService(
                        settings != null ? settings : StoreKitSettings.LoadDefault(),
                        ctx.Container.TryResolve<IStoreGateway>(),
                        ctx.Container.TryResolve<IStorePopupPresenter>());

                    var validator = ctx.Container.TryResolve<IPurchaseValidator>();
                    if (validator != null)
                    {
                        service.Validator = validator;
                    }

                    return (IStoreService)service;
                })
                .AsSingle();

            if (autoInitialize)
            {
                container.BindInterfacesTo<StoreKitZenjectInitializer>().AsSingle().NonLazy();
            }

            if (syncStaticFacade)
            {
                container.BindInterfacesAndSelfTo<StoreFacadeBinding>().AsSingle().NonLazy();
            }
        }

        /// <summary>
        /// Script-driven binding: builds settings fluently (starting from the default Resources
        /// asset when one exists, cloned) and binds <see cref="IStoreService"/>.
        /// </summary>
        public static void BindStoreKit(this DiContainer container, Action<StoreKitSettingsBuilder> configure,
            bool autoInitialize = true, bool syncStaticFacade = true)
        {
            var settingsBuilder = new StoreKitSettingsBuilder(StoreKitSettings.LoadDefault());
            configure?.Invoke(settingsBuilder);
            container.BindStoreKit(settingsBuilder.Build(), autoInitialize, syncStaticFacade);
        }
    }

    /// <summary>Kicks off store initialization when the Zenject kernel starts.</summary>
    public sealed class StoreKitZenjectInitializer : IInitializable, IDisposable
    {
        private readonly IStoreService _service;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public StoreKitZenjectInitializer(IStoreService service)
        {
            _service = service;
        }

        public void Initialize() => _service.InitializeAsync(_cts.Token).Forget();

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    /// <summary>Plain installer variant — <c>StoreKitInstaller.Install(Container)</c> with default options.</summary>
    public sealed class StoreKitInstaller : Installer<StoreKitInstaller>
    {
        public override void InstallBindings() => Container.BindStoreKit();
    }

    /// <summary>
    /// Drop-in MonoInstaller for SceneContext/ProjectContext with inspector-configurable options.
    /// </summary>
    public sealed class StoreKitMonoInstaller : MonoInstaller
    {
        [SerializeField]
        [Tooltip("Optional explicit settings. When empty, the default StoreKitSettings asset from Resources is used.")]
        private StoreKitSettings _settings;

        [SerializeField]
        [Tooltip("Initialize the store automatically when the container starts.")]
        private bool _autoInitialize = true;

        [SerializeField]
        [Tooltip("Install the service into the static Store facade for the container's lifetime.")]
        private bool _syncStaticFacade = true;

        public override void InstallBindings()
            => Container.BindStoreKit(_settings, _autoInitialize, _syncStaticFacade);
    }
}
#endif
