﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace uREPL
{

public class Gui : MonoBehaviour
{
	#region [core]
	static public Gui selected;

	private Thread completionThread_;
	private CompletionInfo[] completions_;

	public Queue<Log.Data> logData_ = new Queue<Log.Data>();
	private History history_ = new History();

	private string currentPartialCode_ = "";
	private string currentComletionPrefix_ = "";
	#endregion

	#region [key operations]
	[HeaderAttribute("Keys")]
	public KeyCode openKey = KeyCode.F1;
	public KeyCode closeKey = KeyCode.F1;
	private bool isWindowOpened_ = false;

	private KeyEvent keyEvent_ = new KeyEvent();
	#endregion

	#region [content]
	private CommandInputField inputField;
	private CommandInputField multilineInputField;
	private Transform outputContent;
	private AnnotationView annotation;
	private CompletionView completionView;
	private GameObject resultItemPrefab;
	private GameObject logItemPrefab;

	public int caretPosition
	{
		get { return inputField.caretPosition;  }
		set { inputField.caretPosition = value; }
	}
	#endregion

	#region [parameters]
	[HeaderAttribute("Parameters")]
	public int maxResultNum = 100;
	public float completionTimer = 0.5f;
	private bool isComplementing_ = false;
	private bool isCompletionFinished_ = false;
	private bool isCompletionStopped_ = false;
	private float elapsedTimeFromLastInput_ = 0f;
	public float annotationTimer = 1f;
	private float elapsedTimeFromLastSelect_ = 0f;
	#endregion

	void Awake()
	{
		InitObjects();
		InitCommands();
		InitEmacsLikeCommands();

		Core.Initialize();

		isWindowOpened_ = GetComponent<Canvas>().enabled;
		if (isWindowOpened_) {
			selected = this;
		}
	}

	void InitObjects()
	{
		// Instances
		var container  = transform.Find("Container");
		inputField     = container.Find("Input Field").GetComponent<CommandInputField>();
		outputContent  = container.Find("Output View/Content");
		annotation     = transform.Find("Annotation View").GetComponent<AnnotationView>();
		completionView = transform.Find("Completion View").GetComponent<CompletionView>();

		// Prefabs
		resultItemPrefab = Resources.Load<GameObject>("uREPL/Prefabs/Output/Result Item");
		logItemPrefab    = Resources.Load<GameObject>("uREPL/Prefabs/Output/Log Item");

		// Settings
		inputField.parentGui = this;
	}

	private void InitCommands()
	{
		keyEvent_.Add(KeyCode.UpArrow, Prev);
		keyEvent_.Add(KeyCode.DownArrow, Next);
		keyEvent_.Add(KeyCode.LeftArrow, StopCompletion);
		keyEvent_.Add(KeyCode.RightArrow, StopCompletion);
		keyEvent_.Add(KeyCode.Escape, StopCompletion);
		keyEvent_.Add(KeyCode.Tab, () => {
			if (isComplementing_) {
				DoCompletion();
			} else {
				ResetCompletion();
				StartCompletion();
			}
		});
	}

	void InitEmacsLikeCommands()
	{
		keyEvent_.Add(KeyCode.P, KeyEvent.Option.Ctrl, Prev);
		keyEvent_.Add(KeyCode.N, KeyEvent.Option.Ctrl, Next);
		keyEvent_.Add(KeyCode.F, KeyEvent.Option.Ctrl, () => {
			caretPosition = Mathf.Min(caretPosition + 1, inputField.text.Length);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.B, KeyEvent.Option.Ctrl, () => {
			caretPosition = Mathf.Max(caretPosition - 1, 0);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.A, KeyEvent.Option.Ctrl, () => {
			inputField.MoveTextStart(false);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.E, KeyEvent.Option.Ctrl, () => {
			inputField.MoveTextEnd(false);
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.H, KeyEvent.Option.Ctrl, () => {
			if (caretPosition > 0) {
				var isCaretPositionLast = caretPosition == inputField.text.Length;
				inputField.text = inputField.text.Remove(caretPosition - 1, 1);
				if (!isCaretPositionLast) {
					--caretPosition;
				}
			}
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.D, KeyEvent.Option.Ctrl, () => {
			if (caretPosition < inputField.text.Length) {
				inputField.text = inputField.text.Remove(caretPosition, 1);
			}
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.K, KeyEvent.Option.Ctrl, () => {
			if (caretPosition < inputField.text.Length) {
				inputField.text = inputField.text.Remove(caretPosition);
			}
			StopCompletion();
		});
		keyEvent_.Add(KeyCode.L, KeyEvent.Option.Ctrl, () => {
			ClearOutputView();
		});
	}

	void Start()
	{
		RegisterListeners();
		history_.Load();
	}

	void OnDestroy()
	{
		StopCompletionThread();
		UnregisterListeners();
		history_.Save();
	}

	void Update()
	{
		if (openKey == closeKey) {
			if (Input.GetKeyDown(openKey)) {
				if (!isWindowOpened_) {
					OpenWindow();
				} else {
					CloseWindow();
				}
			}
		} else {
			if (Input.GetKeyDown(openKey)) {
				OpenWindow();
			}
			if (Input.GetKeyDown(closeKey)) {
				CloseWindow();
			}
		}

		if (isWindowOpened_) {
			if (inputField.isFocused) {
				keyEvent_.Check();
			} else {
				keyEvent_.Clear();
			}

			if (IsEnterPressing()) {
				if (isComplementing_ && !IsInputContinuously()) {
					DoCompletion();
				} else {
					OnSubmit(inputField.text);
				}
			}

			UpdateCompletion();
		}

		UpdateLogs();
		UpdateAnnotation();
	}

	public void OpenWindow()
	{
		selected = this;
		SetActive(true);
		inputField.ActivateInputField();
		inputField.Select();
	}

	public void CloseWindow()
	{
		if (selected == this) {
			selected = null;
		}
		SetActive(false);
	}

	private void SetActive(bool active)
	{
		GetComponent<Canvas>().enabled = active;
		inputField.gameObject.SetActive(active);
		outputContent.gameObject.SetActive(active);
		completionView.gameObject.SetActive(active);
		isWindowOpened_ = active;
	}

	public void ClearOutputView()
	{
		for (int i = 0; i < outputContent.childCount; ++i) {
			Destroy(outputContent.GetChild(i).gameObject);
		}
	}

	private void Prev()
	{
		if (isComplementing_) {
			completionView.Next();
			ResetAnnotation();
		} else {
			if (history_.IsFirst()) history_.SetInputtingCommand(inputField.text);
			inputField.text = history_.Prev();
			isCompletionStopped_ = true;
		}
	}

	private void Next()
	{
		if (isComplementing_) {
			completionView.Prev();
			ResetAnnotation();
		} else {
			inputField.text = history_.Next();
			isCompletionStopped_ = true;
		}
	}

	private void UpdateCompletion()
	{
		elapsedTimeFromLastInput_ += Time.deltaTime;

		// stop completion thread if it is running to avoid hang.
		if (IsCompletionThreadAlive() &&
			elapsedTimeFromLastInput_ > completionTimer + 0.5f /* margin */) {
			StopCompletion();
		}

		// show completion view after waiting for completionTimer.
		if (!isCompletionStopped_ && !isCompletionFinished_ && !IsCompletionThreadAlive()) {
			if (elapsedTimeFromLastInput_ >= completionTimer) {
				StartCompletion();
			}
		}

		// update completion view position.
		completionView.position = GetCompletionPosition();

		// update completion view if new completions set.
		if (completions_ != null && completions_.Length > 0) {
			completionView.SetCompletions(completions_);
			completions_ = null;
			ResetAnnotation();
		}
	}

	private void StartCompletion()
	{
		if (string.IsNullOrEmpty(inputField.text)) return;

		// avoid undesired hang caused by Mono.CSharp.GetCompletions,
		// run it on another thread and stop if hang occurs in UpdateCompletion().
		StopCompletionThread();
		StartCompletionThread();
	}

	private void StartCompletionThread()
	{
		var code = currentPartialCode_ + inputField.text;
		code = code.Substring(0, caretPosition);
		completionThread_ = new Thread(() => {
			completions_ = CompletionPluginManager.GetCompletions(code);
			if (completions_ != null && completions_.Length > 0) {
				currentComletionPrefix_ = completions_[0].prefix; // TODO: this is not smart...
				isComplementing_ = true;
			}
			isCompletionFinished_ = true;
		});
		completionThread_.Start();
	}

	private void StopCompletionThread()
	{
		if (completionThread_ != null) {
			completionThread_.Abort();
		}
	}

	private void DoCompletion()
	{
		var completion = completionView.selectedCompletion;
		inputField.text = inputField.text.Insert(caretPosition, completion);
		completionView.Reset();

		inputField.Select();
		inputField.caretPosition = caretPosition + completion.Length;

		StopCompletion();
	}

	private void ResetCompletion()
	{
		isComplementing_ = false;
		isCompletionFinished_ = false;
		elapsedTimeFromLastInput_ = 0;
		completionView.Reset();
		StopCompletionThread();
	}

	private void StopCompletion()
	{
		isComplementing_ = false;
		isCompletionStopped_ = true;
		completionView.Reset();
		StopCompletionThread();
	}

	private bool IsCompletionThreadAlive()
	{
		return completionThread_ != null && completionThread_.IsAlive;
	}

	private void RegisterListeners()
	{
		inputField.onValueChanged.AddListener(OnValueChanged);
		inputField.onEndEdit.AddListener(OnSubmit);
	}

	private void UnregisterListeners()
	{
		inputField.onValueChanged.RemoveListener(OnValueChanged);
		inputField.onEndEdit.RemoveListener(OnSubmit);
	}

	private bool IsInputContinuously()
	{
		return !inputField.multiLine && KeyUtil.Shift();
	}

	private bool IsEnterPressing()
	{
		if (!inputField.multiLine) {
			return KeyUtil.Enter();
		} else {
			return (KeyUtil.Control() || KeyUtil.Shift()) && KeyUtil.Enter();
		}
	}

	public Vector3 GetCompletionPosition()
	{
		if (inputField.isFocused) {
			var generator = inputField.textComponent.cachedTextGenerator;
			if (caretPosition < generator.characters.Count) {
				var len = caretPosition;
				var info = generator.characters[len];
				var ppu  = inputField.textComponent.pixelsPerUnit;
				var x = info.cursorPos.x / ppu;
				var y = info.cursorPos.y / ppu;
				var z = 0f;
				var prefixWidth = 0f;
				for (int i = 0; i < currentComletionPrefix_.Length && i < len; ++i) {
					prefixWidth += generator.characters[len - 1 - i].charWidth;
				}
				prefixWidth /= ppu;
				var inputTform = inputField.GetComponent<RectTransform>();
				return inputTform.localPosition + new Vector3(x - prefixWidth, y, z);
			}
		}
		return -9999f * Vector3.one;
	}

	private void OnValueChanged(string text)
	{
		if (!inputField.multiLine) {
			text = text.Replace("\n", "");
			text = text.Replace("\r", "");
			inputField.text = text;
		}
		if (!IsEnterPressing()) {
			isCompletionStopped_ = false;
			RunOnEndOfFrame(() => { ResetCompletion(); });
		}
	}

	private void OnSubmit(string text)
	{
		text = text.Trim();

		// do nothing if following states:
		// - the input text is empty.
		// - receive the endEdit event without the enter key (e.g. lost focus).
		if (string.IsNullOrEmpty(text) || !IsEnterPressing()) return;

		// stop completion to avoid hang.
		StopCompletionThread();

		// use the partial code previously input if it exists.
		var isPartial = false;
		var code = text;
		if (!string.IsNullOrEmpty(currentPartialCode_)) {
			code = currentPartialCode_ + code;
			currentPartialCode_ = "";
			isPartial = true;
		}

		// auto-complete semicolon.
		if (!code.EndsWith(";") && !IsInputContinuously()) {
			code += ";";
		}

		var result = Core.Evaluate(code);
		GameObject itemObj = null;
		ResultItem item = null;
		if (isPartial) {
			itemObj = outputContent.GetChild(outputContent.childCount - 1).gameObject;
		} else {
			itemObj = InstantiateInOutputContent(resultItemPrefab);
		}
		if (itemObj) {
			item = itemObj.GetComponent<ResultItem>();
		}

		RemoveExceededItem();

		if (item) {
			switch (result.type) {
				case CompileResult.Type.Success: {
					inputField.text = "";
					history_.Add(result.code);
					history_.Reset();
					item.type   = CompileResult.Type.Success;
					item.input  = result.code;
					item.output = result.value.ToString();
					break;
				}
				case CompileResult.Type.Partial: {
					inputField.text = "";
					currentPartialCode_ += text;
					item.type   = CompileResult.Type.Partial;
					item.input  = result.code;
					item.output = "...";
					break;
				}
				case CompileResult.Type.Error: {
					item.type   = CompileResult.Type.Error;
					item.input  = result.code;
					item.output = result.error;
					break;
				}
			}
		}

		isComplementing_ = false;
	}

	private void RemoveExceededItem()
	{
		if (outputContent.childCount > maxResultNum) {
			Destroy(outputContent.GetChild(0).gameObject);
		}
	}

	public void OutputLog(Log.Data data)
	{
		// enqueue given datas temporarily to handle data from other threads.
		logData_.Enqueue(data);
	}

	private void UpdateLogs()
	{
		while (logData_.Count > 0) {
			var data = logData_.Dequeue();
			var item = InstantiateInOutputContent(logItemPrefab).GetComponent<LogItem>();
			item.level = data.level;
			item.log   = data.log;
			item.meta  = data.meta;
		}
		RemoveExceededItem();
	}

	private void ResetAnnotation()
	{
		elapsedTimeFromLastSelect_ = 0f;
	}

	private void UpdateAnnotation()
	{
		elapsedTimeFromLastSelect_ += Time.deltaTime;

		var item = completionView.selectedItem;
		var hasDescription = (item != null) && item.hasDescription;
		if (hasDescription) annotation.text = item.description;

		var isAnnotationVisible = elapsedTimeFromLastSelect_ >= annotationTimer;
		annotation.gameObject.SetActive(isAnnotationVisible && isComplementing_ && hasDescription);

		annotation.transform.position =
			completionView.selectedPosition + Vector3.right * (completionView.width + 4f);
	}

	static public GameObject InstantiateInOutputContent(GameObject prefab)
	{
		if (selected == null) return null;

		var obj = Instantiate(
			prefab,
			selected.outputContent.position,
			selected.outputContent.rotation) as GameObject;
		obj.transform.SetParent(selected.outputContent);
		obj.transform.localScale = Vector3.one;

		return obj;
	}

	private void RunOnEndOfFrame(System.Action func)
	{
		StartCoroutine(_RunOnEndOfFrame(func));
	}

	private IEnumerator _RunOnEndOfFrame(System.Action func)
	{
		yield return new WaitForEndOfFrame();
		func();
	}

	public void RunOnNextFrame(System.Action func)
	{
		StartCoroutine(_RunOnNextFrame(func));
	}

	private IEnumerator _RunOnNextFrame(System.Action func)
	{
		yield return new WaitForEndOfFrame();
		yield return new WaitForEndOfFrame();
		func();
	}

	[Command(name = "close", description = "Close console.")]
	static public void CloseCommand()
	{
		if (selected) {
			selected.RunOnNextFrame(() => {
				if (selected) selected.CloseWindow();
			});
		}
	}

	[Command(name = "clear outputs", description = "Clear output view.")]
	static public void ClearOutputCommand()
	{
		if (selected == null) return;

		var target = selected;
		selected.RunOnNextFrame(() => {
			target.ClearOutputView();
		});
	}

	[Command(name = "clear histories", description = "Clear all input histories.")]
	static public void ClearHistoryCommand()
	{
		if (selected == null) return;

		var target = selected;
		selected.RunOnNextFrame(() => {
			target.history_.Clear();
		});
	}

	[Command(name = "show histories", description = "show command histoies.")]
	static public void ShowHistory()
	{
		if (selected == null) return;

		string histories = "";
		int num = selected.history_.Count;
		foreach (var command in selected.history_.list.ToArray().Reverse()) {
			histories += string.Format("{0}: {1}\n", num, command);
			--num;
		}
		Log.Output(histories);
	}
}

}
