# StoreKit — Unity IAP Wrapper

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](Packages/com.kobic.storekit/LICENSE.md)

AAA-quality, reliable wrapper around Unity IAP (`com.unity.purchasing`) for mobile games, with a UniTask async API, a fully simulated store for Editor development, default (overridable) device popups, analytics hooks, DI integrations (VContainer / Zenject), and drop-in extendable controllers.

```csharp
await Store.InitializeAsync();

var result = await Store.PurchaseAsync("com.mygame.coins_100");
if (result.Success)
{
    GrantCoins(100);
}
```

This repository contains:

- **The package** — [`Packages/com.kobic.storekit`](Packages/com.kobic.storekit) (see its [README](Packages/com.kobic.storekit/README.md) for full documentation).
- **A Unity 6 development project** hosting the package and its edit-mode test suite.

## Installation (into your own project)

**1. Add UniTask** (required; git dependencies can't be declared by packages) to `Packages/manifest.json`:

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"
```

**2. Add StoreKit** via git URL:

```json
"com.kobic.storekit": "https://github.com/Kobapps/StoreKit.git?path=Packages/com.kobic.storekit"
```

(Or in the Package Manager: `+` → *Install package from git URL…* → `https://github.com/Kobapps/StoreKit.git?path=Packages/com.kobic.storekit`)

`com.unity.purchasing` (Unity IAP) and `com.unity.ugui` resolve automatically as package dependencies. On Unity 6, IAP resolves to 4.15+.

> The package also compiles **without** Unity IAP — it then always uses the simulated store, which is handy in early development.

## Quick start

1. `Tools > StoreKit > Create Settings Asset` (creates `Assets/Resources/StoreKitSettings.asset`), add your products — **or** configure everything from script:

   ```csharp
   Store.Configure(store => store
       .AddConsumable("com.mygame.coins_100")
       .AddNonConsumable("com.mygame.no_ads")
       .AddSubscription("com.mygame.vip"));
   ```

2. `await Store.InitializeAsync();` then `await Store.PurchaseAsync("id");` — in the Editor you get the simulated store (with confirmation/result popups); on device, real Unity IAP.
3. For non-consumables on iOS, expose a Restore button calling `Store.RestorePurchasesAsync()`.
4. Prefer dropping in MonoBehaviours? Extend `StoreControllerBase` (grant purchases, hook game flow) and `ProductControllerBase` (shop buttons) — see the **Basic Store** sample in the Package Manager.

## Highlights

| Area | What you get |
|---|---|
| Async API | `UniTask`-based; results as values, never store-side exceptions |
| Editor development | Simulated store: delays, forced failures, Buy/Cancel prompt, persisted ownership |
| Store operations | Products + localized metadata, purchase (all types), restore (iOS), deferred purchases (Ask to Buy), out-of-band delivery |
| Popups | Default device popups per event kind — toggleable, text-overridable, replaceable via `IStorePopupPresenter` |
| Analytics | `IStoreAnalyticsListener` fan-out, exception-isolated |
| Validation | `IPurchaseValidator` hook; transactions stay pending until validation completes |
| DI | `builder.RegisterStoreKit(...)` (VContainer), `Container.BindStoreKit(...)` (Zenject/Extenject) — optional, compile-gated |
| Reliability | Init retry with backoff, double-tap guards, idempotent-grant flow, disposal-safe services |

Full documentation: [package README](Packages/com.kobic.storekit/README.md) · [changelog](Packages/com.kobic.storekit/CHANGELOG.md)

## Working on this repository

- Open the repo root with **Unity 6000.5+**. The package is embedded under `Packages/com.kobic.storekit` and editable in place.
- Tests: `Window > General > Test Runner > EditMode` — the suite runs against the simulated store gateway (no device or store account needed).

## License

MIT — see [LICENSE](Packages/com.kobic.storekit/LICENSE.md).
