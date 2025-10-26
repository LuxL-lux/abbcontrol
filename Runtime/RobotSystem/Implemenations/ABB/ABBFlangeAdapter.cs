using System;
using System.Collections;
using System.Collections.Concurrent;
using RobotSystem.Interfaces;
using UnityEngine;

namespace RobotSystem.ABB
{
    /// <summary>
    /// Adapter to integrate with the Preliy Flange Controller system
    /// </summary>
    public class ABBFlangeAdapter : MonoBehaviour, IRobotVisualization
    {
        [Header("Flange Integration")]
        [SerializeField]
        private MonoBehaviour controllerComponent;

        private object flangeController; // Will be cast to Preliy.Flange.Controller
        private System.Reflection.PropertyInfo isValidProperty;
        private System.Reflection.PropertyInfo mechanicalGroupProperty;
        private System.Reflection.MethodInfo setJointsMethod;
        private object mechanicalGroup;

        // Thread-safe queue for joint angle updates
        private ConcurrentQueue<float[]> jointAngleQueue = new ConcurrentQueue<float[]>();

        // Event for requesting joint angles from external systems (future extensibility)
#pragma warning disable 67
        public event Action<float[]> OnJointAnglesRequested;
#pragma warning restore 67

        public bool IsConnected { get; private set; }
        public bool IsValid { get; private set; }
        public string VisualizationType => "Preliy Flange";

        void Start()
        {
            Initialize();
        }

        void Update()
        {
            // Process joint angle updates on the main thread
            while (jointAngleQueue.TryDequeue(out float[] jointAngles))
            {
                TryUpdateJointAnglesMainThread(jointAngles);
            }

            // Check if the Flange controller is still valid
            if (IsConnected && isValidProperty != null)
            {
                try
                {
                    var isValidObject = isValidProperty.GetValue(flangeController);
                    if (isValidObject is bool validFlag)
                    {
                        IsValid = validFlag;
                    }
                    else if (isValidObject != null)
                    {
                        // Handle BoolReactiveProperty or similar
                        var valueProperty = isValidObject.GetType().GetProperty("Value");
                        if (valueProperty != null)
                        {
                            var value = valueProperty.GetValue(isValidObject);
                            if (value is bool boolValue)
                            {
                                IsValid = boolValue;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ABB Flange] Error checking IsValid: {e.Message}");
                    IsValid = false;
                }
            }
        }

        public void Initialize()
        {
            InitializeFlangeController();
        }

        public void Shutdown()
        {
            IsConnected = false;
            IsValid = false;
            flangeController = null;
        }

        private void InitializeFlangeController()
        {
            if (controllerComponent == null)
            {
                Debug.LogError(
                    "[ABB Flange] No Controller component assigned. Please assign the Controller.cs script."
                );
                return;
            }

            try
            {
                // Use reflection to work with Preliy.Flange.Controller without direct dependency
                var controllerType = controllerComponent.GetType();

                if (controllerType.Name != "Controller")
                {
                    Debug.LogError(
                        $"[ABB Flange] Component {controllerType.Name} is not the expected Controller script. Please assign Controller.cs component."
                    );
                    return;
                }

                flangeController = controllerComponent;

                // Get IsValid property
                isValidProperty = controllerType.GetProperty("IsValid");
                if (isValidProperty == null)
                {
                    Debug.LogError("[ABB Flange] IsValid property not found on Controller");
                    return;
                }

                // Get MechanicalGroup property
                mechanicalGroupProperty = controllerType.GetProperty("MechanicalGroup");
                if (mechanicalGroupProperty == null)
                {
                    Debug.LogError("[ABB Flange] MechanicalGroup property not found on Controller");
                    return;
                }

                mechanicalGroup = mechanicalGroupProperty.GetValue(flangeController);
                if (mechanicalGroup == null)
                {
                    Debug.LogError("[ABB Flange] MechanicalGroup is null");
                    return;
                }

                // Get SetJoints method on MechanicalGroup
                var mechanicalGroupType = mechanicalGroup.GetType();
                setJointsMethod = mechanicalGroupType.GetMethod("SetJoints");
                if (setJointsMethod == null)
                {
                    Debug.LogError("[ABB Flange] SetJoints method not found on MechanicalGroup");
                    return;
                }

                IsConnected = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Flange] Failed to initialize Flange Controller: {e.Message}");
                IsConnected = false;
            }
        }

        public void UpdateJointAngles(float[] jointAngles)
        {
            if (jointAngles == null || jointAngles.Length < 6)
            {
                return;
            }

            // Queue the joint angles for processing on the main thread
            jointAngleQueue.Enqueue(jointAngles);
        }

        public bool TryUpdateJointAngles(float[] jointAngles)
        {
            if (jointAngles == null || jointAngles.Length < 6)
            {
                return false;
            }

            // Queue the joint angles for processing on the main thread
            jointAngleQueue.Enqueue(jointAngles);
            return true;
        }

        private bool TryUpdateJointAnglesMainThread(float[] jointAngles)
        {
            if (!IsConnected || !IsValid || jointAngles == null || jointAngles.Length < 6)
            {
                return false;
            }

            try
            {
                // Create JointTarget using reflection (using degrees as confirmed by previous debug output)
                var jointTargetType = System.Type.GetType(
                    "Preliy.Flange.JointTarget, Preliy.Flange"
                );
                if (jointTargetType == null)
                {
                    Debug.LogError("[ABB Flange] JointTarget type not found");
                    return false;
                }

                var jointTarget = System.Activator.CreateInstance(
                    jointTargetType,
                    new object[] { jointAngles }
                );
                if (jointTarget == null)
                {
                    Debug.LogError("[ABB Flange] Failed to create JointTarget");
                    return false;
                }

                // Call SetJoints(jointTarget, notify: true) - now on main thread
                setJointsMethod.Invoke(mechanicalGroup, new object[] { jointTarget, true });

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Flange] Failed to update joint angles: {e.Message}");
                return false;
            }
        }
    }
}

