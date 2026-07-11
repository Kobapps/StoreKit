using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoreKit
{
    /// <summary>
    /// StoreKit's built-in popup presenter. In Play Mode it builds a lightweight uGUI dialog at
    /// runtime (no prefabs, no TextMeshPro dependency); in Edit Mode it falls back to an Editor
    /// dialog; in batch mode it auto-confirms so automated tests never block.
    /// Popups are queued so they never overlap.
    /// </summary>
    public sealed class DefaultStorePopupPresenter : IStorePopupPresenter
    {
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color PanelColor = new Color(0.12f, 0.13f, 0.17f, 0.98f);
        private static readonly Color TitleColor = Color.white;
        private static readonly Color MessageColor = new Color(0.78f, 0.81f, 0.88f, 1f);
        private static readonly Color ConfirmButtonColor = new Color(0.24f, 0.42f, 0.94f, 1f);
        private static readonly Color CancelButtonColor = new Color(0.28f, 0.30f, 0.36f, 1f);

        private bool _showing;

        public async UniTask<bool> ShowAsync(StorePopupRequest request, CancellationToken cancellationToken = default)
        {
            if (Application.isBatchMode)
            {
                return true;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return request.HasCancelButton
                    ? UnityEditor.EditorUtility.DisplayDialog(request.Title, request.Message, request.ConfirmText, request.CancelText)
                    : UnityEditor.EditorUtility.DisplayDialog(request.Title, request.Message, request.ConfirmText);
            }
#endif

            // Queue popups so they never stack on top of each other.
            while (_showing)
            {
                await UniTask.Yield(cancellationToken);
            }

            _showing = true;
            GameObject root = null;
            try
            {
                var tcs = new UniTaskCompletionSource<bool>();
                root = BuildDialog(request, tcs);
                using (cancellationToken.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                if (root != null)
                {
                    Object.Destroy(root);
                }

                _showing = false;
            }
        }

        private static GameObject BuildDialog(StorePopupRequest request, UniTaskCompletionSource<bool> tcs)
        {
            EnsureEventSystem();

            var root = new GameObject("StoreKitPopup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // Fullscreen backdrop that blocks input behind the dialog.
            var backdrop = CreateImage("Backdrop", root.transform, BackdropColor);
            Stretch(backdrop.rectTransform);

            // Dialog panel.
            var panel = CreateImage("Panel", root.transform, PanelColor);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(860f, 0f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 32;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateText("Title", panel.transform, request.Title, 52, FontStyle.Bold, TitleColor);
            CreateText("Message", panel.transform, request.Message, 40, FontStyle.Normal, MessageColor);

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            var rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 24;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            if (request.HasCancelButton)
            {
                CreateButton(buttonRow.transform, request.CancelText, CancelButtonColor, () => tcs.TrySetResult(false));
            }

            CreateButton(buttonRow.transform, request.ConfirmText, ConfirmButtonColor, () => tcs.TrySetResult(true));

            return root;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var existing = Object.FindAnyObjectByType<EventSystem>();
            if (existing != null)
            {
                return;
            }

            var eventSystem = new GameObject("StoreKitEventSystem", typeof(EventSystem));
            Object.DontDestroyOnLoad(eventSystem);
#if STOREKIT_HAS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(string name, Transform parent, string content, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var image = CreateImage("Button_" + label, parent, color);
            var layoutElement = image.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 110f;
            layoutElement.minHeight = 110f;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", image.transform, label, 40, FontStyle.Bold, Color.white);
            Stretch(text.rectTransform);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
