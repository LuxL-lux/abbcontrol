using System;
using System.Collections.Generic;
using UnityEngine;
using RobotSystem.Interfaces;
using Preliy.Flange;

namespace RobotSystem.Core
{
    public class RobotManager : MonoBehaviour
    {
        // Public event for external components to subscribe to state updates
        public event Action<RobotState> OnStateUpdated;
        
        // Specific change events for targeted subscriptions
        public event Action<string, string> OnMotorStateChanged; // (oldState, newState)
        public event Action<string, string> OnModuleChanged; // (oldModule, newModule)
        
        [Header("Robot Connector")]
        [SerializeField] private MonoBehaviour connectorComponent;

        [Header("Visualization Systems")]
        [SerializeField] private List<MonoBehaviour> visualizationComponents = new List<MonoBehaviour>();

        private IRobotConnector robotConnector;
        private List<IRobotVisualization> visualizers = new List<IRobotVisualization>();

        [Header("Status")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private string currentProgram = "";
        [SerializeField] private bool gripperOpen = false;
        [SerializeField] private float[] currentJointAngles = new float[6];
        [SerializeField] private double motionUpdateFreq = 0.0;
        
        // Previous state values for change detection
        private string previousMotorState = "";
        private string previousModule = "";
        
        // Local Flange mode
        private RobotState localRobotState;

        void Start()
        {
            if (connectorComponent != null)
            {
                robotConnector = connectorComponent as IRobotConnector;
                if (robotConnector != null)
                {
                    // Subscribe to events
                    robotConnector.OnConnectionStateChanged += OnConnectionChanged;
                    robotConnector.OnRobotStateUpdated += OnRobotStateUpdated;

                }
                else
                {
                    Debug.LogError($"Component {connectorComponent.GetType().Name} does not implement IRobotConnector");
                }
            }

            // Initialize visualization systems
            InitializeVisualizationSystems();
            
            // Initialize local robot state for Flange mode
            localRobotState = new RobotState();
        }

        void Update()
        {
            // When not connected to robot, read joint data from Flange Controller
            if (!isConnected)
            {
                UpdateFromFlangeController();
            }
        }
        
        void OnDestroy()
        {
            if (robotConnector != null)
            {
                robotConnector.OnConnectionStateChanged -= OnConnectionChanged;
                robotConnector.OnRobotStateUpdated -= OnRobotStateUpdated;
            }

            // Shutdown visualization systems
            foreach (var visualizer in visualizers)
            {
                visualizer.Shutdown();
            }
        }

        private void OnConnectionChanged(bool connected)
        {
            isConnected = connected;
        }

        private void OnRobotStateUpdated(RobotState state)
        {
            // Update UI/status variables
            currentProgram = $"{state.currentModule}.{state.currentRoutine}:{state.currentLine}";
            gripperOpen = state.GripperOpen;
            
            // Check for specific state changes and fire targeted events
            DetectStateChanges(state);
            
            // Trigger public event for external subscribers
            OnStateUpdated?.Invoke(state);

            // Update motion data
            if (state.hasValidJointData)
            {
                currentJointAngles = state.GetJointAngles();
                motionUpdateFreq = state.motionUpdateFrequencyHz;

                // Forward joint angles to all visualization systems
                foreach (var visualizer in visualizers)
                {
                    if (visualizer.IsConnected && visualizer.IsValid)
                    {
                        visualizer.UpdateJointAngles(currentJointAngles);
                    }
                }
            }

            if (state.isRunning && !string.IsNullOrEmpty(state.currentRoutine))
            {
                // Robot is executing a program
            }

            if (state.GripperOpen != gripperOpen)
            {
                // Gripper state changed
            }
        }

        // Public methods that work with any robot type
        public void ConnectToRobot()
        {
            robotConnector?.Connect();
        }

        public void DisconnectFromRobot()
        {
            robotConnector?.Disconnect();
        }

        public RobotState GetCurrentState()
        {
            return robotConnector?.CurrentState;
        }

        public bool IsRobotConnected()
        {
            return robotConnector?.IsConnected ?? false;
        }

        // Convenience methods for joint data access
        public float[] GetCurrentJointAngles()
        {
            return GetCurrentState()?.GetJointAngles() ?? new float[6];
        }

        public bool HasValidMotionData()
        {
            return GetCurrentState()?.hasValidJointData ?? false;
        }

        public double GetMotionUpdateFrequency()
        {
            return GetCurrentState()?.motionUpdateFrequencyHz ?? 0.0;
        }

        private void InitializeVisualizationSystems()
        {
            visualizers.Clear();

            foreach (var component in visualizationComponents)
            {
                if (component != null)
                {
                    var visualizer = component as IRobotVisualization;
                    if (visualizer != null)
                    {
                        visualizer.Initialize();
                        visualizers.Add(visualizer);
                    }
                    else
                    {
                        Debug.LogError($"Component {component.GetType().Name} does not implement IRobotVisualization");
                    }
                }
            }
        }

        public void AddVisualizationSystem(IRobotVisualization visualizer)
        {
            if (visualizer != null && !visualizers.Contains(visualizer))
            {
                visualizer.Initialize();
                visualizers.Add(visualizer);
            }
        }

        public void RemoveVisualizationSystem(IRobotVisualization visualizer)
        {
            if (visualizer != null && visualizers.Contains(visualizer))
            {
                visualizer.Shutdown();
                visualizers.Remove(visualizer);
            }
        }

        public List<IRobotVisualization> GetVisualizationSystems()
        {
            return new List<IRobotVisualization>(visualizers);
        }
        
        private void DetectStateChanges(RobotState state)
        {
            string currentMotorState = state.motorState ?? "";
            string currentModule = state.currentModule ?? "";
            
            // Check for motor state changes
            if (currentMotorState != previousMotorState)
            {
                //Debug.Log($"[Robot Manager] Motor state changed: '{previousMotorState}' → '{currentMotorState}'");
                OnMotorStateChanged?.Invoke(previousMotorState, currentMotorState);
                previousMotorState = currentMotorState;
            }
            
            // Check for module changes
            if (currentModule != previousModule)
            {
                // Debug.Log($"[Robot Manager] Module changed: '{previousModule}' → '{currentModule}'");
                OnModuleChanged?.Invoke(previousModule, currentModule);
                previousModule = currentModule;
            }
        }
        
        private void UpdateFromFlangeController()
        {
            // Find Flange Controller automatically
            var controller = FindFirstObjectByType<Preliy.Flange.Controller>();
            if (controller != null && controller.MechanicalGroup != null)
            {
                // Get joint values from the robot joints
                var robotJoints = controller.MechanicalGroup.RobotJoints;
                if (robotJoints != null && robotJoints.Count >= 6)
                {
                    var jointValues = robotJoints.GetJointValues();
                    
                    // Update local robot state with Flange joint data
                    localRobotState.hasValidJointData = true;
                    localRobotState.jointAngles = jointValues;
                    localRobotState.lastUpdate = DateTime.Now;
                    localRobotState.lastJointUpdate = DateTime.Now;
                    localRobotState.motorState = "LocalMode";
                    
                    // Trigger the same update path as when connected
                    OnRobotStateUpdated(localRobotState);
                }
            }
        }
    }
}