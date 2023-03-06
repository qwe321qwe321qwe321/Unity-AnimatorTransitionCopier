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
			OnAnimatorTransitionCopierGUI();
		}

		AnimatorController m_TargetAnimatorController;
		int m_AnimatorLayerIndex;
		bool m_IgnoreSelfTransitions = true;

		bool m_HasRefreshedAnimatorLayers;
		AnimatorStateMachine m_CurrentLayerStateMachine;
		List<AnimatorState> m_AllAnimatorStatesInLayer = new List<AnimatorState>();
		List<StateMachineInfo> m_AllStateMachinesInLayer = new List<StateMachineInfo>();
		Dictionary<int, string> m_AnimatorStateHashToFullName = new Dictionary<int, string>();

		List<AnimatorStateTransitionInfo> m_CopiedAnimatorStateTransitions = new List<AnimatorStateTransitionInfo>();

		private struct StateMachineInfo {
			public AnimatorStateMachine stateMachine;
			public AnimatorStateMachine parent;

			public StateMachineInfo(AnimatorStateMachine stateMachine, AnimatorStateMachine parent) {
				this.stateMachine = stateMachine;
				this.parent = parent;
			}

			/// <summary>
			/// Get all transitions that comes from this state machine.
			/// </summary>
			/// <param name="srcStateMachine"></param>
			/// <returns></returns>
			public AnimatorTransition[] GetAllTransitionsFromStateMachine() {
				if (parent == null) {
					return new AnimatorTransition[0];
				}

				// GetStateMachineTransitions() usage:
				// https://csharp.hotexamples.com/examples/-/AnimatorStateMachine/-/php-animatorstatemachine-class-examples.html
				return parent.GetStateMachineTransitions(stateMachine);
			}

			public void AddTransition(AnimatorTransitionBase template) {
				if (parent == null) {
					return;
				}

				if (template.destinationState) {
					AnimatorTransition newTransition = parent.AddStateMachineTransition(stateMachine);
					CopyToAnimatorTransition(template, newTransition);
					newTransition.destinationStateMachine = null;
					newTransition.destinationState = template.destinationState;
				} else if (template.destinationStateMachine) {
					AnimatorTransition newTransition = parent.AddStateMachineTransition(stateMachine);
					CopyToAnimatorTransition(template, newTransition);
					newTransition.destinationState = null;
					newTransition.destinationStateMachine = template.destinationStateMachine;
				} else {
					Debug.LogWarning($"Null ref. Trying to make transition to destination that doesn't exist.");
				}
			}

			public void SetStateMachineTransitions(AnimatorTransition[] transitions) {
				if (parent == null) {
					return;
				}
				parent.SetStateMachineTransitions(stateMachine, transitions);
			}
		}

		bool IsCurrentLayerValid => m_TargetAnimatorController != null && m_AnimatorLayerIndex < m_TargetAnimatorController.layers.Length;

		enum SourceType {
			NormalState = 0,
			AnyState = 1,
			EntryState = 2,
			StateMachine = 3,
		}
		struct AnimatorStateTransitionInfo {
			public SourceType sourceStateType;
			public AnimatorState srcState;
			public AnimatorStateMachine srcStateMachine;

			public AnimatorTransitionBase transition;

			public int orderInSrcTransitions;

			public bool IsValid() {
				switch (sourceStateType) {
					case SourceType.NormalState:
						if (!srcState || !transition) {
							return false;
						}
						break;
					case SourceType.AnyState:
						if (!srcStateMachine || !transition) { // any state doesn't have srcState.
							return false;
						}
						break;
					case SourceType.EntryState:
						if (!srcStateMachine || !transition) {
							return false;
						}
						break;
					case SourceType.StateMachine:
						if (!srcStateMachine || !transition) {
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
					case SourceType.NormalState:
						if (transition.isExit) {
							return $"{srcState.name}->Exit State";
						}

						return $"{srcState.name}->{GetDestinationName(transition)}";
					case SourceType.AnyState:
						return $"Any State->{GetDestinationName(transition)}";
					case SourceType.EntryState:
						return $"Entry State({srcStateMachine.name})->{GetDestinationName(transition)}";
					case SourceType.StateMachine:
						return $"{srcStateMachine.name}->{GetDestinationName(transition)}";
				}

				return "<unknown_type_transitions>";
			}
		}

		void OnAnimatorTransitionCopierGUI() {
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
				// Selected transitions.
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

				// Selected state/stateMachine for copying.
				UnityEngine.Object selectedAcitveStateOrMachine = Selection.activeObject;
				if (selectedAcitveStateOrMachine is AnimatorState ||
					selectedAcitveStateOrMachine is AnimatorStateMachine) {
					RefreshCurrentStatesInLayer();
					bool isValidState = m_AnimatorStateHashToFullName.ContainsKey(selectedAcitveStateOrMachine.GetInstanceID());
					if (IsCurrentLayerValid && !isValidState) {
						EditorGUILayout.HelpBox($"Selected state is not in {m_TargetAnimatorController.name}.{m_TargetAnimatorController.layers[m_AnimatorLayerIndex].name}", MessageType.Warning);
					}
					using (new EditorGUI.DisabledGroupScope(!isValidState)) {

						EditorGUIHelper.LabelField($"Copy Transitions from {selectedAcitveStateOrMachine.name}", FontStyle.Bold);
						EditorGUI.indentLevel++;
						using (new EditorGUILayout.HorizontalScope()) {
							if (EditorGUIHelper.Button("Copy all *Ingoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								if (selectedAcitveStateOrMachine is AnimatorState selectedAnimatorState) {
									CopyIngoingTransitions(selectedAnimatorState, m_CopiedAnimatorStateTransitions);
								} else if (selectedAcitveStateOrMachine is AnimatorStateMachine selectedAnimatorStateMachine) {
									CopyIngoingTransitions(selectedAnimatorStateMachine, m_CopiedAnimatorStateTransitions);
								}


							}

							if (EditorGUIHelper.Button("Copy all *Outgoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								if (selectedAcitveStateOrMachine is AnimatorState selectedAnimatorState) {
									CopyOutgoingTransitions(selectedAnimatorState, m_CopiedAnimatorStateTransitions);
								} else if (selectedAcitveStateOrMachine is AnimatorStateMachine selectedAnimatorStateMachine) {
									CopyOutgoingTransitions(selectedAnimatorStateMachine, m_CopiedAnimatorStateTransitions);
								}
							}
						}
						EditorGUI.indentLevel--;
					}
				}

				// Selected state for pasting.
				AnimatorState[] selectedStates = Selection.GetFiltered<AnimatorState>(SelectionMode.Unfiltered);
				AnimatorStateMachine[] selectedStateMachines = Selection.GetFiltered<AnimatorStateMachine>(SelectionMode.Unfiltered);
				if (selectedStates.Length > 0 || selectedStateMachines.Length > 0) {
					bool canPasteTransitions = m_CopiedAnimatorStateTransitions != null &&
						m_CopiedAnimatorStateTransitions.Count > 0;
					EditorGUIHelper.LabelField("Paste Transitions to state", FontStyle.Bold);
					EditorGUI.indentLevel++;
					m_IgnoreSelfTransitions = EditorGUILayout.Toggle("Ignore self transitions", m_IgnoreSelfTransitions);
					using (new EditorGUI.DisabledGroupScope(!canPasteTransitions)) {
						using (new EditorGUILayout.HorizontalScope()) {
							// Try to multi-paste
							if (EditorGUIHelper.Button("Paste as *Ingoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								PasteAsIngoingTransitions(selectedStates, m_CopiedAnimatorStateTransitions);
								PasteAsIngoingTransitions(selectedStateMachines, m_CopiedAnimatorStateTransitions);

								Debug.Log($"Pasted {m_CopiedAnimatorStateTransitions.Count} ingoing transitions.");
							}
							if (EditorGUIHelper.Button("Paste as *Outgoing* transitions", EditorGUIHelper.EditorButtonSize.Large)) {
								PasteAsOutgoingTransitions(selectedStates, m_CopiedAnimatorStateTransitions);
								PasteAsOutgoingTransitions(selectedStateMachines, m_CopiedAnimatorStateTransitions);

								Debug.Log($"Pasted {m_CopiedAnimatorStateTransitions.Count} outgoing transitions to {selectedStates.Length} states and {selectedStateMachines} state machines.");
							}
						}
					}
					EditorGUI.indentLevel--;
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
			// Including the root state machine of this layer.
			m_AllStateMachinesInLayer.Add(new StateMachineInfo(m_CurrentLayerStateMachine, null));
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
				m_AllStateMachinesInLayer.Add(new StateMachineInfo(subStateMachine, stateMachine));
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
				if (!transition.isExit && !transition.destinationState && !transition.destinationStateMachine) {
					Debug.LogWarning($"Undefined transition type: {transition}");
					info = default;
					return false;
				}
				// Normal transition.
				foreach (var state in m_AllAnimatorStatesInLayer) {
					for (int i = 0; i < state.transitions.Length; i++) {
						if (transition.GetInstanceID() == state.transitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceType.NormalState,
								srcState = state,
								transition = transition,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}

				// Transitions from a AnyState.
				foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
					for (int i = 0; i < stateMachineInfo.stateMachine.anyStateTransitions.Length; i++) {
						if (transition.GetInstanceID() == stateMachineInfo.stateMachine.anyStateTransitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceType.AnyState,
								transition = transition,
								srcStateMachine = stateMachineInfo.stateMachine,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}
			}

			// generic transition is from EntryState or StateMachine.
			// It looks grey in the Animator window.
			if (transition is AnimatorTransition genericTransition) {
				if (!transition.isExit && !transition.destinationState && !transition.destinationStateMachine) {
					Debug.LogWarning($"Undefined transition type: {transition}");
					info = default;
					return false;
				}

				// Transitions from a EntryState.
				foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
					for (int i = 0; i < stateMachineInfo.stateMachine.entryTransitions.Length; i++) {
						if (transition.GetInstanceID() == stateMachineInfo.stateMachine.entryTransitions[i].GetInstanceID()) {
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceType.EntryState,
								srcStateMachine = stateMachineInfo.stateMachine,
								transition = transition,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}

				// Transitions from a StateMachine.
				foreach (var srcStateMachineInfo in m_AllStateMachinesInLayer) {
					var stateMachineTransitions = srcStateMachineInfo.GetAllTransitionsFromStateMachine();
					for (int i = 0; i < stateMachineTransitions.Length; i++) {
						if (transition.GetInstanceID() == stateMachineTransitions[i].GetInstanceID()) {
							//Debug.Log($"Found transition: {srcStateMachine} -> {transition.destinationStateMachine}{transition.destinationState}");
							info = new AnimatorStateTransitionInfo() {
								sourceStateType = SourceType.StateMachine,
								srcStateMachine = srcStateMachineInfo.stateMachine,
								transition = transition,
								orderInSrcTransitions = i
							};
							return true;
						}
					}
				}
				foreach (var layer in m_AllStateMachinesInLayer) {

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
				if (!target.transitions[i].isExit &&
					!target.transitions[i].destinationState &&
					!target.transitions[i].destinationStateMachine) {
					// Unknown transition.
					continue;
				}
				copyTo.Add(new AnimatorStateTransitionInfo() {
					sourceStateType = SourceType.NormalState,
					srcState = target,
					transition = target.transitions[i],
					orderInSrcTransitions = i
				});
			}
		}

		void CopyOutgoingTransitions(AnimatorStateMachine target, List<AnimatorStateTransitionInfo> copyTo) {
			if (!TryGetStateMachineInfo(target, out var stateMachineInfo)) {
				Debug.LogWarning($"Failed: Cannot find {target} in current layer.");
				return;
			}

			copyTo.Clear();
			var transitions = stateMachineInfo.GetAllTransitionsFromStateMachine();
			for (int i = 0; i < transitions.Length; i++) {
				if (!transitions[i].isExit &&
					!transitions[i].destinationState &&
					!transitions[i].destinationStateMachine) {
					// Unknown transition.
					continue;
				}
				copyTo.Add(new AnimatorStateTransitionInfo() {
					sourceStateType = SourceType.StateMachine,
					srcStateMachine = target,
					transition = transitions[i],
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
					if (m_IgnoreSelfTransitions) {
						if (transitionInfo.transition.destinationState != null &&
							transitionInfo.transition.destinationState.GetInstanceID() == target.GetInstanceID()) {
							continue;
						}
					}

					target.AddTransition(CreateStateTransition(target, transitionInfo.transition));
				}
			}
		}

		void PasteAsOutgoingTransitions(AnimatorStateMachine targetStateMachine, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// AddTransition() has already supported undo.
			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }
				if (!TryGetStateMachineInfo(targetStateMachine, out var stateMachineInfo)) {
					Debug.LogWarning($"Failed: Cannot find {targetStateMachine} in current layer.");
					continue;

				}

				if (transitionInfo.transition.isExit) {
					stateMachineInfo.AddTransition(transitionInfo.transition);
				} else {
					if (m_IgnoreSelfTransitions) {
						if (transitionInfo.transition.destinationStateMachine != null &&
							transitionInfo.transition.destinationStateMachine.GetInstanceID() == targetStateMachine.GetInstanceID()) {
							continue;
						}
					}

					stateMachineInfo.AddTransition(transitionInfo.transition);
				}
			}
		}

		void PasteAsOutgoingTransitions(AnimatorState[] states, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			foreach (var state in states) {
				if (null == state) { continue; }
				PasteAsOutgoingTransitions(state, transitionInfos);
			}
		}

		void PasteAsOutgoingTransitions(AnimatorStateMachine[] stateMachines, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			foreach (var stateMachine in stateMachines) {
				if (null == stateMachine) { continue; }
				PasteAsOutgoingTransitions(stateMachine, transitionInfos);
			}
		}


		void CopyIngoingTransitions(AnimatorState target, List<AnimatorStateTransitionInfo> copyTo) {
			copyTo.Clear();
			foreach (var state in m_AllAnimatorStatesInLayer) {
				for (int i = 0; i < state.transitions.Length; i++) {
					var transition = state.transitions[i];
					if (transition.destinationState == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.NormalState,
							srcState = state,
							transition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}

			foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
				// Entry transitions
				for (int i = 0; i < stateMachineInfo.stateMachine.entryTransitions.Length; i++) {
					var transition = stateMachineInfo.stateMachine.entryTransitions[i];
					if (transition.destinationState == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.EntryState,
							srcStateMachine = stateMachineInfo.stateMachine,
							transition = transition,
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
						sourceStateType = SourceType.AnyState,
						srcStateMachine = m_CurrentLayerStateMachine,
						transition = transition,
						orderInSrcTransitions = i
					});
				}
			}

			// StateMachine transitions.
			foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
				var stateMachineTransitions = stateMachineInfo.GetAllTransitionsFromStateMachine();
				for (int i = 0; i < stateMachineTransitions.Length; i++) {
					var transition = stateMachineTransitions[i];
					if (transition.destinationState == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.StateMachine,
							srcStateMachine = stateMachineInfo.stateMachine,
							transition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}
		}

		void CopyIngoingTransitions(AnimatorStateMachine target, List<AnimatorStateTransitionInfo> copyTo) {
			copyTo.Clear();
			foreach (var state in m_AllAnimatorStatesInLayer) {
				for (int i = 0; i < state.transitions.Length; i++) {
					var transition = state.transitions[i];
					if (transition.destinationStateMachine == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.NormalState,
							srcState = state,
							transition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}

			foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
				// Entry transitions
				for (int i = 0; i < stateMachineInfo.stateMachine.entryTransitions.Length; i++) {
					var transition = stateMachineInfo.stateMachine.entryTransitions[i];
					if (transition.destinationStateMachine == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.EntryState,
							srcStateMachine = stateMachineInfo.stateMachine,
							transition = transition,
							orderInSrcTransitions = i
						});
					}
				}
			}

			// AnyState transitions
			for (int i = 0; i < m_CurrentLayerStateMachine.anyStateTransitions.Length; i++) {
				var transition = m_CurrentLayerStateMachine.anyStateTransitions[i];
				if (transition.destinationStateMachine == target) {
					copyTo.Add(new AnimatorStateTransitionInfo() {
						sourceStateType = SourceType.AnyState,
						srcStateMachine = m_CurrentLayerStateMachine,
						transition = transition,
						orderInSrcTransitions = i
					});
				}
			}

			// StateMachine transitions.
			foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
				var stateMachineTransitions = stateMachineInfo.GetAllTransitionsFromStateMachine();
				for (int i = 0; i < stateMachineTransitions.Length; i++) {
					var transition = stateMachineTransitions[i];
					if (transition.destinationStateMachine == target) {
						copyTo.Add(new AnimatorStateTransitionInfo() {
							sourceStateType = SourceType.StateMachine,
							srcStateMachine = stateMachineInfo.stateMachine,
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

				if (m_IgnoreSelfTransitions) {
					if (transitionInfo.transition.destinationState != null &&
						transitionInfo.transition.destinationState.GetInstanceID() == target.GetInstanceID()) {
						continue;
					}
				}

				switch (transitionInfo.sourceStateType) {
					case SourceType.NormalState: {
						List<AnimatorStateTransition> newTransitions = new List<AnimatorStateTransition>(transitionInfo.srcState.transitions);
						// Insert new state into the position of original one.
						newTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateStateTransition(transitionInfo.srcState, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcState, "Add new transitions");
						transitionInfo.srcState.transitions = newTransitions.ToArray();
					}
					break;
					case SourceType.AnyState: {
						List<AnimatorStateTransition> newAnyStateTransitions = new List<AnimatorStateTransition>(transitionInfo.srcStateMachine.anyStateTransitions);
						// Insert new state into the position of original one.
						newAnyStateTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateAnyTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.anyStateTransitions = newAnyStateTransitions.ToArray();
					}
					break;
					case SourceType.EntryState: {
						List<AnimatorTransition> newEntryTransitions = new List<AnimatorTransition>(transitionInfo.srcStateMachine.entryTransitions);
						// Insert new state into the position of original one.
						newEntryTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateEntryTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.entryTransitions = newEntryTransitions.ToArray();
					}
					break;
					case SourceType.StateMachine: {
						if (!TryGetStateMachineInfo(transitionInfo.srcStateMachine, out var srcStateMachineInfo)) {
							Debug.LogWarning($"Failed: Cannot find {transitionInfo.srcStateMachine} in current layer.");
							return;
						}
						List<AnimatorTransition> newStateMachineTransitions = new List<AnimatorTransition>(srcStateMachineInfo.GetAllTransitionsFromStateMachine());
						// Insert new state into the position of original one.
						newStateMachineTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateStateMachineTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						// Set its state machine outgoing transitions.
						srcStateMachineInfo.SetStateMachineTransitions(newStateMachineTransitions.ToArray());
					}
					break;
				}
			}
		}

		void PasteAsIngoingTransitions(AnimatorStateMachine target, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			// Because AddTransition() will insert transition into the first element.
			transitionInfos = transitionInfos.Reverse();

			foreach (var transitionInfo in transitionInfos) {
				if (!transitionInfo.IsValid()) { continue; }

				if (m_IgnoreSelfTransitions) {
					if (transitionInfo.transition.destinationState != null &&
						transitionInfo.transition.destinationState.GetInstanceID() == target.GetInstanceID()) {
						continue;
					}
				}

				switch (transitionInfo.sourceStateType) {
					case SourceType.NormalState: {
						List<AnimatorStateTransition> newTransitions = new List<AnimatorStateTransition>(transitionInfo.srcState.transitions);
						// Insert new state into the position of original one.
						newTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateStateTransition(transitionInfo.srcState, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcState, "Add new transitions");
						transitionInfo.srcState.transitions = newTransitions.ToArray();
					}
					break;
					case SourceType.AnyState: {
						List<AnimatorStateTransition> newAnyStateTransitions = new List<AnimatorStateTransition>(transitionInfo.srcStateMachine.anyStateTransitions);
						// Insert new state into the position of original one.
						newAnyStateTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateAnyTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.anyStateTransitions = newAnyStateTransitions.ToArray();
					}
					break;
					case SourceType.EntryState: {
						List<AnimatorTransition> newEntryTransitions = new List<AnimatorTransition>(transitionInfo.srcStateMachine.entryTransitions);
						// Insert new state into the position of original one.
						newEntryTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateEntryTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						transitionInfo.srcStateMachine.entryTransitions = newEntryTransitions.ToArray();
					}
					break;
					case SourceType.StateMachine: {
						if (!TryGetStateMachineInfo(transitionInfo.srcStateMachine, out var srcStateMachineInfo)) {
							Debug.LogWarning($"Failed: Cannot find {transitionInfo.srcStateMachine} in current layer.");
							return;
						}
						List<AnimatorTransition> newStateMachineTransitions = new List<AnimatorTransition>(srcStateMachineInfo.GetAllTransitionsFromStateMachine());
						// Insert new state into the position of original one.
						newStateMachineTransitions.Insert(transitionInfo.orderInSrcTransitions, CreateStateMachineTransition(transitionInfo.srcStateMachine, target, transitionInfo.transition));
						// Record undo before replace transitions.
						Undo.RegisterCompleteObjectUndo(transitionInfo.srcStateMachine, "Add new transitions");
						// Set its state machine outgoing transitions.
						srcStateMachineInfo.SetStateMachineTransitions(newStateMachineTransitions.ToArray());
					}
					break;
				}
			}
		}

		void PasteAsIngoingTransitions(AnimatorState[] states, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			foreach (var state in states) {
				if (null == state) { continue; }
				PasteAsIngoingTransitions(state, transitionInfos);
			}
		}

		void PasteAsIngoingTransitions(AnimatorStateMachine[] stateMachines, IEnumerable<AnimatorStateTransitionInfo> transitionInfos) {
			foreach (var stateMachine in stateMachines) {
				if (null == stateMachine) { continue; }
				PasteAsIngoingTransitions(stateMachine, transitionInfos);
			}
		}
		#endregion

		string GetAnimatorStateFullName(AnimatorState state) {
			return m_AnimatorStateHashToFullName[state.GetInstanceID()];
		}
		string GetSubStateMachineFullName(AnimatorStateMachine subStateMachine) {
			return m_AnimatorStateHashToFullName[subStateMachine.GetInstanceID()];
		}

		bool TryGetStateMachineInfo(AnimatorStateMachine stateMachine, out StateMachineInfo found) {
			foreach (var stateMachineInfo in m_AllStateMachinesInLayer) {
				if (stateMachineInfo.stateMachine == stateMachine) {
					found = stateMachineInfo;
					return true;
				}
			}
			found = default;
			return false;
		}

		#region Static factory of AnimatorStateTransition and AnimatorTransition
		static AnimatorStateTransition CreateStateTransition(AnimatorState srcState, AnimatorTransitionBase template) {
			if (template.destinationState) {
				return CreateStateTransition(srcState, template.destinationState, template);
			}
			if (template.destinationStateMachine) {
				return CreateStateTransition(srcState, template.destinationStateMachine, template);
			}

			Debug.LogWarning($"Null ref. Trying to make transition to destination that doesn't exist.");
			return null;
		}

		static AnimatorStateTransition CreateStateTransition(AnimatorState srcState, AnimatorState dstState, AnimatorTransitionBase template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcState);
			CopyToAnimatorStateTransition(template, newTransition);

			newTransition.isExit = false;
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorStateTransition CreateStateTransition(AnimatorState srcState, AnimatorStateMachine dstStateMachine, AnimatorTransitionBase template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcState);
			CopyToAnimatorStateTransition(template, newTransition);

			newTransition.isExit = false;
			newTransition.destinationState = null;
			newTransition.destinationStateMachine = dstStateMachine;
			return newTransition;
		}

		static AnimatorTransition CreateStateMachineTransition(AnimatorStateMachine srcStateMachine, AnimatorState dstState, AnimatorTransitionBase template) {
			AnimatorTransition newTransition = CreateDefaultTransition(srcStateMachine);
			CopyToAnimatorTransition(template, newTransition);

			newTransition.isExit = false;
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorTransition CreateStateMachineTransition(AnimatorStateMachine srcStateMachine, AnimatorStateMachine dstStateMachine, AnimatorTransitionBase template) {
			AnimatorTransition newTransition = CreateDefaultTransition(srcStateMachine);
			CopyToAnimatorTransition(template, newTransition);

			newTransition.isExit = false;
			newTransition.destinationState = null;
			newTransition.destinationStateMachine = dstStateMachine;
			return newTransition;
		}

		/// <summary>
		/// Create a exit state transition from another.
		/// </summary>
		/// <param name="srcState"></param>
		/// <param name="template"></param>
		/// <returns></returns>
		static AnimatorStateTransition CreateExitTransition(AnimatorState srcState, AnimatorTransitionBase template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcState);

			CopyToAnimatorStateTransition(template, newTransition);
			newTransition.isExit = true;
			return newTransition;
		}

		static AnimatorTransition CreateEntryTransition(AnimatorStateMachine srcStateMachine, AnimatorState dstState, AnimatorTransitionBase template) {
			AnimatorTransition newTransition = CreateDefaultTransition(srcStateMachine);
			CopyToAnimatorTransition(template, newTransition);
			newTransition.isExit = false;
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorTransition CreateEntryTransition(AnimatorStateMachine srcStateMachine, AnimatorStateMachine dstStateMachine, AnimatorTransitionBase template) {
			AnimatorTransition newTransition = CreateDefaultTransition(srcStateMachine);
			CopyToAnimatorTransition(template, newTransition);
			newTransition.isExit = false;
			newTransition.destinationState = null;
			newTransition.destinationStateMachine = dstStateMachine;
			return newTransition;
		}

		static AnimatorStateTransition CreateAnyTransition(AnimatorStateMachine srcStateMachine, AnimatorState dstState, AnimatorTransitionBase template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcStateMachine);
			CopyToAnimatorStateTransition(template, newTransition);
			newTransition.isExit = false;
			newTransition.destinationState = dstState;
			return newTransition;
		}

		static AnimatorStateTransition CreateAnyTransition(AnimatorStateMachine srcStateMachine, AnimatorStateMachine dstStateMachine, AnimatorTransitionBase template) {
			AnimatorStateTransition newTransition = CreateDefaultStateTransition(srcStateMachine);
			CopyToAnimatorStateTransition(template, newTransition);
			newTransition.isExit = false;
			newTransition.destinationState = null;
			newTransition.destinationStateMachine = dstStateMachine;
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

		static void CopyToAnimatorStateTransition(AnimatorTransitionBase src, AnimatorStateTransition dst) {
			if (src is AnimatorStateTransition srcStateTrans) {
				CopyAnimatorStateTransition(srcStateTrans, dst);
			} else if (src is AnimatorTransition srcTrans) {
				CopyAnimatorStateTransition(srcTrans, dst);
			}
		}

		static void CopyToAnimatorTransition(AnimatorTransitionBase src, AnimatorTransition dst) {
			if (src is AnimatorStateTransition srcStateTrans) {
				CopyAnimatorTransition(srcStateTrans, dst);
			} else if (src is AnimatorTransition srcTrans) {
				CopyAnimatorTransition(srcTrans, dst);
			}
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

		static void CopyAnimatorStateTransition(AnimatorTransition src, AnimatorStateTransition dst) {
			dst.isExit = src.isExit;
			dst.hideFlags = src.hideFlags;
			dst.mute = src.mute;
			dst.name = src.name;
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

		static void CopyAnimatorTransition(AnimatorStateTransition src, AnimatorTransition dst) {
			dst.isExit = src.isExit;
			dst.hideFlags = src.hideFlags;
			dst.mute = src.mute;
			dst.name = src.name;
			dst.solo = src.solo;
			dst.conditions = src.conditions.ToArray();
		}

		static string GetDestinationName(AnimatorTransitionBase transition) {
			if (transition.destinationState) {
				return transition.destinationState.name;
			}
			if (transition.destinationStateMachine) {
				return transition.destinationStateMachine.name;
			}
			return "Undefined Destination.";
		}
		#endregion
	}
}
