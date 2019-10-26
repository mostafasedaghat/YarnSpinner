﻿/*

The MIT License (MIT)

Copyright (c) 2015-2017 Secret Lab Pty. Ltd. and Yarn Spinner contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CsvHelper;
using System;

namespace Yarn.Unity
{

    [System.Serializable]
    public class LocalisedStringGroup {
        public SystemLanguage language;
        public TextAsset[] stringFiles;
    }

    /// DialogueRunners act as the interface between your game and YarnSpinner.
    /** Make our menu item slightly nicer looking */
    [AddComponentMenu("Scripts/Yarn Spinner/Dialogue Runner")]
    public class DialogueRunner : MonoBehaviour
    {
        /// The source files to load the conversation from
        public YarnProgram[] yarnScripts;

        /// Our variable storage
        public Yarn.Unity.VariableStorageBehaviour variableStorage;

        /// The object that will handle the actual display and user input
        public Yarn.Unity.DialogueUIBehaviour dialogueUI;

        /// Which node to start from
        public string startNode = Yarn.Dialogue.DEFAULT_START;

        /// Whether we should start dialogue when the scene starts
        public bool startAutomatically = true;

        /// Tests to see if the dialogue is running
        public bool isDialogueRunning { get; private set; }

        public bool automaticCommands = true;

        private System.Action _continue;

        private System.Action<int> _selectAction;

        private HashSet<string> _visitedNodes = new HashSet<string>();
        
        /// Our conversation engine
        /** Automatically created on first access
         */
        private Dialogue _dialogue;
        public Dialogue dialogue {
            get {
                if (_dialogue == null) {
                    // Create the main Dialogue runner, and pass our variableStorage to it
                    _dialogue = new Yarn.Dialogue (variableStorage);

                    // Set up the logging system.
                    _dialogue.LogDebugMessage = delegate (string message) {
                        Debug.Log (message);
                    };
                    _dialogue.LogErrorMessage = delegate (string message) {
                        Debug.LogError (message);
                    };

                    _dialogue.library.RegisterFunction("visited", 1, delegate(Yarn.Value[] parameters) {
                        var nodeName = parameters[0];
                        return _visitedNodes.Contains(nodeName.AsString);
                    });

                    _dialogue.lineHandler = HandleLine;
                    _dialogue.commandHandler = HandleCommand;
                    _dialogue.optionsHandler = HandleOptions;
                    _dialogue.nodeCompleteHandler = HandleNodeComplete;
                    _dialogue.dialogueCompleteHandler = HandleDialogueComplete;
                    
                }
                return _dialogue;
            }
        }

        private void HandleDialogueComplete()
        {
            isDialogueRunning = false;
            this.dialogueUI.DialogueComplete();
        }

        private Dialogue.HandlerExecutionType HandleNodeComplete(string completedNodeName)
        {
            Debug.Log("Node complete: " + completedNodeName);
            _visitedNodes.Add(completedNodeName);
            return this.dialogueUI.NodeComplete(completedNodeName, _continue);            
        }

        private void HandleOptions(OptionSet options)
        {
            this.dialogueUI.RunOptions(options, _selectAction);
        }

        private Dialogue.HandlerExecutionType HandleCommand(Command command)
        {
            (bool wasValidCommand, bool wasCoroutine) = DispatchCommand(command.Text);
            
            if (wasValidCommand) {
                if (wasCoroutine) {
                    // We're currently waiting for the coroutine to
                    // complete, which will take at least one frame to do.
                    return Dialogue.HandlerExecutionType.PauseExecution;
                } else {
                    // The command was not a coroutine, and invoked
                    // immediately. We therefore don't need to wait.
                    return Dialogue.HandlerExecutionType.ContinueExecution;
                }
            } else {
                // We didn't find a method in our C# code to invoke. Pass
                // it to the UI to handle.
                return this.dialogueUI.RunCommand(command, _continue);            
            }

            
        }

        private Dialogue.HandlerExecutionType HandleLine(Line line)
        {
            return this.dialogueUI.RunLine (line, _continue);            
        }

        /// Start the dialogue
        void Start ()
        {
            // Ensure that we have our Implementation object
            if (dialogueUI == null) {
                Debug.LogError ("Implementation was not set! Can't run the dialogue!");
                return;
            }

            // And that we have our variable storage object
            if (variableStorage == null) {
                Debug.LogError ("Variable storage was not set! Can't run the dialogue!");
                return;
            }

            // Ensure that the variable storage has the right stuff in it
            variableStorage.ResetToDefaults ();

            // Combine all scripts together and load them
            if (yarnScripts != null && yarnScripts.Length > 0) {
                
                var compiledPrograms = new List<Program>();

                foreach (var program in yarnScripts) {
                    compiledPrograms.Add(program.GetProgram());
                }

                var combinedProgram = Program.Combine(compiledPrograms.ToArray());
                
                dialogue.SetProgram(combinedProgram);
            }

            _continue = this.ContinueDialogue;

            _selectAction = this.SelectedOption;

            if (startAutomatically) {
                StartDialogue();
            }
            
        }

        internal void AddProgram(YarnProgram scriptToLoad)
        {
            this.dialogue.AddProgram(scriptToLoad.GetProgram());
        }

        private void SelectedOption(int obj)
        {
            this.dialogue.SetSelectedOption(obj);
            ContinueDialogue();
        }

        /// Destroy the variable store and start again
        public void ResetDialogue ()
        {
            variableStorage.ResetToDefaults ();
            StartDialogue ();
        }

        /// Start the dialogue
        public void StartDialogue () {
            StartDialogue(startNode);
        }

        /// Start the dialogue from a given node
        public void StartDialogue (string startNode)
        {

            // Stop any processes that might be running already
            dialogueUI.StopAllCoroutines ();

            // Get it going
            RunDialogue (startNode);
        }

        private void ContinueDialogue()
        {
            
            this.dialogue.Continue();

            // No more results! The dialogue is done.
            //yield return StartCoroutine (this.dialogueUI.DialogueComplete ());

            // Clear the 'is running' flag. We do this after DialogueComplete returns,
            // to allow time for any animations that might run while transitioning
            // out of a conversation (ie letterboxing going away, etc)
            
        }

        void RunDialogue (string startNode = "Start")
        {
            // TODO: Provide handlers, start executing

            // Mark that we're in conversation.
            isDialogueRunning = true;

            // Signal that we're starting up.
            this.dialogueUI.DialogueStarted();

            this.dialogue.SetNode(startNode);

            ContinueDialogue();
            
            
        }

        /// Clear the dialogue system
        public void Clear() {

            if (isDialogueRunning) {
                throw new System.InvalidOperationException("You cannot clear the dialogue system while a dialogue is running.");
            }

            dialogue.UnloadAll();
        }

        /// Stop the dialogue
        public void Stop() {
            isDialogueRunning = false;
            dialogue.Stop();
        }

        /// Test to see if a node name exists
        public bool NodeExists(string nodeName) {
            return dialogue.NodeExists(nodeName);
        }

        /// Return the current node name
        public string currentNodeName {
            get {
                return dialogue.currentNode;
            }
        }


        /// commands that can be automatically dispatched look like this:
        /// COMMANDNAME OBJECTNAME <param> <param> <param> ...
        /** We can dispatch this command if:
         * 1. it has at least 2 words
         * 2. the second word is the name of an object
         * 3. that object has components that have methods with the YarnCommand attribute that have the correct commandString set
         */
        public (bool methodFound, bool isCoroutine) DispatchCommand(string command) {

            var words = command.Split(' ');

            // need 2 parameters in order to have both a command name
            // and the name of an object to find
            if (words.Length < 2)
            {
                return (false, false);                
            }

            var commandName = words[0];

            var objectName = words[1];

            var sceneObject = GameObject.Find(objectName);

            // If we can't find an object, we can't dispatch a command
            if (sceneObject == null)
            {
                return (false, false);                
            }

            int numberOfMethodsFound = 0;
            List<string[]> errorValues = new List<string[]>();

            List<string> parameters;

            if (words.Length > 2) {
                parameters = new List<string>(words);
                parameters.RemoveRange(0, 2);
            } else {
                parameters = new List<string>();
            }

            var startedCoroutine = false;

            // Find every MonoBehaviour (or subclass) on the object
            foreach (var component in sceneObject.GetComponents<MonoBehaviour>()) {
                var type = component.GetType();

                // Find every method in this component
                foreach (var method in type.GetMethods()) {

                    // Find the YarnCommand attributes on this method
                    var attributes = (YarnCommandAttribute[]) method.GetCustomAttributes(typeof(YarnCommandAttribute), true);

                    // Find the YarnCommand whose commandString is equal to the command name
                    foreach (var attribute in attributes) {
                        if (attribute.commandString == commandName) {


                            var methodParameters = method.GetParameters();
                            bool paramsMatch = false;
                            // Check if this is a params array
                            if (methodParameters.Length == 1 && methodParameters[0].ParameterType.IsAssignableFrom(typeof(string[])))
                                {
                                    // Cool, we can send the command!
                                    // Yield if this is a Coroutine
                                    string[][] paramWrapper = new string[1][];
                                    paramWrapper[0] = parameters.ToArray();
                                    if (method.ReturnType == typeof(IEnumerator))
                                    {
                                        StartCoroutine(DoYarnCommand(component, method, paramWrapper)); 
                                        startedCoroutine = true;
                                    }
                                    else
                                    {
                                        method.Invoke(component, paramWrapper);                                        

                                    }
                                    numberOfMethodsFound++;
                                    paramsMatch = true;

                            }
                            // Otherwise, verify that this method has the right number of parameters
                            else if (methodParameters.Length == parameters.Count)
                            {
                                paramsMatch = true;
                                foreach (var paramInfo in methodParameters)
                                {
                                    if (!paramInfo.ParameterType.IsAssignableFrom(typeof(string)))
                                    {
                                        Debug.LogErrorFormat(sceneObject, "Method \"{0}\" wants to respond to Yarn command \"{1}\", but not all of its parameters are strings!", method.Name, commandName);
                                        paramsMatch = false;
                                        break;
                                    }
                                }
                                if (paramsMatch)
                                {
                                    // Cool, we can send the command!
                                    // Yield if this is a Coroutine
                                    if (method.ReturnType == typeof(IEnumerator))
                                    {
                                        StartCoroutine(DoYarnCommand(component, method, parameters.ToArray()));
                                        startedCoroutine = true;
                                    }
                                    else
                                    {
                                        method.Invoke(component, parameters.ToArray());
                                    }
                                    numberOfMethodsFound++;
                                }
                            }
                            //parameters are invalid, but name matches.
                            if (!paramsMatch)
                            {
                                //save this error in case a matching command is never found.
                                errorValues.Add(new string[] { method.Name, commandName, methodParameters.Length.ToString(), parameters.Count.ToString() });
                            }
                        }
                    }
                }
            }

            // Warn if we found multiple things that could respond
            // to this command.
            if (numberOfMethodsFound > 1) {
                Debug.LogWarningFormat(sceneObject, "The command \"{0}\" found {1} targets. " +
                    "You should only have one - check your scripts.", command, numberOfMethodsFound);
            } else if (numberOfMethodsFound == 0) {
                //list all of the near-miss methods only if a proper match is not found, but correctly-named methods are.
                foreach (string[] errorVal in errorValues) {
                    Debug.LogErrorFormat(sceneObject, "Method \"{0}\" wants to respond to Yarn command \"{1}\", but it has a different number of parameters ({2}) to those provided ({3}), or is not a string array!", errorVal[0], errorVal[1], errorVal[2], errorVal[3]);
                }
            }

            var wasValidCommand = numberOfMethodsFound > 0;

            return (wasValidCommand, startedCoroutine);
        }

        IEnumerator DoYarnCommand(MonoBehaviour component, System.Reflection.MethodInfo method, string[][] parameters) {
            // Wait for this command coroutine to complete
            yield return StartCoroutine((IEnumerator)method.Invoke(component, parameters));

            // And then continue running dialogue
            ContinueDialogue();
        }

        IEnumerator DoYarnCommand(MonoBehaviour component, System.Reflection.MethodInfo method, string[] parameters) {
            // Wait for this command coroutine to complete
            yield return StartCoroutine((IEnumerator)method.Invoke(component, parameters));

            // And then continue running dialogue
            ContinueDialogue();
        }
        
    }

    

    /// Apply this attribute to methods in your scripts to expose
    /// them to Yarn.

    /** For example:
     *  [YarnCommand("dosomething")]
     *      void Foo() {
     *         do something!
     *      }
     */
    public class YarnCommandAttribute : System.Attribute
    {
        public string commandString { get; private set; }

        public YarnCommandAttribute(string commandString) {
            this.commandString = commandString;
        }
    }

    /// Scripts that can act as the UI for the conversation should subclass this
    public abstract class DialogueUIBehaviour : MonoBehaviour
    {
        /// A conversation has started.
        public virtual void DialogueStarted() {
            // Default implementation does nothing.            
        }

        /// Display a line.
        public abstract Dialogue.HandlerExecutionType RunLine (Yarn.Line line, System.Action onLineComplete);

        /// Display the options, and call the optionChooser when done.
        public abstract void RunOptions (Yarn.OptionSet optionSet,
                                                System.Action<int> onOptionSelected);

        /// Perform some game-specific command.
        public abstract Dialogue.HandlerExecutionType RunCommand (Yarn.Command command, System.Action onCommandComplete);

        /// The node has ended.
        public virtual Dialogue.HandlerExecutionType NodeComplete(string nextNode, System.Action onComplete) {
            // Default implementation does nothing.  

            return Dialogue.HandlerExecutionType.ContinueExecution;             
        }

        /// The conversation has ended.
        public virtual void DialogueComplete () {
            // Default implementation does nothing.            
        }
    }

    /// Scripts that can act as a variable storage should subclass this
    public abstract class VariableStorageBehaviour : MonoBehaviour, Yarn.VariableStorage
    {
        /// Get a value
        public abstract Value GetValue(string variableName);

        public virtual void SetValue(string variableName, float floatValue)
        {
            Value value = new Yarn.Value(floatValue);
            this.SetValue(variableName, value);
        }
        public virtual void SetValue(string variableName, bool stringValue)
        {
            Value value = new Yarn.Value(stringValue);
            this.SetValue(variableName, value);
        }
        public virtual void SetValue(string variableName, string boolValue)
        {
            Value value = new Yarn.Value(boolValue);
            this.SetValue(variableName, value);
        }

        /// Set a value
        public abstract void SetValue(string variableName, Value value);

        /// Not implemented here
        public virtual void Clear ()
        {
            throw new System.NotImplementedException ();
        }

        public abstract void ResetToDefaults ();

    }

}
