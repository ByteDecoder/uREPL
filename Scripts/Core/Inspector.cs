﻿using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace uREPL
{

public class FieldItemInfo
{
	public string name;
	public Type type;
	public object value;
}

public class ComponentInfo
{
	public Component instance;
	public System.Type type;
	public string componentName;
	public string gameObjectPath;
	public List<FieldItemInfo> fields = new List<FieldItemInfo>();
}

public class Inspector : MonoBehaviour
{
	static private Inspector instance;

	[HeaderAttribute("Views")]
	public GameObject componentViewPrefab;

	[HeaderAttribute("Items")]
	public GameObject intItemPrefab;
	public GameObject floatItemPrefab;
	public GameObject stringItemPrefab;
	public GameObject vector2ItemPrefab;
	public GameObject vector3ItemPrefab;
	public GameObject vector4ItemPrefab;

	void Awake()
	{
		instance = this;
	}

	static private readonly List<Type> supportedTypes =
		new List<Type>() {
			typeof(int),
			typeof(float),
			typeof(string),
			typeof(Vector2),
			typeof(Vector3),
			typeof(Vector4),
			typeof(Quaternion),
		};

	static public void Inspect<T>(T component) where T : Component
	{
		if (component == null) {
			Log.Warn("given component is null.");
			return;
		}

		var componentType = typeof(T);
		var componentInfo = new ComponentInfo();
		componentInfo.instance       = component;
		componentInfo.type           = componentType;
		componentInfo.componentName  = componentType.FullName;
		componentInfo.gameObjectPath = component.transform.GetPath();

		var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance);
		foreach (var field in fields) {
			var type = field.FieldType;
			if (!supportedTypes.Contains(type)) continue;
			var info = new FieldItemInfo();
			info.type = type;
			info.name = field.Name;
			info.value = field.GetValue(component);
			componentInfo.fields.Add(info);
		}

		Gui.selected.RunOnNextFrame(() => {
			Output(componentInfo);
		});
	}

	static public void Output(ComponentInfo component)
	{
		var obj = Gui.InstantiateInOutputContent(instance.componentViewPrefab);
		if (obj == null) return;

		var item = obj.GetComponent<ComponentItem>();
		item.title = string.Format("<b><i>{0}</i></b> ({1})",
			component.componentName, component.gameObjectPath);
		foreach (var field in component.fields) {
			InstantiateFieldItem(component, item, field);
		}
	}

	static private void InstantiateFieldItem(
			ComponentInfo component, ComponentItem view, FieldItemInfo field)
	{
		GameObject obj = null;

		if (field.type == typeof(int)) {
			obj = Instantiate(instance.intItemPrefab);
		} else if (field.type == typeof(float)) {
			obj = Instantiate(instance.floatItemPrefab);
		} else if (field.type == typeof(string)) {
			obj = Instantiate(instance.intItemPrefab);
		} else if (field.type == typeof(Vector2)) {
			obj = Instantiate(instance.vector2ItemPrefab);
		} else if (field.type == typeof(Vector3)) {
			obj = Instantiate(instance.vector3ItemPrefab);
		} else if (field.type == typeof(Vector4)) {
			obj = Instantiate(instance.vector4ItemPrefab);
		} else if (field.type == typeof(Quaternion)) {
			obj = Instantiate(instance.vector4ItemPrefab);
		}

		var item = obj.GetComponent<FieldItem>();
		item.label         = field.name;
		item.component     = component.instance;
		item.componentType = component.type;
		item.fieldName     = field.name;
		item.fieldType     = field.type;

		view.Add(obj);
	}

	[Command]
	static public void Test()
	{
		uREPL.Inspector.Inspect(GameObject.Find("uREPL").GetComponent<uREPL.Gui>());
	}
}

}
