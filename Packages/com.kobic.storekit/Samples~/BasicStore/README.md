# Basic Store Sample

Two ready-to-adapt implementations of StoreKit's extendable controllers:

- **ExampleStoreController** (`StoreControllerBase`) — initializes the store on Start, grants purchases into PlayerPrefs (coins / no-ads), and logs every flow hook. Replace `GrantPurchase` with your game's economy or save system.
- **ExampleProductButton** (`ProductControllerBase`) — keeps a uGUI `Button` + price/title `Text` labels in sync with the store (price, availability, owned state, busy state) and triggers the purchase on click.

## Try it

1. Create settings: `Tools > StoreKit > Create Settings Asset`.
2. Add products with ids `com.example.coins_100` (Consumable) and `com.example.no_ads` (Non-Consumable).
3. Drop `ExampleStoreController` on any GameObject in a scene.
4. Add a uGUI Button with `ExampleProductButton`, set its product id, and assign the Button/Text references.
5. Press Play — the simulated store initializes and purchases work immediately in the Editor, complete with confirmation and result popups.
