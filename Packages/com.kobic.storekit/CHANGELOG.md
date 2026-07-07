# Changelog

All notable changes to this package are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.3.0] - 2026-07-07

### Changed
- **Unity IAP is now an optional, unpinned dependency.** `com.unity.purchasing` is no longer declared in `package.json`, so the package never forces a specific (now-deprecated) Unity IAP version onto consumers. Add Unity IAP to your own project manifest at the version you want; the package uses its simulated store when IAP is absent.
- The `STOREKIT_HAS_IAP` version define is bounded to the supported gateway range `[4.8, 5.0)` (was `>= 4.8.0`). This means installing **Unity IAP 5** no longer pulls the IAP 4.x gateway into compilation (which would fail) — the package compiles cleanly and falls back to the simulated store until dedicated IAP 5 support ships. IAP 4.8–4.x continues to use the real store on device.
- Editor "no IAP" inspector hint clarified for the 4.x-vs-5 situation.

## [1.2.0] - 2026-07-06

### Added
- **Script-driven configuration** — everything the settings asset does can now be done from code:
  - `StoreKitSettingsBuilder` fluent API (`StoreKitSettings.Builder()` / `Builder(baseAsset)`): catalog (`AddConsumable` / `AddNonConsumable` / `AddSubscription` / `AddProduct` / `SetProducts` / `RemoveProduct`), popup toggles (`WithPopups`, `EnableDefaultPopups`, `DisablePopups`) and texts (`WithPopupTexts`), retry policy (`WithInitializationRetry` / `WithoutInitializationRetry`), Editor simulation (`WithSimulation`, `UseSimulatedStoreInEditor`), and logging (`WithVerboseLogging`).
  - `Store.Configure(Action<StoreKitSettingsBuilder>)` overload — starts from a clone of the default Resources asset when one exists (the asset itself is never modified).
  - Matching DI overloads: `builder.RegisterStoreKit(store => ...)` (VContainer) and `Container.BindStoreKit(store => ...)` (Zenject).
  - Catalog methods directly on `StoreKitSettings` (`AddProduct` replaces by id, `RemoveProduct`, `SetProducts`), a script-friendly `StoreProductDefinition` constructor, and `StoreKitSettings.Create()` for runtime instances.
- Builder/catalog test coverage, including a full purchase flow driven by script-built settings.

## [1.1.0] - 2026-07-06

### Added
- **Dependency injection integrations** (optional, compile-gated):
  - **VContainer** — `builder.RegisterStoreKit(settings)` (`StoreKit.VContainer` assembly, enabled automatically when `jp.hadashikick.vcontainer` is installed).
  - **Zenject / Extenject** — `Container.BindStoreKit(settings)`, `StoreKitInstaller`, and a `StoreKitMonoInstaller` (`StoreKit.Zenject` assembly, enabled automatically for `com.svermeulen.extenject`; add the `STOREKIT_ZENJECT` define for vendored Zenject installs).
  - Both resolve optional `IStoreGateway` / `IStorePopupPresenter` / `IPurchaseValidator` bindings from the container, can auto-initialize the store on scope start, and can sync the static `Store` facade for the scope's lifetime (with automatic reset on scope disposal via `StoreFacadeBinding`).
- `IStoreService.WaitUntilInitializedAsync` / `Store.WaitUntilInitializedAsync` for game flows that need to await store readiness without triggering initialization.
- `Store.ServiceOrNull` — null-safe facade accessor that never lazily creates a service.
- DI integration tests (compile only when the corresponding framework is installed).

### Changed
- `StoreService` now implements `IDisposable`: disposal unhooks gateway events, drops analytics listeners, and cancels in-flight popups. DI containers dispose it automatically with their scope.
- `StoreControllerBase` no longer replaces an already-configured service when a settings asset is assigned in the inspector (prevents clobbering DI-installed services); it warns instead.
- Popups launched by the service are now tied to the service lifetime and cancelled on disposal.

### Fixed
- Concurrent `InitializeAsync` callers now receive the configuration-error result instead of a default value when settings are missing.

## [1.0.0] - 2026-07-06

### Added
- UniTask-based async store API (`Store` facade + `StoreService`).
- Unity IAP gateway with UGS bootstrap, per-store product id overrides, pending-safe receipt validation, Apple restore, and Ask-to-Buy deferral handling.
- Simulated store gateway for Editor / no-IAP development and automated tests.
- `StoreKitSettings` ScriptableObject configuration (catalog, popups, retry policy, simulation).
- Default device popups (success/failure/cancelled/deferred/restore), toggleable and overridable via `IStorePopupPresenter`.
- Analytics fan-out via `IStoreAnalyticsListener`.
- Extendable `StoreControllerBase` and `ProductControllerBase` MonoBehaviours.
- Edit-mode test suite covering initialization, purchases, failures, restore, events, and analytics.
- Basic Store sample.
