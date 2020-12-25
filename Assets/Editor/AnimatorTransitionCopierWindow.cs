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
		List<AnimatorState> m_AllAnimatorStatesInLayer = new List<AnimatorState>();
		Dictionary<int, string> m_AnimatorStateHashToFullName = new Dictionary<int, string>();

		List<AnimatorStateTransitionInfo> m_CopiedAnimatorStateTransitions = new List<AnimatorStateTransitionInfo>();

		bool IsCurrentLayerValid => m_TargetAnimatorController != null && m_AnimatorLayerIndex < m_TargetAnimatorController.layers.Length;

		struct AnimatorStateTransitionInfo {
			public AnimatorState srcState;
			public AnimatorStateTransition transition;
			public int orderInSrcTransitions;

			public bool IsValid() {
				return srcState && transition;
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
				AnimatorStateTransition[] selectedTransitions = Selection.objects.Select(x => x as AnimatorStateTransition).Where(y => y != null).ToArray();
				bool canCopySelectedTransitions = selectedTransitions.Length > 0;
				if (canCopySelectedTransitions) {
					EditorGUIHelper.LabelField("Copy Transitions", FontStyle.Bold);

					if (EditorGUIHelper.Button("Copy selected transitions", EditorGUIHelper.EditorButtonSize.Large)) {
						RefreshCurrentStatesInLayer();
						m_CopiedAnimatorStateTransitions.Clear();
						foreach (var transition in selectedTransitions) {
							if (!TryGetFullInfoInTransition(transition, out AnimatorStateTransitionInfo info)) {
								Debug.LogWarning($"Selected transition is not in {m_TargetAnimatorController.name}.{m_TargetAnimatorController.layers[m_AnimatorLayerIndex].name}");
								m_CopiedAnimatorStateTransitions.Clear();
								break;
							}
							m_CopiedAnimatorStateTransitions.Add(info);
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
					if (!transitionInfo.IsValid()) {
						hint += "<deleted_transition>\n";
					} else {
						hint += $"{transitionInfo.srcState.name}->{transitionInfo.transition.destinationState.name}\n";
					}
				}
				EditorGUILayout.HelpBox(hint, MessageType.Info);

			}
		}

		void RefreshCurrentStatesInLayer() {
			if (!IsCurrentLayerValid || m_HasRefreshedAnimatorLayers) {
				return;
			}
			AnimatorStateMachine stateMachine = m_TargetAnimatorController.layers[m_AnimatorLayerIndex].stateMachine;
			m_AllAnimatorStatesInLayer.Clear();
			m_AnimatorStateHashToFullName.Clear();
			GetAllStatesInStateMachineRecursively(stateMachine, "");
		}
		void GetAllStatesInStateMachineRecursively(AnimatorStateMachine stateMachine, string prefix) {
			foreach (var state in stateMachine.states) {
				m_AllAnimatorStatesInLayer.Add(state.state);
				if (m_AnimatorStateHashToFullName.ContainsKey(state.state.GetInstanceID())) {
					Debug.LogWarning($"{prefix + state.state.name}, {m_AnimatorStateHashToFullName[state.state.GetInstanceID()]}");
				} else {
					m_AnimatorStateHashToFullName.Add(state.state.GetInstanceID(), prefix + state.state.name);
				}
			}
			foreach (var childStateMachine in stateMachine.stateMachines) {
				string nextPrefix = prefix + childStateMachine.stateMachine.name + ".";
				GetAllStatesInStateMachineRecursively(childStateMachine.stateMachine, nextPrefix);
			}
		}

		bool TryGetFullInfoInTransition(AnimatorStateTransition transition, out AnimatorStateTransitionInfo info) {
			foreach (var state in m_AllAnimatorStatesInLayer) {
				for (int i = 0; i < state.transitions.Length; i++) {
					if (transition.GetInstanceID() == state.transitions[i].GetInstanceID()) {
						info = new AnimatorStateTransitionInfo() {
							srcState = state,
							transition = transition,
							orderInSrcTransitions = i
						};
						return true;
					}
				}
			}
			info = default;
			return false;
		}

		void CopyOutgoingTransitions(AnimatorState target, List<AnimatorStateTransitionInfo> copyTo) {
			copyTo.Clear();
			for (int i = 0; i < target.transitions.Length; i++) {
				copyTo.Add(new AnimatorStateTransitionInfo() {
					srcState = target,
					transition = target.transitions[i],
					orderInSrcTransitions = i
				});
			}
		}

		void PasteAsOutgoingTransitions(AnimatorState target, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// Because AddTransition() will insert transition into the first element.
			transitionInfos = transitionInfos.Reverse();
			// AddTransition() has already supported undo.
			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }
				target.AddTransition(CreateTransition(transitionInfo.transition.destinationState, transitionInfo.transition));
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
		}

		void PasteAsIngoingTransitions(AnimatorState target, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// Because AddTransition() will insert transition into the first element.
			transitionInfos = transitionInfos.Reverse();
			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }
				// Insert new state into the position of original one.
				List<AnimatorStateTransition> newTransitions = new List<AnimatorStateTransition>(transitionInfo.srcState.transitions);
				newTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateTransition(target, transitionInfo.transition));
				// Record undo before replace transitions.
				Undo.RegisterCompleteObjectUndo(transitionInfo.srcState, "Add new transitions");
				transitionInfo.srcState.transitions = newTransitions.ToArray();
			}
		}

		string GetAnimatorStateFullName(AnimatorState state) {
			return m_AnimatorStateHashToFullName[state.GetInstanceID()];
		}

		static AnimatorStateTransition CreateTransition(AnimatorState dstState, AnimatorStateTransition template) {
			AnimatorStateTransition newTransition = new AnimatorStateTransition();
			newTransition.destinationState = dstState;
			newTransition.canTransitionToSelf = template.canTransitionToSelf;
			newTransition.duration = template.duration;
			newTransition.exitTime = template.exitTime;
			newTransition.hasExitTime = template.hasExitTime;
			newTransition.hasFixedDuration = template.hasFixedDuration;
			newTransition.interruptionSource = template.interruptionSource;
			newTransition.isExit = template.isExit;
			newTransition.mute = template.mute;
			newTransition.name = template.name;
			newTransition.offset = template.offset;
			newTransition.orderedInterruption = template.orderedInterruption;
			newTransition.solo = template.solo;
			newTransition.conditions = template.conditions.ToArray();

			return newTransition;
		}
	}
}
