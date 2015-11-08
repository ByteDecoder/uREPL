﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

namespace uREPL
{

public class CommandInputField : InputField
{
	public override void OnUpdateSelected(BaseEventData eventData)
	{
		if (!isFocused) return;

		bool consumedEvent = false;
		var e = new Event();
		while (Event.PopEvent(e)) {
			if (e.rawType == EventType.KeyDown) {
				consumedEvent = true;
				// Skip
				switch (e.keyCode) {
					case KeyCode.UpArrow:
					case KeyCode.DownArrow:
						continue;
				}
				var shouldContinue = KeyPressed(e);
				// Prevent finish
				switch (e.keyCode) {
					case KeyCode.Return:
					case KeyCode.KeypadEnter:
					case KeyCode.Escape:
						shouldContinue = EditState.Continue;
						break;
				}
				if (shouldContinue == EditState.Finish) {
					DeactivateInputField();
					break;
				}
			}
		}

		if (consumedEvent) {
			UpdateLabel();
		}

		eventData.Use();
	}
}

}
