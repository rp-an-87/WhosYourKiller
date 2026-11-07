using System;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhosYourKiller
{
	// Sits on a kill list row: a transparent image catches clicks over the whole row, a click opens the victim's profile screen
	internal class VictimRowClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
	{
		private static readonly Color Idle = new Color(1f, 1f, 1f, 0f);
		private static readonly Color Hover = new Color(1f, 1f, 1f, 0.07f);

		private Image _image;
		private string _key;

		public string Key
		{
			get => _key;
			set
			{
				_key = value;
				var image = GetImage();
				image.raycastTarget = value != null;
				image.color = Idle;
			}
		}

		private Image GetImage()
		{
			if (_image == null)
			{
				_image = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
				_image.color = Idle;
			}

			return _image;
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (_key != null)
			{
				GetImage().color = Hover;
			}
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			GetImage().color = Idle;
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (_key == null || ItemUiContext.Instance == null)
			{
				return;
			}

			Plugin.Verbose($"Kill list row clicked: opening profile {_key}");
			ItemUiContext.Instance.ShowPlayerProfileScreen(_key, EItemViewType.OtherPlayerProfileSimple).ContinueWith(task =>
			{
				if (task.IsFaulted)
				{
					Plugin.Log.LogError($"Opening the victim profile failed: {task.Exception}");
				}
			});
		}
	}
}
