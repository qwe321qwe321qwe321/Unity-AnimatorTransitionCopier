/*
 * Created by PeDev 2020
 * https://github.com/qwe321qwe321qwe321/Unity-AnimatorTransitionCopier
 */
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.Animations;

using UnityEngine;

namespace PeDev {
	class AnimatorTransitionCopierWindow : EditorWindow {
		[MenuItem("Custom/AnimationTools/Animator Transition Copier")]
		static void CreateWindow() {
			// Get existing open window or if none, make a new one:
			GetWindow<AnimatorTransitionCopierWindow>("Animator Transition Copier");
		}

		// Draw the content of window.
		void OnGUI() {
			TestAnimatorEditorExtension();
		}

		AnimatorController m_TargetAnimatorController;
		int m_AnimatorLayerIndex;

		bool m_HasRefreshedAnimatorLayers;
		AnimatorStateMachine m_CurrentLayerStateMachine;
		List<AnimatorState> m_AllAnimatorStatesInLayer = new List<AnimatorState>();
		List<AnimatorStateMachine> m_AllStateMachinesInLayer = new List<AnimatorStateMachine>();
		Dictionary<int, string> m_AnimatorStateHashToFullName = new Dictionary<int, string>();

		List<AnimatorStateTransitionInfo> m_CopiedAnimatorStateTransitions = new List<AnimatorStateTransitionInfo>();

		bool IsCurrentLayerValid => m_TargetAnimatorController != null && m_AnimatorLayerIndex < m_TargetAnimatorController.layers.Length;

		enum SourceStateType {
			Normal = 0,
			AnyState = 1,
			EntryState = 2,
        }
		struct AnimatorStateTransitionInfo {
			public SourceStateType sourceStateType;
			public AnimatorState srcState;
			public AnimatorStateTransition transition;

			public AnimatorStateMachine srcStateMachine;
			public AnimatorTransition entryTransition;

			public int orderInSrcTransitions;

			public bool IsValid() {
                switch (sourceStateType) {
                    case SourceStateType.Normal:
						if (!srcState || !transition) {
							return false;
						}
						break;
                    case SourceStateType.AnyState:
						if (!srcStateMachine || !transition) { // any state doesn't have srcState.
							return false;
						}
						break;
                    case SourceStateType.EntryState:
						if (!srcStateMachine || !entryTransition) {
							return false;
						}
						break;
                }
				return true;
			}

			public override string ToString() {
				if (!IsValid()) {
					return "<invalid_transitions>";
                }
                switch (sourceStateType) {
                    case SourceStateType.Normal:
						if (transition.isExit) {
							return $"{srcState.name}->Exit State";
						}
						// Normal.
						return $"{srcState.name}->{transition.destinationState}";
					case SourceStateType.AnyState:
						return $"Any State->{transition.destinationState}";
					case SourceStateType.EntryState:
						return $"Entry State->{entryTransition.destinationState}";
                }

				return "<unknown_type_transitions>";
			}
		}

