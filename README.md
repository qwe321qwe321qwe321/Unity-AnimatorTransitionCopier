# Unity-AnimatorTransitionCopier
AnimatorTransitionCopier is a simple tool to copy paste animation transitions in Animator editor.

It helps you to depart from suffering when you have to change animation states in a huge state machine, such as from clip to blend tree.

## Features
* It copys full infomation, including transition settings and conditions.
* Two ways to copy:
    1. Selected transitions
    2. Ingoing/Outgoing transitions of selected state 
* It supports undo/redo as well.

## Preview
### Copy selected transitions
![](./images/copy_selected_transitions.gif)
### Copy all transitions of selected state
![](./images/copy_selected_state.gif)

## Usage
1. Import `Assets/Editor` folder into your project.
2. Open menu: Custom > AnimationTools > Animator Transition Copier.

## Classes
* [AnimatorTransitionCopierWindow.cs](./Assets/Editor/AnimatorTransitionCopierWindow.cs) - Main class.
* [EditorGUIHelper.cs](./Assets/Editor/EditorGUIHelper.cs) - A part of my helper library. It provides useful EditorGUI and GUILayout extensions.
