# StoreKit — Unity IAP Wrapper

AAA-quality, reliable wrapper around Unity IAP (`com.unity.purchasing`) for mobile games.

- **UniTask async API** — `await Store.PurchaseAsync("id")`; no callbacks, no listeners to implement.
- **Works in the Editor** — a fully simulated store (Edit Mode + Play Mode) with configurable delays, forced failures, confirmation prompts, and persisted ownership. No device, store account, or IAP setup needed to develop.
- **All common store operations** — product catalog + localized metadata, purchases (consumable / non-consumable / subscription), restore (iOS), deferred purchases (Apple "Ask to Buy"), out-of-band purchase delivery.
- **Simple configuration** — one `StoreKitSettings` ScriptableObject (catalog, popups, retry policy, simulation), or configure everything **from script** with a fluent builder (or mix: asset + script overrides).
- **Game-flow & analytics hooks** — C# events for flow control, `IStoreAnalyticsListener` for analytics fan-out.
- **Default device popups** — success / failure / cancelled / deferred / restore popups out of the box, individually toggleable, fully overridable via `IStorePopupPresenter`.
- **Extendable controllers** — `StoreControllerBase` and `ProductControllerBase` MonoBehaviours to drop into any game.
- **DI-ready** — optional first-class integrations for **VContainer** and **Zenject/Extenject**; `StoreService` is `IDisposable` and scope-friendly.
- **Reliability built in** — initialization retry with exponential backoff, pending-transaction-safe receipt validation, purchase re-entrancy guards, exception-isolated event/analytics handlers, out-of-band (restored/deferred/promo) purchase routing.

## Installation

1. Add the package via git URL (or embed it in `Packages/`):
   ```json
   "com.kobic.storekit": "https://github.com/Kobapps/StoreKit.git?path=Packages/com.kobic.storekit"
   ```
