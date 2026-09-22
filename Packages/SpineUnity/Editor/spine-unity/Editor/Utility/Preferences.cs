/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated April 5, 2025. Replaces all prior versions.
 *
 * Copyright (c) 2013-2025, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software
 * or otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THE SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

#pragma warning disable 0219

#define SPINE_SKELETONMECANIM

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Spine.Unity.Editor {
	public partial class SpineEditorUtilities {
		static class SpineSettingsProviderRegistration {
			[SettingsProvider]
			public static SettingsProvider CreateSpineSettingsProvider () {
				SettingsProvider provider = new SettingsProvider("Spine", SettingsScope.User) {
					label = "Spine",
					guiHandler = (searchContext) => {
						SpinePreferences settings = SpinePreferences.GetOrCreateSettings();
						SerializedObject serializedSettings = new SerializedObject(settings);
						SpinePreferences.HandlePreferencesGUI(serializedSettings);
						if (serializedSettings.ApplyModifiedProperties())
							OldPreferences.SaveToEditorPrefs(settings);
					},

					// Populate the search keywords to enable smart search filtering and label highlighting:
					keywords = new HashSet<string>(new[] { "Spine", "Preferences", "Skeleton", "Default", "Mix", "Duration" })
				};
				return provider;
			}
		}
        
		public static SpinePreferences Preferences => SpinePreferences.GetOrCreateSettings();

        public static class OldPreferences {
			const string DEFAULT_SCALE_KEY = "SPINE_DEFAULT_SCALE";
			public static float defaultScale = SpinePreferences.DEFAULT_DEFAULT_SCALE;

			const string DEFAULT_MIX_KEY = "SPINE_DEFAULT_MIX";
			public static float defaultMix = SpinePreferences.DEFAULT_DEFAULT_MIX;

			const string DEFAULT_SHADER_KEY = "SPINE_DEFAULT_SHADER";
			public static string defaultShader = SpinePreferences.DEFAULT_DEFAULT_SHADER;
			public static string DefaultShader {
				get { return !string.IsNullOrEmpty(defaultShader) ? defaultShader : SpinePreferences.DEFAULT_DEFAULT_SHADER; }
				set { defaultShader = value; }
			}

			const string DEFAULT_ZSPACING_KEY = "SPINE_DEFAULT_ZSPACING";
			public static float defaultZSpacing = SpinePreferences.DEFAULT_DEFAULT_ZSPACING;

			const string DEFAULT_INSTANTIATE_LOOP_KEY = "SPINE_DEFAULT_INSTANTIATE_LOOP";
			public static bool defaultInstantiateLoop = SpinePreferences.DEFAULT_DEFAULT_INSTANTIATE_LOOP;

			const string DEFAULT_PHYSICS_POSITION_INHERITANCE_X_KEY = "SPINE_DEFAULT_PHYSICS_POSITION_INHERITANCE_X";
			const string DEFAULT_PHYSICS_POSITION_INHERITANCE_Y_KEY = "SPINE_DEFAULT_PHYSICS_POSITION_INHERITANCE_Y";
			public static Vector2 defaultPhysicsPositionInheritance = SpinePreferences.DEFAULT_DEFAULT_PHYSICS_POSITION_INHERITANCE;

			const string DEFAULT_PHYSICS_ROTATION_INHERITANCE_KEY = "SPINE_DEFAULT_PHYSICS_ROTATION_INHERITANCE";
			public static float defaultPhysicsRotationInheritance = SpinePreferences.DEFAULT_DEFAULT_PHYSICS_ROTATION_INHERITANCE;

			const string SHOW_HIERARCHY_ICONS_KEY = "SPINE_SHOW_HIERARCHY_ICONS";
			public static bool showHierarchyIcons = SpinePreferences.DEFAULT_SHOW_HIERARCHY_ICONS;

			const string RELOAD_AFTER_PLAYMODE_KEY = "SPINE_RELOAD_AFTER_PLAYMODE";
			public static bool reloadAfterPlayMode = SpinePreferences.DEFAULT_RELOAD_AFTER_PLAYMODE;

			const string SET_TEXTUREIMPORTER_SETTINGS_KEY = "SPINE_SET_TEXTUREIMPORTER_SETTINGS";
			public static bool setTextureImporterSettings = SpinePreferences.DEFAULT_SET_TEXTUREIMPORTER_SETTINGS;

			const string TEXTURE_SETTINGS_REFERENCE_KEY = "SPINE_TEXTURE_SETTINGS_REFERENCE";
			public static string textureSettingsReference = SpinePreferences.DEFAULT_TEXTURE_SETTINGS_REFERENCE;

			public static bool UsesPMAWorkflow {
				get {
					return SpinePreferences.IsPMAWorkflow(textureSettingsReference);
				}
			}

			const string APPLY_ADDITIVE_MATERIAL_KEY = "SPINE_APPLY_ADDITIVE_MATERIAL";
			const string BLEND_MODE_MATERIAL_MULTIPLY_KEY = "SPINE_BLENDMODE_MATERIAL_MULTIPLY";
			const string BLEND_MODE_MATERIAL_SCREEN_KEY = "SPINE_BLENDMODE_MATERIAL_SCREEN";
			const string BLEND_MODE_MATERIAL_ADDITIVE_KEY = "SPINE_BLENDMODE_MATERIAL_ADDITIVE";
			public static bool applyAdditiveMaterial = false;
			public static string blendModeMaterialMultiply = "";
			public static string blendModeMaterialScreen = "";
			public static string blendModeMaterialAdditive = "";
			public const bool DEFAULT_APPLY_ADDITIVE_MATERIAL = SpinePreferences.DEFAULT_APPLY_ADDITIVE_MATERIAL;
			public const string DEFAULT_BLEND_MODE_MULTIPLY_MATERIAL = SpinePreferences.DEFAULT_BLEND_MODE_MULTIPLY_MATERIAL;
			public const string DEFAULT_BLEND_MODE_SCREEN_MATERIAL = SpinePreferences.DEFAULT_BLEND_MODE_SCREEN_MATERIAL;
			public const string DEFAULT_BLEND_MODE_ADDITIVE_MATERIAL = SpinePreferences.DEFAULT_BLEND_MODE_ADDITIVE_MATERIAL;

			public static Material BlendModeMaterialMultiply {
				get { return AssetDatabase.LoadAssetAtPath<Material>(blendModeMaterialMultiply); }
			}
			public static Material BlendModeMaterialScreen {
				get { return AssetDatabase.LoadAssetAtPath<Material>(blendModeMaterialScreen); }
			}
			public static Material BlendModeMaterialAdditive {
				get { return AssetDatabase.LoadAssetAtPath<Material>(blendModeMaterialAdditive); }
			}

			const string ATLASTXT_WARNING_KEY = "SPINE_ATLASTXT_WARNING";
			public static bool atlasTxtImportWarning = SpinePreferences.DEFAULT_ATLASTXT_WARNING;

			const string TEXTUREIMPORTER_WARNING_KEY = "SPINE_TEXTUREIMPORTER_WARNING";
			public static bool textureImporterWarning = SpinePreferences.DEFAULT_TEXTUREIMPORTER_WARNING;

			const string COMPONENTMATERIAL_WARNING_KEY = "SPINE_COMPONENTMATERIAL_WARNING";
			public static bool componentMaterialWarning = SpinePreferences.DEFAULT_COMPONENTMATERIAL_WARNING;

			const string SKELETONDATA_ASSET_NO_FILE_ERROR_KEY = "SPINE_SKELETONDATA_ASSET_NO_FILE_ERROR";
			public static bool skeletonDataAssetNoFileError = SpinePreferences.DEFAULT_SKELETONDATA_ASSET_NO_FILE_ERROR;

			public const float DEFAULT_MIPMAPBIAS = SpinePreferences.DEFAULT_MIPMAPBIAS;

			public const string SCENE_ICONS_SCALE_KEY = "SPINE_SCENE_ICONS_SCALE";
			public static float handleScale = SpinePreferences.DEFAULT_SCENE_ICONS_SCALE;

			const string AUTO_RELOAD_SCENESKELETONS_KEY = "SPINE_AUTO_RELOAD_SCENESKELETONS";
			public static bool autoReloadSceneSkeletons = SpinePreferences.DEFAULT_AUTO_RELOAD_SCENESKELETONS;

			const string MECANIM_EVENT_INCLUDE_FOLDERNAME_KEY = "SPINE_MECANIM_EVENT_INCLUDE_FOLDERNAME";
			public static bool mecanimEventIncludeFolderName = SpinePreferences.DEFAULT_MECANIM_EVENT_INCLUDE_FOLDERNAME;

			const string TIMELINE_USE_BLEND_DURATION_KEY = "SPINE_TIMELINE_USE_BLEND_DURATION_KEY";
			public static bool timelineUseBlendDuration = SpinePreferences.DEFAULT_TIMELINE_USE_BLEND_DURATION;

			const string TIMELINE_DEFAULT_MIX_DURATION_KEY = "SPINE_TIMELINE_DEFAULT_MIX_DURATION_KEY";
			public static bool timelineDefaultMixDuration = SpinePreferences.DEFAULT_TIMELINE_DEFAULT_MIX_DURATION;

			static bool preferencesLoaded = false;

			public static void Load () {
				if (preferencesLoaded)
					return;

				defaultMix = EditorPrefs.GetFloat(DEFAULT_MIX_KEY, SpinePreferences.DEFAULT_DEFAULT_MIX);
				defaultScale = EditorPrefs.GetFloat(DEFAULT_SCALE_KEY, SpinePreferences.DEFAULT_DEFAULT_SCALE);
				defaultZSpacing = EditorPrefs.GetFloat(DEFAULT_ZSPACING_KEY, SpinePreferences.DEFAULT_DEFAULT_ZSPACING);
				defaultInstantiateLoop = EditorPrefs.GetBool(DEFAULT_INSTANTIATE_LOOP_KEY, SpinePreferences.DEFAULT_DEFAULT_INSTANTIATE_LOOP);
				defaultPhysicsPositionInheritance.x = EditorPrefs.GetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_X_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_POSITION_INHERITANCE.x);
				defaultPhysicsPositionInheritance.y = EditorPrefs.GetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_Y_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_POSITION_INHERITANCE.y);
				defaultPhysicsRotationInheritance = EditorPrefs.GetFloat(DEFAULT_PHYSICS_ROTATION_INHERITANCE_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_ROTATION_INHERITANCE);
				defaultShader = EditorPrefs.GetString(DEFAULT_SHADER_KEY, SpinePreferences.DEFAULT_DEFAULT_SHADER);
				showHierarchyIcons = EditorPrefs.GetBool(SHOW_HIERARCHY_ICONS_KEY, SpinePreferences.DEFAULT_SHOW_HIERARCHY_ICONS);
				reloadAfterPlayMode = EditorPrefs.GetBool(RELOAD_AFTER_PLAYMODE_KEY, SpinePreferences.DEFAULT_RELOAD_AFTER_PLAYMODE);
				setTextureImporterSettings = EditorPrefs.GetBool(SET_TEXTUREIMPORTER_SETTINGS_KEY, SpinePreferences.DEFAULT_SET_TEXTUREIMPORTER_SETTINGS);
				textureSettingsReference = EditorPrefs.GetString(TEXTURE_SETTINGS_REFERENCE_KEY, SpinePreferences.DEFAULT_TEXTURE_SETTINGS_REFERENCE);
				applyAdditiveMaterial = EditorPrefs.GetBool(APPLY_ADDITIVE_MATERIAL_KEY, SpinePreferences.DEFAULT_APPLY_ADDITIVE_MATERIAL);
				blendModeMaterialMultiply = EditorPrefs.GetString(BLEND_MODE_MATERIAL_MULTIPLY_KEY, "");
				blendModeMaterialScreen = EditorPrefs.GetString(BLEND_MODE_MATERIAL_SCREEN_KEY, "");
				blendModeMaterialAdditive = EditorPrefs.GetString(BLEND_MODE_MATERIAL_ADDITIVE_KEY, "");
				autoReloadSceneSkeletons = EditorPrefs.GetBool(AUTO_RELOAD_SCENESKELETONS_KEY, SpinePreferences.DEFAULT_AUTO_RELOAD_SCENESKELETONS);
				mecanimEventIncludeFolderName = EditorPrefs.GetBool(MECANIM_EVENT_INCLUDE_FOLDERNAME_KEY, SpinePreferences.DEFAULT_MECANIM_EVENT_INCLUDE_FOLDERNAME);
				atlasTxtImportWarning = EditorPrefs.GetBool(ATLASTXT_WARNING_KEY, SpinePreferences.DEFAULT_ATLASTXT_WARNING);
				textureImporterWarning = EditorPrefs.GetBool(TEXTUREIMPORTER_WARNING_KEY, SpinePreferences.DEFAULT_TEXTUREIMPORTER_WARNING);
				componentMaterialWarning = EditorPrefs.GetBool(COMPONENTMATERIAL_WARNING_KEY, SpinePreferences.DEFAULT_COMPONENTMATERIAL_WARNING);
				skeletonDataAssetNoFileError = EditorPrefs.GetBool(SKELETONDATA_ASSET_NO_FILE_ERROR_KEY, SpinePreferences.DEFAULT_SKELETONDATA_ASSET_NO_FILE_ERROR);
				timelineDefaultMixDuration = EditorPrefs.GetBool(TIMELINE_DEFAULT_MIX_DURATION_KEY, SpinePreferences.DEFAULT_TIMELINE_DEFAULT_MIX_DURATION);
				timelineUseBlendDuration = EditorPrefs.GetBool(TIMELINE_USE_BLEND_DURATION_KEY, SpinePreferences.DEFAULT_TIMELINE_USE_BLEND_DURATION);
				handleScale = EditorPrefs.GetFloat(SCENE_ICONS_SCALE_KEY, SpinePreferences.DEFAULT_SCENE_ICONS_SCALE);
				preferencesLoaded = true;
			}

			public static void CopyOldToNewPreferences (ref SpinePreferences newPreferences) {
				newPreferences.defaultMix = EditorPrefs.GetFloat(DEFAULT_MIX_KEY, SpinePreferences.DEFAULT_DEFAULT_MIX);
				newPreferences.defaultScale = EditorPrefs.GetFloat(DEFAULT_SCALE_KEY, SpinePreferences.DEFAULT_DEFAULT_SCALE);
				newPreferences.defaultZSpacing = EditorPrefs.GetFloat(DEFAULT_ZSPACING_KEY, SpinePreferences.DEFAULT_DEFAULT_ZSPACING);
				newPreferences.defaultInstantiateLoop = EditorPrefs.GetBool(DEFAULT_INSTANTIATE_LOOP_KEY, SpinePreferences.DEFAULT_DEFAULT_INSTANTIATE_LOOP);
				newPreferences.defaultPhysicsPositionInheritance.x = EditorPrefs.GetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_X_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_POSITION_INHERITANCE.x);
				newPreferences.defaultPhysicsPositionInheritance.y = EditorPrefs.GetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_Y_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_POSITION_INHERITANCE.y);
				newPreferences.defaultPhysicsRotationInheritance = EditorPrefs.GetFloat(DEFAULT_PHYSICS_ROTATION_INHERITANCE_KEY, SpinePreferences.DEFAULT_DEFAULT_PHYSICS_ROTATION_INHERITANCE);
				newPreferences.defaultShader = EditorPrefs.GetString(DEFAULT_SHADER_KEY, SpinePreferences.DEFAULT_DEFAULT_SHADER);
				newPreferences.showHierarchyIcons = EditorPrefs.GetBool(SHOW_HIERARCHY_ICONS_KEY, SpinePreferences.DEFAULT_SHOW_HIERARCHY_ICONS);
				newPreferences.reloadAfterPlayMode = EditorPrefs.GetBool(RELOAD_AFTER_PLAYMODE_KEY, SpinePreferences.DEFAULT_RELOAD_AFTER_PLAYMODE);
				newPreferences.setTextureImporterSettings = EditorPrefs.GetBool(SET_TEXTUREIMPORTER_SETTINGS_KEY, SpinePreferences.DEFAULT_SET_TEXTUREIMPORTER_SETTINGS);
				newPreferences.textureSettingsReference = EditorPrefs.GetString(TEXTURE_SETTINGS_REFERENCE_KEY, SpinePreferences.DEFAULT_TEXTURE_SETTINGS_REFERENCE);
				newPreferences.autoReloadSceneSkeletons = EditorPrefs.GetBool(AUTO_RELOAD_SCENESKELETONS_KEY, SpinePreferences.DEFAULT_AUTO_RELOAD_SCENESKELETONS);
				newPreferences.mecanimEventIncludeFolderName = EditorPrefs.GetBool(MECANIM_EVENT_INCLUDE_FOLDERNAME_KEY, SpinePreferences.DEFAULT_MECANIM_EVENT_INCLUDE_FOLDERNAME);
				newPreferences.atlasTxtImportWarning = EditorPrefs.GetBool(ATLASTXT_WARNING_KEY, SpinePreferences.DEFAULT_ATLASTXT_WARNING);
				newPreferences.textureImporterWarning = EditorPrefs.GetBool(TEXTUREIMPORTER_WARNING_KEY, SpinePreferences.DEFAULT_TEXTUREIMPORTER_WARNING);
				newPreferences.componentMaterialWarning = EditorPrefs.GetBool(COMPONENTMATERIAL_WARNING_KEY, SpinePreferences.DEFAULT_COMPONENTMATERIAL_WARNING);
				newPreferences.skeletonDataAssetNoFileError = EditorPrefs.GetBool(SKELETONDATA_ASSET_NO_FILE_ERROR_KEY, SpinePreferences.DEFAULT_SKELETONDATA_ASSET_NO_FILE_ERROR);
				newPreferences.timelineDefaultMixDuration = EditorPrefs.GetBool(TIMELINE_DEFAULT_MIX_DURATION_KEY, SpinePreferences.DEFAULT_TIMELINE_DEFAULT_MIX_DURATION);
				newPreferences.timelineUseBlendDuration = EditorPrefs.GetBool(TIMELINE_USE_BLEND_DURATION_KEY, SpinePreferences.DEFAULT_TIMELINE_USE_BLEND_DURATION);
				newPreferences.handleScale = EditorPrefs.GetFloat(SCENE_ICONS_SCALE_KEY, SpinePreferences.DEFAULT_SCENE_ICONS_SCALE);
			}

			public static void SaveToEditorPrefs (SpinePreferences preferences) {
				EditorPrefs.SetFloat(DEFAULT_MIX_KEY, preferences.defaultMix);
				EditorPrefs.SetFloat(DEFAULT_SCALE_KEY, preferences.defaultScale);
				EditorPrefs.SetFloat(DEFAULT_ZSPACING_KEY, preferences.defaultZSpacing);
				EditorPrefs.SetBool(DEFAULT_INSTANTIATE_LOOP_KEY, preferences.defaultInstantiateLoop);
				EditorPrefs.SetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_X_KEY, preferences.defaultPhysicsPositionInheritance.x);
				EditorPrefs.SetFloat(DEFAULT_PHYSICS_POSITION_INHERITANCE_Y_KEY, preferences.defaultPhysicsPositionInheritance.y);
				EditorPrefs.SetFloat(DEFAULT_PHYSICS_ROTATION_INHERITANCE_KEY, preferences.defaultPhysicsRotationInheritance);
				EditorPrefs.SetString(DEFAULT_SHADER_KEY, preferences.defaultShader);
				EditorPrefs.SetBool(SHOW_HIERARCHY_ICONS_KEY, preferences.showHierarchyIcons);
				EditorPrefs.SetBool(RELOAD_AFTER_PLAYMODE_KEY, preferences.reloadAfterPlayMode);
				EditorPrefs.SetBool(SET_TEXTUREIMPORTER_SETTINGS_KEY, preferences.setTextureImporterSettings);
				EditorPrefs.SetString(TEXTURE_SETTINGS_REFERENCE_KEY, preferences.textureSettingsReference);
				EditorPrefs.SetBool(AUTO_RELOAD_SCENESKELETONS_KEY, preferences.autoReloadSceneSkeletons);
				EditorPrefs.SetBool(MECANIM_EVENT_INCLUDE_FOLDERNAME_KEY, preferences.mecanimEventIncludeFolderName);
				EditorPrefs.SetBool(ATLASTXT_WARNING_KEY, preferences.atlasTxtImportWarning);
				EditorPrefs.SetBool(TEXTUREIMPORTER_WARNING_KEY, preferences.textureImporterWarning);
				EditorPrefs.SetBool(COMPONENTMATERIAL_WARNING_KEY, preferences.componentMaterialWarning);
				EditorPrefs.SetBool(SKELETONDATA_ASSET_NO_FILE_ERROR_KEY, preferences.skeletonDataAssetNoFileError);
				EditorPrefs.SetBool(TIMELINE_DEFAULT_MIX_DURATION_KEY, preferences.timelineDefaultMixDuration);
				EditorPrefs.SetBool(TIMELINE_USE_BLEND_DURATION_KEY, preferences.timelineUseBlendDuration);
				EditorPrefs.SetFloat(SCENE_ICONS_SCALE_KEY, preferences.handleScale);
			}
		}

		static void BoolPrefsField (ref bool currentValue, string editorPrefsKey, GUIContent label) {
			EditorGUI.BeginChangeCheck();
			currentValue = EditorGUILayout.Toggle(label, currentValue);
			if (EditorGUI.EndChangeCheck())
				EditorPrefs.SetBool(editorPrefsKey, currentValue);
		}

		static void FloatPrefsField (ref float currentValue, string editorPrefsKey, GUIContent label, float min = float.NegativeInfinity, float max = float.PositiveInfinity) {
			EditorGUI.BeginChangeCheck();
			currentValue = EditorGUILayout.DelayedFloatField(label, currentValue);
			if (EditorGUI.EndChangeCheck()) {
				currentValue = Mathf.Clamp(currentValue, min, max);
				EditorPrefs.SetFloat(editorPrefsKey, currentValue);
			}
		}

		static void Texture2DPrefsField (ref string currentValue, string editorPrefsKey, GUIContent label) {
			EditorGUI.BeginChangeCheck();
			EditorGUIUtility.wideMode = true;
			Texture2D texture = (EditorGUILayout.ObjectField(label, AssetDatabase.LoadAssetAtPath<Texture2D>(currentValue), typeof(Object), false) as Texture2D);
			currentValue = texture != null ? AssetDatabase.GetAssetPath(texture) : "";
			if (EditorGUI.EndChangeCheck()) {
				EditorPrefs.SetString(editorPrefsKey, currentValue);
			}
		}

		static void MaterialPrefsField (ref string currentValue, string editorPrefsKey, GUIContent label) {
			EditorGUI.BeginChangeCheck();
			EditorGUIUtility.wideMode = true;
			Material material = (EditorGUILayout.ObjectField(label, AssetDatabase.LoadAssetAtPath<Material>(currentValue), typeof(Object), false) as Material);
			currentValue = material != null ? AssetDatabase.GetAssetPath(material) : "";
			if (EditorGUI.EndChangeCheck()) {
				EditorPrefs.SetString(editorPrefsKey, currentValue);
			}
		}

		public static void FloatPropertyField (SerializedProperty property, GUIContent label, float min = float.NegativeInfinity, float max = float.PositiveInfinity) {
			EditorGUI.BeginChangeCheck();
			property.floatValue = EditorGUILayout.DelayedFloatField(label, property.floatValue);
			if (EditorGUI.EndChangeCheck()) {
				property.floatValue = Mathf.Clamp(property.floatValue, min, max);
			}
		}

		public static void ShaderPropertyField (SerializedProperty property, GUIContent label, string fallbackShaderName) {
			Shader shader = (EditorGUILayout.ObjectField(label, Shader.Find(property.stringValue), typeof(Shader), false) as Shader);
			property.stringValue = shader != null ? shader.name : fallbackShaderName;
		}

		public static void MaterialPropertyField (SerializedProperty property, GUIContent label) {
			Material material = (EditorGUILayout.ObjectField(label, AssetDatabase.LoadAssetAtPath<Material>(property.stringValue), typeof(Material), false) as Material);
			property.stringValue = material ? AssetDatabase.GetAssetPath(material) : "";
		}

		public static void PresetAssetPropertyField (SerializedProperty property, GUIContent label) {
			UnityEditor.Presets.Preset texturePreset = (EditorGUILayout.ObjectField(label, AssetDatabase.LoadAssetAtPath<UnityEditor.Presets.Preset>(property.stringValue), typeof(UnityEditor.Presets.Preset), false) as UnityEditor.Presets.Preset);
			bool isTexturePreset = texturePreset != null && texturePreset.GetTargetTypeName() == "TextureImporter";
			property.stringValue = isTexturePreset ? AssetDatabase.GetAssetPath(texturePreset) : "";
		}
	}
}
