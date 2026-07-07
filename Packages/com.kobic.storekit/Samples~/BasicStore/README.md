# Basic Store Sample

A ready-to-run demo scene plus example implementations of StoreKit's extendable controllers.

## Run the demo scene

1. Import this sample (Package Manager → StoreKit → Samples → **Basic Store** → Import).
2. Open **`BasicStore.unity`** (under `Assets/Samples/StoreKit/<version>/Basic Store/`).
3. Press **Play**.

The scene needs no setup: `StoreDemo` configures a demo catalog if your project has no
`StoreKitSettings`, initializes the store, and builds a runtime shop UI — one Buy button per
product, a Restore button, a live status line, and an event log. In the Editor it all runs against
the **simulated store**, so no device or store account is required. Buying shows StoreKit's default
confirmation and result popups; owned non-consumables show as "Owned".

## What's inside

| File | Role |
|---|---|
| `BasicStore.unity` | The demo scene: a `StoreController` object (`ExampleStoreController`) and a `StoreDemo` object that builds the shop UI at runtime. |
| `StoreDemo.cs` | Self-contained bootstrap that configures/initializes the store and builds the demo UI. Not something you ship — it shows the API end to end. |
| `DemoProductRow.cs` | A `ProductControllerBase` wired **from code** (used by `StoreDemo` for each row). |
| `ExampleStoreController.cs` | A `StoreControllerBase` — grants purchases (into PlayerPrefs here) and hooks game-flow events. This is the pattern to copy into your game. |
| `ExampleProductButton.cs` | A `ProductControllerBase` wired **from the inspector** (assign a Button + price/title Text). |

## Adapting to your game

- **Granting**: replace `ExampleStoreController.GrantPurchase` with your economy / save system. It's called for every successful purchase, including restored ones, so keep it idempotent for non-consumables.
- **Shop buttons**: for inspector-driven UI, put `ExampleProductButton` on a Button, set its product id, and assign the Button/Text references. For code-driven UI, follow `DemoProductRow`.
- **Products**: the demo uses `com.example.coins_100`, `com.example.no_ads`, and `com.example.vip`. Point these at your real product ids (via a `StoreKitSettings` asset or `Store.Configure(...)`), and update the grant logic to match.