2. **UniTask** is required and must be added to your project manifest (git dependencies can't be declared by packages):
   ```json
   "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"
   ```
3. `com.unity.ugui` resolves automatically (used by the default popups).
4. **Unity IAP is optional and unpinned** — add it yourself when you want real device purchasing, at whatever version you choose:
   ```json
   "com.unity.purchasing": "4.15.1"
   ```

### Unity IAP versioning

StoreKit deliberately does **not** declare `com.unity.purchasing` as a dependency, so it never locks your project to a specific (now-deprecated) IAP version. The device gateway targets the **Unity IAP 4.x** line (`[4.8, 5.0)`), selected via an asmdef version define:

- **Supported IAP 4.x installed** → real Unity IAP is used on device.
- **No IAP, or IAP 5 installed** → the package compiles cleanly and uses the **simulated store** (IAP 5 support is planned; the 4.x line still works today).

So you can build and test the entire store flow with no Unity IAP installed, then add it for device builds.

## Quick start

1. **Create settings**: `Tools > StoreKit > Create Settings Asset` (creates `Assets/Resources/StoreKitSettings.asset`).
2. **Add products** to the catalog (id + type; optional per-store id overrides and simulated price).
3. **Purchase**:

```csharp
using StoreKit;

await Store.InitializeAsync();

var result = await Store.PurchaseAsync("com.mygame.coins_100");
if (result.Success)
{
    GrantCoins(100);
}
```

That's it — in the Editor you get the simulated store with a confirmation popup; on device you get real Unity IAP. Default popups inform the player about success/failure automatically.

### Configure from script (no asset needed)

Everything the settings asset does can be driven from code — useful for remote-config-driven catalogs, per-environment setups, or teams that prefer code over assets:

```csharp
Store.Configure(store => store
    .AddConsumable("com.mygame.coins_100")
    .AddNonConsumable("com.mygame.no_ads", appleId: "com.mygame.ios.no_ads")
    .AddSubscription("com.mygame.vip")
    .WithPopups(cancelled: true)                      // per-kind toggles
    .WithPopupTexts(t => t.purchaseSuccessTitle = "Thanks!") // e.g. localization
    .WithInitializationRetry(maxRetries: 5, baseDelaySeconds: 1f)
    .WithSimulation(o => o.purchaseDelaySeconds = 0.2f));

await Store.InitializeAsync();
```

`Store.Configure(builder => ...)` starts from the default Resources asset when one exists (as a **clone** — the asset is never modified) and from empty settings otherwise. To build settings without installing them, use `StoreKitSettings.Builder()` / `StoreKitSettings.Builder(baseAsset)` and pass the result anywhere settings are accepted (`Store.Configure`, `new StoreService(...)`, `RegisterStoreKit`, `BindStoreKit`).

The catalog can also be edited directly on any settings instance: `settings.AddProduct(id, type, ...)` (replaces by id), `settings.RemoveProduct(id)`, `settings.SetProducts(list)`. Configure **before** `InitializeAsync` — the product catalog is sent to the store during initialization. Popup toggles and texts can be changed at any time.

### Restore (iOS)

```csharp
var restore = await Store.RestorePurchasesAsync();
// Each restored purchase is also delivered via Store.PurchaseCompleted with IsRestored == true.
```

Apple requires a visible "Restore Purchases" button for apps with non-consumables. On Google Play restore happens automatically at initialization; the call simply reports success.

## Controllers — connect to any game

Extend the base controllers instead of talking to `Store` directly:

```csharp
public class MyStoreController : StoreControllerBase
{
    protected override void GrantPurchase(PurchaseResult result)
    {
        // Called for EVERY successful purchase, including restored/out-of-band ones.
        // Must be idempotent for non-consumables.
        Economy.Grant(result.ProductId);
    }

    protected override void OnPurchaseFailed(PurchaseResult result)
    {
        // Optional: connect to your game flow (screens, toasts, retry offers...).
    }
}
```

```csharp
public class MyShopButton : ProductControllerBase
{
    protected override void OnProductUpdated(StoreProduct product)
    {
        priceLabel.text = product?.PriceString ?? "...";
        button.interactable = product is { AvailableToPurchase: true } && !IsOwned;
    }
}
```

Import the **Basic Store** sample (Package Manager > StoreKit > Samples) for complete examples.

## Popups

Defaults are enabled per kind in `StoreKitSettings` (`showSuccessPopup`, `showFailurePopup`, `showCancelledPopup`, `showDeferredPopup`, `showRestorePopup`), with all texts editable in `popupTexts`.

- **Disable all**: turn off `enableDefaultPopups`.
- **Override with your own UI**:

```csharp
public class MyPopups : IStorePopupPresenter
{
    public async UniTask<bool> ShowAsync(StorePopupRequest request, CancellationToken ct)
    {
        return await MyDialogSystem.ShowAsync(request.Title, request.Message, request.ConfirmText, request.CancelText);
    }
}

Store.PopupPresenter = new MyPopups();
```

## Analytics

```csharp
public class MyAnalytics : StoreAnalyticsListenerBase
{
    public override void OnPurchaseCompleted(PurchaseResult result)
        => Analytics.Track("iap_success", result.ProductId, result.Product?.Price);

    public override void OnPurchaseFailed(PurchaseResult result)
        => Analytics.Track("iap_fail", result.ProductId, result.Failure.ToString());
}

Store.AddAnalyticsListener(new MyAnalytics());
```

Listener exceptions are caught and logged — they can never break a purchase.

## Receipt validation

Plug in local or server-side validation; the store transaction stays **pending** until your validator finishes, so purchases are never lost mid-validation:

```csharp
public class ServerValidator : IPurchaseValidator
{
    public async UniTask<bool> ValidateAsync(StoreProduct product, string receipt, CancellationToken ct)
        => await MyBackend.ValidateReceiptAsync(product.Id, receipt, ct);
}

Store.Validator = new ServerValidator();
```

## Editor simulation

Configured under `StoreKitSettings > Simulation`: init/purchase delays, forced init/purchase failures (pick the failure reason), a Buy/Cancel confirmation popup, and persisted ownership. Reset owned products via `Tools > StoreKit > Clear Simulated Purchases`. Uncheck `useSimulatedStoreInEditor` to exercise Unity IAP's own fake store in Play Mode instead.

## Dependency injection (VContainer / Zenject)

Optional integrations live in their own assemblies and compile only when the DI framework is installed — no extra dependency otherwise.

### VContainer (`jp.hadashikick.vcontainer`)

```csharp
using StoreKit.DI;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterStoreKit(); // or RegisterStoreKit(settings, autoInitialize: true, syncStaticFacade: true)
    }
}
```

### Zenject / Extenject (`com.svermeulen.extenject`)

Add `StoreKitMonoInstaller` to your SceneContext/ProjectContext, or from another installer:

```csharp
using StoreKit.DI;

public override void InstallBindings()
{
    Container.BindStoreKit(); // or BindStoreKit(settings, autoInitialize: true, syncStaticFacade: true)
    // or: StoreKitInstaller.Install(Container);
}
```

For a vendored (non-UPM) Zenject that still ships the `Zenject` asmdef, add the `STOREKIT_ZENJECT` scripting define.

### Behavior (both frameworks)

- `IStoreService` is bound as a **singleton**; inject it anywhere. The container disposes it with the scope (`StoreService : IDisposable`).
- Optional container bindings are picked up automatically when registered: `IStoreGateway` (custom backend/test double), `IStorePopupPresenter` (your popup UI), `IPurchaseValidator` (receipt validation).
- `autoInitialize` (default on) starts store initialization when the scope starts.
- `syncStaticFacade` (default on) installs the container's service into the static `Store` facade for the scope's lifetime — so `ProductControllerBase` buttons and other facade-based code use the same instance. The facade resets automatically when the scope is disposed.

## Advanced

- `Store.Configure(settings, gateway, popupPresenter)` — explicit boot; pass a custom `IStoreGateway` to plug in another store backend or a test double.
- `new StoreService(...)` — instance-based use (tests, multiple catalogs) without the static facade. Dispose it when done.
- `Store.WaitUntilInitializedAsync()` — await store readiness from game flows without triggering initialization; `Store.ServiceOrNull` — null-safe facade access.
- Events: `Initialized`, `PurchaseStarted`, `PurchaseCompleted`, `PurchaseFailed`, `PurchaseDeferred`, `RestoreCompleted`.
- `PurchaseResult` carries the transaction id, raw receipt, product snapshot, and `IsRestored`.

## Reliability notes

- Initialization retries transient failures with exponential backoff (configurable).
- Concurrent `InitializeAsync` calls share one initialization; concurrent purchases are rejected with `PurchaseFailure.PurchaseInProgress` (double-tap safe).
- Deferred purchases (Ask to Buy) resolve the awaiting `PurchaseAsync` with `PurchaseFailure.Deferred`; when approved later, the purchase arrives through `PurchaseCompleted`.
- All results are returned as values — store operations never throw for store-side failures.

## Tests

Edit-mode tests live under `Tests/Editor` and run against the simulated gateway (Window > General > Test Runner). The project manifest lists the package under `testables`.
