/*
 * Created by PeDev 2020
 * https://github.com/qwe321qwe321qwe321/Unity-AnimatorTransitionCopier
 */
using UnityEditor;
using UnityEngine;

namespace PeDev {
	/// <summary>
	/// Provides useful EditorGUI and GUILayout extensions.
	/// </summary>
	public static class EditorGUIHelper {
		public enum EditorButtonSize {
			Tiny,
			Small,
			Medium,
			Large,
			Gigantic
		}
		private static float GetDefinedButtonHeight(EditorButtonSize buttonSize) {
			switch (buttonSize) {
				case EditorButtonSize.Tiny:
					return 20f;
				case EditorButtonSize.Small:
					return 25;
				case EditorButtonSize.Medium:
					return 32.5f;
				case EditorButtonSize.Large:
					return 40;
				case EditorButtonSize.Gigantic:
					return 60;
			}
			return 0;
		}

		/// <summary>
		/// Make a single press button with the defined button size, the specific background color and text color.
		/// </summary>
		/// <returns></returns>
		public static bool Button(string text, EditorButtonSize buttonSize, bool richText = false, Color? backgroundColor = null, Color? textColor = null) {
			return Button(new GUIContent(text), buttonSize, richText, backgroundColor, textColor);
		}

		/// <summary>
		/// Make a single press button with the defined button size, the specific background color and text color.
		/// </summary>
		/// <returns></returns>
		public static bool Button(GUIContent text, EditorButtonSize buttonSize, bool richText = false, Color? backgroundColor = null, Color? textColor = null) {
			float maxHeight = GetDefinedButtonHeight(buttonSize);
			return Button(text, maxHeight, richText, backgroundColor, textColor);
		}

		/// <summary>
		/// Make a single press button with the specific height, expand width, the specific background color and text color.
		/// </summary>
		/// <returns></returns>
		public static bool Button(GUIContent text, float maxHeight, bool richText = false, Color? backgroundColor = null, Color? textColor = null) {
			if (backgroundColor.HasValue) {
				BeginBackgroundColorGroup(backgroundColor.Value);
			}
			var style = new GUIStyle(GUI.skin.button);
			if (textColor.HasValue) {
				style.normal.textColor = textColor.Value;
				style.hover.textColor = textColor.Value;
				style.active.textColor = textColor.Value;
				style.focused.textColor = textColor.Value;
			}
			style.richText = richText;
			BeginGUILayoutIndentLevelGroup();
			bool buttonReturn = GUILayout.Button(text, style, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(maxHeight));
			EndGUILayoutIndentLevelGroup();
			if (backgroundColor.HasValue) {
				EndBackgroundColorGroup();
			}
			return buttonReturn;
		}

		private static Color s_OriginalBackgroundColor;
		private static int s_BackgroundColorGroupLevel = 0;

		/// <summary>
		/// Begin the content color group which makes all GUI in there will have the specific background color.
		/// </summary>
		/// <param name="backgroundColor"></param>
		public static void BeginBackgroundColorGroup(Color backgroundColor) {
			s_OriginalBackgroundColor = GUI.backgroundColor;
			s_BackgroundColorGroupLevel++;
			GUI.backgroundColor = backgroundColor;
		}

		public static void EndBackgroundColorGroup() {
			if (s_BackgroundColorGroupLevel <= 0) {
				Debug.LogError("You are trying to do invalid operation: Call EndContentColorGroup() without BeginContentColorGroup().");
				return;
			}
			GUI.backgroundColor = s_OriginalBackgroundColor;
			s_BackgroundColorGroupLevel--;
		}

		/// <summary>
		/// Make a label field with the specific font style.
		/// </summary>
		/// <param name="text"></param>
		public static void LabelField(string text, FontStyle fontStyle = FontStyle.Normal) {
			LabelField(new GUIContent(text), fontStyle);
		}

		/// <summary>
		/// Make a label field with the specific font style.
		/// </summary>
		/// <param name="text"></param>
		public static void LabelField(GUIContent text, FontStyle fontStyle = FontStyle.Normal) {
			GUIStyle labelStyle = EditorStyles.label;
			labelStyle.fontStyle = fontStyle;
			EditorGUILayout.LabelField(text, labelStyle);
		}

		/// <summary>
		/// EdiotrGUI indent level plus one.
		/// </summary>
		public static void PushIndentLevel() {
			EditorGUI.indentLevel++;
		}

		/// <summary>
		/// EditorGUI indent level minus one.
		/// </summary>
		public static void PopIndentLevel() {
			EditorGUI.indentLevel--;
		}

		private static bool s_BeginGUILayoutIndentLevelGroup;
		private const float INDENT_TAB_SIZE = 15.4f;
		private const float DEFAULT_LEFT_MARGIN = 2.2f;
		public static void BeginGUILayoutIndentLevelGroup() {
			if (s_BeginGUILayoutIndentLevelGroup) {
				Debug.LogError("You are trying to call BeginGUILayoutIndentLevelGroup() twice without EndGUILayoutIndentLevelGroup()");
			}
			if (EditorGUI.indentLevel <= 0) {
				return;
			}
			GUILayout.BeginHorizontal();
			GUILayout.Space(DEFAULT_LEFT_MARGIN + EditorGUI.indentLevel * INDENT_TAB_SIZE);
			s_BeginGUILayoutIndentLevelGroup = true;
		}

		public static void EndGUILayoutIndentLevelGroup() {
			if (!s_BeginGUILayoutIndentLevelGroup) {
				return;
			}
			s_BeginGUILayoutIndentLevelGroup = false;
			GUILayout.EndHorizontal();
		}
	}
}
