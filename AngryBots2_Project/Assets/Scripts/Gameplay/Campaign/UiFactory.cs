using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ABGameplay.Campaign
{
	/// <summary>运行时构建 UGUI 的小工具集，避免依赖手工制作的 Prefab。</summary>
	public static class UiFactory
	{
		static Font cachedFont;

		public static Font Font
		{
			get
			{
				if (cachedFont != null) return cachedFont;
				cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
				if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
				if (cachedFont == null) cachedFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 18);
				return cachedFont;
			}
		}

		public static Canvas CreateCanvas(string name)
		{
			var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			var canvas = go.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 100;

			var scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1600f, 900f);
			scaler.matchWidthOrHeight = 0.5f;

			if (Object.FindObjectOfType<EventSystem>() == null)
			{
				var eventGo = new GameObject("[EventSystem]", typeof(EventSystem), typeof(StandaloneInputModule));
				Object.DontDestroyOnLoad(eventGo);
			}
			return canvas;
		}

		public static RectTransform CreatePanel(Transform parent, string name, Color color)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			var rect = go.GetComponent<RectTransform>();
			var image = go.GetComponent<Image>();
			image.color = color;
			return rect;
		}

		public static RectTransform CreateRect(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return go.GetComponent<RectTransform>();
		}

		public static Text CreateText(Transform parent, string name, string content, int size, TextAnchor anchor)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Text));
			go.transform.SetParent(parent, false);
			var text = go.GetComponent<Text>();
			text.font = Font;
			text.fontSize = size;
			text.text = content;
			text.alignment = anchor;
			text.color = Color.white;
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			return text;
		}

		public static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size, System.Action onClick)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(parent, false);

			var rect = go.GetComponent<RectTransform>();
			rect.sizeDelta = size;
			rect.anchoredPosition = anchoredPosition;

			var image = go.GetComponent<Image>();
			image.color = new Color(0.12f, 0.16f, 0.2f, 0.95f);

			var button = go.GetComponent<Button>();
			var colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(0.75f, 0.9f, 1f);
			colors.pressedColor = new Color(0.5f, 0.8f, 1f);
			button.colors = colors;
			if (onClick != null) button.onClick.AddListener(() => onClick());

			var text = CreateText(go.transform, "Label", label, 24, TextAnchor.MiddleCenter);
			var textRect = text.rectTransform;
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = Vector2.zero;
			textRect.offsetMax = Vector2.zero;

			return button;
		}

		public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
			Vector2 anchoredPosition, Vector2 sizeDelta)
		{
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = pivot;
			rect.anchoredPosition = anchoredPosition;
			rect.sizeDelta = sizeDelta;
		}

		public static RectTransform FullScreenPanel(Transform parent, string name, Color color)
		{
			var rect = CreatePanel(parent, name, color);
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			return rect;
		}
	}
}