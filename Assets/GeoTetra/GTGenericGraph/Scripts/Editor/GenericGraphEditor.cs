﻿using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.AssetImporters;
using UnityEditor.ShaderGraph.Drawing;
using UnityEngine;

namespace GeoTetra.GTGenericGraph
{
	[CustomEditor(typeof(GraphData))]
	public class GenericGraphEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			if (GUILayout.Button("Open Generic Graph Editor"))
			{
				GraphData graphData = target as GraphData;
				GenericGraphEditorWindow.CreateWindow(graphData);
//				ShowGraphEditWindow(graph);
			}
		}

//		internal bool ShowGraphEditWindow(GenericGraphLogic graph)
//		{
//			var guid = AssetDatabase.AssetPathToGUID(path);
//			var extension = Path.GetExtension(path);
//			if (extension != ".GenericGraph" && extension != ".GenericSubGraph")
//				return false;
//
//			var foundWindow = false;
//			foreach (var w in Resources.FindObjectsOfTypeAll<GenericGraphEditWindow>())
//			{
//				if (w.selectedGuid == guid)
//				{
//					foundWindow = true;
//					w.Focus();
//				}
//			}
//
//			if (!foundWindow)
//			{
//				GenericGraphEditWindow.CreateWindow(target as GenericGraphLogic);
//			}
//
//			return true;
//		}

//		[OnOpenAsset(0)]
//		public static bool OnOpenAsset(int instanceID, int line)
//		{
//			var path = AssetDatabase.GetAssetPath(instanceID);
//			return ShowGraphEditWindow(path);
//		}
	}
}