using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoreKit.Samples
{
    /// <summary>
    /// Self-contained demo shop. Drop this on a GameObject (the Basic Store sample scene has one)
    /// and press Play: it configures a demo catalog if your project has none, initializes the
    /// store, and builds a runtime uGUI shop — one row per product (each row is a
    /// <see cref="DemoProductRow"/>, i.e. a <see cref="ProductControllerBase"/>), a Restore
    /// button, a live status line, and an event log. In the Editor everything runs against the
    /// simulated store, so no device or store account is needed.
    ///
    /// This is a demo: the UI is built in code to keep the scene tiny and robust. In a real game
    /// you would design the shop in the Editor and extend <see cref="ProductControllerBase"/> /
    /// <see cref="StoreControllerBase"/> (see <see cref="ExampleProductButton"/> and
    /// <see cref="ExampleStoreController"/>).
    /// </summary>
    public sealed class StoreDemo : MonoBehaviour
    {
        private const int MaxLogLines = 12;

        private static readonly Color PanelColor = new Color(0.12f, 0.13f, 0.17f, 1f);
        private static readonly Color RowColor = new Color(0.17f, 0.18f, 0.23f, 1f);
        private static readonly Color AccentColor = new Color(0.24f, 0.42f, 0.94f, 1f);
        private static readonly Color RestoreColor = new Color(0.28f, 0.30f, 0.36f, 1f);
        private static readonly Color TitleColor = Color.white;
        private static readonly Color MutedColor = new Color(0.72f, 0.76f, 0.84f, 1f);

        private readonly List<string> _log = new List<string>();

        private Text _statusLabel;
        private Text _logLabel;
        private Transform _productList;

        private void Awake()
        {
            // Make the sample runnable out of the box: if the project has no StoreKitSettings
            // (or an empty one), configure a demo catalog from script. If you created a settings
            // asset with products, that is used instead.
            var existing = StoreKitSettings.LoadDefault();
            if (existing == null || existing.products == null || existing.products.Count == 0)
            {
                Store.Configure(store => store
                    .AddConsumable("com.example.coins_100", simulatedTitle: "100 Coins", simulatedPrice: "$0.99")
                    .AddNonConsumable("com.example.no_ads", simulatedTitle: "Remove Ads", simulatedPrice: "$2.99")
                    .AddSubscription("com.example.vip", simulatedTitle: "VIP Membership", simulatedPrice: "$4.99")
                    .WithSimulation(o => o.persistOwnership = true));
            }
        }

        private void OnEnable()
        {
            Store.Initialized += OnInitialized;
            Store.PurchaseStarted += OnPurchaseStarted;
            Store.PurchaseCompleted += OnPurchaseCompleted;
            Store.PurchaseFailed += OnPurchaseFailed;
            Store.RestoreCompleted += OnRestoreCompleted;
        }

        private void OnDisable()
        {
            Store.Initialized -= OnInitialized;
            Store.PurchaseStarted -= OnPurchaseStarted;
            Store.PurchaseCompleted -= OnPurchaseCompleted;
            Store.PurchaseFailed -= OnPurchaseFailed;
            Store.RestoreCompleted -= OnRestoreCompleted;
        }

        private void Start() => RunAsync().Forget();

        private async UniTaskVoid RunAsync()
        {
            BuildUi();
            SetStatus("Initializing store…");
            AppendLog("Booting StoreKit demo…");

            var result = await Store.InitializeAsync(destroyCancellationToken);
            if (!result.Success)
            {
                SetStatus($"Initialization failed: {result.Status}");
                AppendLog($"Init failed: {result.Message}");
                return;
            }

            BuildProductRows();
            SetStatus($"Store ready — {Store.Products.Count} product(s). Tap a Buy button.");
        }

        // ---- Store events ----

        private void OnInitialized(StoreInitializeResult result) => AppendLog($"Initialized: {result.Status}");
        private void OnPurchaseStarted(StoreProduct product) => AppendLog($"Purchasing {product.Id}…");
        private void OnPurchaseCompleted(PurchaseResult result) => AppendLog($"✓ {result.ProductId}{(result.IsRestored ? " (restored)" : string.Empty)}");
        private void OnPurchaseFailed(PurchaseResult result) => AppendLog($"✗ {result.ProductId}: {result.Failure}");
        private void OnRestoreCompleted(RestoreResult result) => AppendLog($"Restore: {(result.Success ? $"{result.RestoredCount} restored" : result.Message)}");

        private void OnRestoreClicked() => Store.RestorePurchasesAsync(destroyCancellationToken).Forget();

        // ---- UI construction ----

        private void BuildUi()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("StoreDemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreateImage("Panel", canvasGo.transform, PanelColor);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(940f, 1500f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 40, 40);
            layout.spacing = 24;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText("Title", panel.transform, "StoreKit Demo Shop", 56, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
            _statusLabel = CreateText("Status", panel.transform, "…", 34, FontStyle.Normal, MutedColor, TextAnchor.MiddleCenter);

            var listGo = new GameObject("Products", typeof(RectTransform));
            listGo.transform.SetParent(panel.transform, false);
            var listLayout = listGo.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 16;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childForceExpandHeight = false;
            _productList = listGo.transform;

            var restoreButton = CreateButton(panel.transform, "Restore Purchases", RestoreColor, OnRestoreClicked, 90f);
            restoreButton.name = "RestoreButton";

            CreateText("LogTitle", panel.transform, "Event Log", 32, FontStyle.Bold, MutedColor, TextAnchor.MiddleLeft);
            var logBg = CreateImage("LogPanel", panel.transform, new Color(0.08f, 0.09f, 0.12f, 1f));
            logBg.gameObject.AddComponent<LayoutElement>().minHeight = 380f;
            _logLabel = CreateText("Log", logBg.transform, string.Empty, 28, FontStyle.Normal, MutedColor, TextAnchor.UpperLeft);
            var logPad = _logLabel.gameObject.AddComponent<LayoutElement>();
            logPad.ignoreLayout = false;
            Stretch(_logLabel.rectTransform, 20f);
        }

        private void BuildProductRows()
        {
            foreach (var product in Store.Products)
            {
                var rowImage = CreateImage($"Row_{product.Id}", _productList, RowColor);
                rowImage.gameObject.AddComponent<LayoutElement>().minHeight = 140f;

                var rowLayout = rowImage.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset(24, 24, 16, 16);
                rowLayout.spacing = 16;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;

                var titleLabel = CreateText("Title", rowImage.transform, product.Title, 34, FontStyle.Bold, TitleColor, TextAnchor.MiddleLeft);
                titleLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                var buyButton = CreateButton(rowImage.transform, "Buy", AccentColor, null, 0f);
                var buyLayout = buyButton.gameObject.AddComponent<LayoutElement>();
                buyLayout.minWidth = 260f;
                buyLayout.minHeight = 96f;
                var priceLabel = buyButton.GetComponentInChildren<Text>();

                // Wire the row up as a ProductControllerBase — this keeps price/owned/busy state
                // in sync with the store automatically.
                var row = rowImage.gameObject.AddComponent<DemoProductRow>();
                row.Bind(buyButton, priceLabel);
                row.ProductId = product.Id;
            }
        }

        // ---- log ----

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }

        private void AppendLog(string line)
        {
            _log.Add(line);
            if (_log.Count > MaxLogLines)
            {
                _log.RemoveRange(0, _log.Count - MaxLogLines);
            }

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _log);
            }
        }

        // ---- uGUI helpers (kept local so the sample has no extra dependencies) ----

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            // Prefer the new Input System UI module when the Input System package is present,
            // otherwise fall back to the legacy module. Resolved by reflection so the sample
            // needs no compile-time dependency on the Input System.
            var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
            }
            else
            {
                go.AddComponent<StandaloneInputModule>();
            }
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(string name, Transform parent, string content, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float minHeight)
        {
            var image = CreateImage("Button_" + label, parent, color);
            if (minHeight > 0f)
            {
                image.gameObject.AddComponent<LayoutElement>().minHeight = minHeight;
            }

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var text = CreateText("Label", image.transform, label, 34, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f);
            return button;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