		void TestAnimatorEditorExtension() {
			m_HasRefreshedAnimatorLayers = false;

			m_TargetAnimatorController = EditorGUILayout.ObjectField("Animator Controller", m_TargetAnimatorController,
				typeof(AnimatorController), true) as AnimatorController;
			using (new EditorGUI.DisabledGroupScope(!m_TargetAnimatorController)) {
				if (m_TargetAnimatorController) {
					m_AnimatorLayerIndex = Mathf.Clamp(m_AnimatorLayerIndex, 0, m_TargetAnimatorController.layers.Length - 1);
					m_AnimatorLayerIndex = EditorGUILayout.Popup(
						"Animator Layer",
						m_AnimatorLayerIndex,
						m_TargetAnimatorController.layers.Select(x => x.name).ToArray()
						);
				}
			}
			if (!IsCurrentLayerValid) {
				EditorGUILayout.HelpBox($"Please assgin AnimatorController.", MessageType.Warning);
			}

			EditorGUILayout.Space(10f);
			using (new EditorGUI.DisabledGroupScope(!IsCurrentLayerValid)) {
				AnimatorTransitionBase[] selectedTransitions = Selection.objects.Select(x => x as AnimatorTransitionBase).Where(y => y != null).ToArray();
				bool canCopySelectedTransitions = selectedTransitions.Length > 0;
				if (canCopySelectedTransitions) {
					EditorGUIHelper.LabelField("Copy Transitions", FontStyle.Bold);
					
					if (EditorGUIHelper.Button("Copy selected transitions", EditorGUIHelper.EditorButtonSize.Large)) {
						RefreshCurrentStatesInLayer();
						m_CopiedAnimatorStateTransitions.Clear();
						foreach (var transition in selectedTransitions) {
							if (TryGetFullInfoInTransition(transition, out AnimatorStateTransitionInfo info)) {
								m_CopiedAnimatorStateTransitions.Add(info);
							}
						}
					}
				}

				AnimatorState selectedState = Selection.activeObject as AnimatorState;
				if (selectedState != null) {
					RefreshCurrentStatesInLayer();
					bool isValidState = m_AnimatorStateHashToFullName.ContainsKey(selectedState.GetInstanceID());
					if (IsCurrentLayerValid && !isValidState) {
						EditorGUILayout.HelpBox($"Selected state is not in {m_TargetAnimatorController.name}.{m_TargetAnimatorController.layers[m_AnimatorLayerIndex].name}", MessageType.Warning);
					}
					using (new EditorGUI.DisabledGroupScope(!isValidState)) {
						EditorGUIHelper.LabelField("Copy Transitions from state", FontStyle.Bold);
						EditorGUI.indentLevel++;
						using (new EditorGUILayout.HorizontalScope()) {
							if (EditorGUIHelper.Button("Copy all *Ingoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								CopyIngoingTransitions(selectedState, m_CopiedAnimatorStateTransitions);
							}

							if (EditorGUIHelper.Button("Copy all *Outgoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								CopyOutgoingTransitions(selectedState, m_CopiedAnimatorStateTransitions);
							}
						}
						EditorGUI.indentLevel--;

						bool canPasteTransitions = m_CopiedAnimatorStateTransitions != null &&
						m_CopiedAnimatorStateTransitions.Count > 0;

						EditorGUIHelper.LabelField("Paste Transitions to state", FontStyle.Bold);
						EditorGUI.indentLevel++;
						using (new EditorGUI.DisabledGroupScope(!canPasteTransitions)) {
							using (new EditorGUILayout.HorizontalScope()) {
								if (EditorGUIHelper.Button("Paste as *Ingoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
									PasteAsIngoingTransitions(selectedState, m_CopiedAnimatorStateTransitions);
									Debug.Log($"Pasted {m_CopiedAnimatorStateTransitions.Count} ingoing transitions.");
								}
								if (EditorGUIHelper.Button("Paste as *Outgoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
									PasteAsOutgoingTransitions(selectedState, m_CopiedAnimatorStateTransitions);
									Debug.Log($"Pasted {m_CopiedAnimatorStateTransitions.Count} outgoing transitions.");
								}
							}
						}
						EditorGUI.indentLevel--;
					}
				}
			}

			if (IsCurrentLayerValid && m_CopiedAnimatorStateTransitions.Count > 0) {
				string hint = $"Copied {m_CopiedAnimatorStateTransitions.Count} transitions: \n\n";
				foreach (var transitionInfo in m_CopiedAnimatorStateTransitions) {
					hint += $"{transitionInfo.ToString()}\n";
				}
				EditorGUILayout.HelpBox(hint, MessageType.Info);

			}

			
		}

		void RefreshCurrentStatesInLayer() {
			if (!IsCurrentLayerValid || m_HasRefreshedAnimatorLayers) {
				return;
			}
			m_CurrentLayerStateMachine = m_TargetAnimatorController.layers[m_AnimatorLayerIndex].stateMachine;
			m_AllAnimatorStatesInLayer.Clear();
			m_AllStateMachinesInLayer.Clear();
			m_AllStateMachinesInLayer.Add(m_CurrentLayerStateMachine); // Including layer state machine.
			m_AnimatorStateHashToFullName.Clear();
			GetAllStatesInStateMachineRecursively(m_CurrentLayerStateMachine, "");
		}
		void GetAllStatesInStateMachineRecursively(AnimatorStateMachine stateMachine, string prefix) {
			foreach (var childState in stateMachine.states) {
				var state = childState.state;
				m_AllAnimatorStatesInLayer.Add(state);
				if (m_AnimatorStateHashToFullName.ContainsKey(state.GetInstanceID())) {
					Debug.LogWarning($"{prefix + state.name}, {m_AnimatorStateHashToFullName[state.GetInstanceID()]}");
				} else {
					m_AnimatorStateHashToFullName.Add(state.GetInstanceID(), prefix + state.name);
				}
			}
			foreach (var childStateMachine in stateMachine.stateMachines) {
				var subStateMachine = childStateMachine.stateMachine;
				m_AllStateMachinesInLayer.Add(subStateMachine);
				if (m_AnimatorStateHashToFullName.ContainsKey(subStateMachine.GetInstanceID())) {
					Debug.LogWarning($"{prefix + subStateMachine.name}, {m_AnimatorStateHashToFullName[subStateMachine.GetInstanceID()]}");
				} else {
					m_AnimatorStateHashToFullName.Add(subStateMachine.GetInstanceID(), prefix + subStateMachine.name);
				}

				string nextPrefix = prefix + subStateMachine.name + ".";
				GetAllStatesInStateMachineRecursively(subStateMachine, nextPrefix);
			}
		}

		bool TryGetFullInfoInTransition(AnimatorTransitionBase transition, out AnimatorStateTransitionInfo info) {
			if (transition is AnimatorStateTransition stateTransition) {
				if (!transition.isExit && !transition.destinationState) {
					Debug.LogWarning($"It doesn't support copy transitions which source or destination is a state machine.");
					info = default;
					return false;
				}
				foreach (var state in m_AllAnimatorStatesInLayer) {
					for (int i = 0; i < state.transitions.Length; i++) {
						if (transition.GetInstanceID() == state.transitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								srcState = state,
								transition = stateTransition,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}

				// AnyState transitions
				foreach (var stateMachine in m_AllStateMachinesInLayer) {
					for (int i = 0; i < stateMachine.anyStateTransitions.Length; i++) {
						if (transition.GetInstanceID() == stateMachine.anyStateTransitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceStateType.AnyState,
								transition = stateTransition,
								srcStateMachine = stateMachine,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}
			}

			if (transition is AnimatorTransition entryTransition) {
				if (!transition.isExit && !transition.destinationState) {
					Debug.LogWarning($"It doesn't support copy transitions which source or destination is a state machine.");
					info = default;
					return false;
				}

				// Entry transitions
				foreach (var stateMachine in m_AllStateMachinesInLayer) {
					for (int i = 0; i < stateMachine.entryTransitions.Length; i++) {
						if (transition.GetInstanceID() == stateMachine.entryTransitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceStateType.EntryState,
								srcStateMachine = stateMachine,
								entryTransition = entryTransition,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}
            }

			// Failed.
			Debug.LogWarning($"Selected transition is not in {m_TargetAnimatorController.name}.{m_TargetAnimatorController.layers[m_AnimatorLayerIndex].name}.");
			info = default;
			return false;
		}

        #region Copy/Paste by selected state/stateMachine
        void CopyOutgoingTransitions(AnimatorState target, List<AnimatorStateTransitionInfo> copyTo) {
			copyTo.Clear();
			for (int i = 0; i < target.transitions.Length; i++) {
				if (!target.transitions[i].isExit && !target.transitions[i].destinationState) {
					// Don't handle state machine transition.
					continue;
                }
				copyTo.Add(new AnimatorStateTransitionInfo() {
					srcState = target,
					transition = target.transitions[i],
					orderInSrcTransitions = i
				});
			}
		}

		void PasteAsOutgoingTransitions(AnimatorState target, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// AddTransition() has already supported undo.
			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }
				if (transitionInfo.transition.isExit) {
					target.AddTransition(CreateExitTransition(target, transitionInfo.transition));
				} else {
					target.AddTransition(CreateStateTransition(target, transitionInfo.transition.destinationState, transitionInfo.transition));
                }
			}

		}

		void CopyIngoingTransitions(AnimatorState target, List<AnimatorStateTransitionInfo> copyTo) {
			copyTo.Clear();
			foreach (var state in m_AllAnimatorStatesInLayer) {
				for (int i = 0; i < state.transitions.Length; i++) {
					var transition = state.transitions[i];
					if (transition.destinationState == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							srcState = state,
							transition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}

			foreach (var stateMachine in m_AllStateMachinesInLayer) {
				// Entry transitions
				for (int i = 0; i < stateMachine.entryTransitions.Length; i++) {
					var transition = stateMachine.entryTransitions[i];
					if (transition.destinationState == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceStateType.EntryState,
							srcStateMachine = stateMachine,
							entryTransition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}

            // AnyState transitions
            for (int i = 0; i < m_CurrentLayerStateMachine.anyStateTransitions.Length; i++) {
				var transition = m_CurrentLayerStateMachine.anyStateTransitions[i];
				if (transition.destinationState == target) {
					copyTo.Add(new AnimatorStateTransitionInfo() {
						sourceStateType = SourceStateType.AnyState,
						srcStateMachine = m_CurrentLayerStateMachine,
						transition = transition,
						orderInSrcTransitions = i
					});
				}
			}
		}

		void PasteAsIngoingTransitions(AnimatorState target, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// Because AddTransition() will insert transition into the first element.
			transitionInfos = transitionInfos.Reverse();

			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }

                switch (transitionInfo.sourceStateType) {
                    case SourceStateType.Normal: { 
						List<AnimatorStateTransition> newTransitions = new List<AnimatorStateTransition>(transitionInfo.srcState.transitions);
						// Insert new state into the position of original one.
						newTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateStateTransition(transitionInfo.srcState, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcState, "Add new transitions");
						transitionInfo.srcState.transitions = newTransitions.ToArray();
					}
					break;
                    case SourceStateType.AnyState: {
						List<AnimatorStateTransition> newAnyStateTransitions = new List<AnimatorStateTransition>(transitionInfo.srcStateMachine.anyStateTransitions);
						// Insert new state into the position of original one.
						newAnyStateTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateAnyTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.anyStateTransitions = newAnyStateTransitions.ToArray();
					}
					break;
                    case SourceStateType.EntryState: {
						List<AnimatorTransition> newEntryTransitions = new List<AnimatorTransition>(transitionInfo.srcStateMachine.entryTransitions);
						// Insert new state into the position of original one.
						newEntryTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateEntryTransition(transitionInfo.srcStateMachine, target, transitionInfo.entryTransition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.entryTransitions = newEntryTransitions.ToArray();
					}
					break;
                }
			}
		}
        #endregion

        string GetAnimatorStateFullName(AnimatorState state) {
			return m_AnimatorStateHashToFullName[state.GetInstanceID()];
		}
		string GetSubStateMachineFullName(AnimatorStateMachine subStateMachine) {
			return m_AnimatorStateHashToFullName[subStateMachine.GetInstanceID()];
		}

		#region Static factory of AnimatorStateTransition and AnimatorTransition
		static AnimatorStateTransition CreateStateTransition(AnimatorState srcState, AnimatorState dstState, AnimatorStateTransition template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcState);
			
			CopyAnimatorStateTransition(template, newTransition);
			newTransition.isExit = false;
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorStateTransition CreateExitTransition(AnimatorState srcState, AnimatorStateTransition template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcState);

			CopyAnimatorStateTransition(template, newTransition);
			newTransition.isExit = true;
			return newTransition;
		}

		static AnimatorTransition CreateEntryTransition(AnimatorStateMachine srcStateMachine, AnimatorState dstState, AnimatorTransition template) {
			AnimatorTransition newTransition = CreateDefaultTransition(srcStateMachine);
			CopyAnimatorTransition(template, newTransition);
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorStateTransition CreateAnyTransition(AnimatorStateMachine srcStateMachine, AnimatorState dstState, AnimatorStateTransition template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcStateMachine);
			CopyAnimatorStateTransition(template, newTransition);
			newTransition.destinationState = dstState;
			return newTransition;
		}

		/// <summary>
		/// Create a AnimatorStateTransition and stored in the specific AnimatorState. 
		/// </summary>
		/// <param name="storedState"></param>
		/// <returns></returns>
		static AnimatorStateTransition CreateDefaultStateTransition(AnimatorState storedState) {
			AnimatorStateTransition newTransition = new AnimatorStateTransition();
			// Store in the asset.
			bool flag = AssetDatabase.GetAssetPath(storedState) != "";
			if (flag) {
				AssetDatabase.AddObjectToAsset(newTransition, AssetDatabase.GetAssetPath(storedState));
			}
			newTransition.hideFlags = HideFlags.HideInHierarchy;

			return newTransition;
		}

		/// <summary>
		/// Create a AnimatorStateTransition and stored in the specific AnimatorStateMachine. 
		/// </summary>
		/// <param name="storedStateMachine"></param>
		/// <returns></returns>
		static AnimatorStateTransition CreateDefaultStateTransition(AnimatorStateMachine storedStateMachine) {
			AnimatorStateTransition newTransition = new AnimatorStateTransition();
			// Store in the asset.
			bool flag = AssetDatabase.GetAssetPath(storedStateMachine) != "";
			if (flag) {
				AssetDatabase.AddObjectToAsset(newTransition, AssetDatabase.GetAssetPath(storedStateMachine));
			}
			newTransition.hideFlags = HideFlags.HideInHierarchy;

			return newTransition;
		}

		/// <summary>
		/// Create a AnimatorTransition and stored in the specific AnimatorStateMachine. 
		/// </summary>
		/// <param name="storedStateMachine"></param>
		/// <returns></returns>
		static AnimatorTransition CreateDefaultTransition(AnimatorStateMachine storedStateMachine) {
			AnimatorTransition newTransition = new AnimatorTransition();
			// Store in the asset.
			bool flag = AssetDatabase.GetAssetPath(storedStateMachine) != "";
			if (flag) {
				AssetDatabase.AddObjectToAsset(newTransition, AssetDatabase.GetAssetPath(storedStateMachine));
			}
			newTransition.hideFlags = HideFlags.HideInHierarchy;

			return newTransition;
		}

		static void CopyAnimatorStateTransition(AnimatorStateTransition src, AnimatorStateTransition dst) {
			dst.canTransitionToSelf = src.canTransitionToSelf;
			dst.duration = src.duration;
			dst.exitTime = src.exitTime;
			dst.hasExitTime = src.hasExitTime;
			dst.hasFixedDuration = src.hasFixedDuration;
			dst.hideFlags = src.hideFlags;
			dst.interruptionSource = src.interruptionSource;
			dst.isExit = src.isExit;
			dst.mute = src.mute;
			dst.name = src.name;
			dst.offset = src.offset;
			dst.orderedInterruption = src.orderedInterruption;
			dst.solo = src.solo;
			dst.conditions = src.conditions.ToArray();
		}

		static void CopyAnimatorTransition(AnimatorTransition src, AnimatorTransition dst) {
			dst.isExit = src.isExit;
			dst.hideFlags = src.hideFlags;
			dst.mute = src.mute;
			dst.name = src.name;
			dst.solo = src.solo;
			dst.conditions = src.conditions.ToArray();
		}
        #endregion
    }
}
