using System;
using UnityEngine;
using Preliy.Flange;

namespace RobotSystem.Core
{
    /// <summary>
    /// Dedicated module for generating RAPID ROBTARGET and JOINTTARGET values
    /// Works independently of RWS connection using Preliy.Flange forward kinematics
    /// Functions in both Edit Mode and Play Mode
    /// </summary>
    [System.Serializable]
    public class RapidTargetGenerator : MonoBehaviour
    {
        [Header("Robot Configuration")]
        [SerializeField] private Robot6RSphericalWrist robot6R;
        [SerializeField] private bool autoFindRobot = true;
        [SerializeField] private bool enableAutoUpdate = true;
        
        [Header("Current RAPID Targets")]
        [SerializeField, TextArea(2, 4)] private string currentJoinTarget = "";
        [SerializeField, TextArea(2, 4)] private string currentRobTarget = "";
        
        [Header("Display Options")]
        [SerializeField] private bool showInInspector = true;
        [SerializeField] private bool logUpdates = false;
        
        // Events for external components
        public event Action<string, string> OnTargetsUpdated; // (joinTarget, robTarget)
        
        private bool isInitialized = false;
        
        void Awake()
        {
            InitializeRapidGenerator();
        }
        
        void Start()
        {
            // Additional initialization in Start for Play Mode
            if (Application.isPlaying)
            {
                InitializeRapidGenerator();
            }
        }
        
        void OnValidate()
        {
            // Update in Edit Mode when inspector values change
            if (!Application.isPlaying)
            {
                InitializeRapidGenerator();
                UpdateTargets();
            }
        }
        
        void OnDestroy()
        {
            if (robot6R != null)
            {
                robot6R.OnJointStateChanged -= OnJointStateChanged;
            }
        }
        
        private void InitializeRapidGenerator()
        {
            // Find robot component if not assigned
            if (autoFindRobot && robot6R == null)
            {
                robot6R = FindFirstObjectByType<Robot6RSphericalWrist>();
                if (robot6R == null)
                {
                    // Try to find in parent or children
                    robot6R = GetComponentInParent<Robot6RSphericalWrist>();
                    if (robot6R == null)
                    {
                        robot6R = GetComponentInChildren<Robot6RSphericalWrist>();
                    }
                }
            }
            
            if (robot6R != null && !isInitialized)
            {
                // Subscribe to joint state changes for automatic updates
                if (enableAutoUpdate)
                {
                    robot6R.OnJointStateChanged += OnJointStateChanged;
                }
                
                // Initial target generation
                UpdateTargets();
                isInitialized = true;
                
                if (logUpdates)
                {
                    Debug.Log($"[RAPID Target Generator] Initialized with robot: {robot6R.name}");
                }
            }
            else if (robot6R == null)
            {
                currentJoinTarget = "Robot6RSphericalWrist component not found";
                currentRobTarget = "Robot6RSphericalWrist component not found";
            }
        }
        
        private void OnJointStateChanged()
        {
            if (enableAutoUpdate && isInitialized)
            {
                UpdateTargets();
            }
        }
        
        /// <summary>
        /// Update RAPID targets based on current robot configuration
        /// Works in both Edit Mode and Play Mode
        /// </summary>
        public void UpdateTargets()
        {
            if (robot6R == null)
            {
                currentJoinTarget = "No Robot6RSphericalWrist found";
                currentRobTarget = "No Robot6RSphericalWrist found";
                return;
            }
            
            try
            {
                // Get current joint angles
                var jointAngles = GetCurrentJointAngles();
                if (jointAngles != null && jointAngles.Length >= 6)
                {
                    // Generate JOINTTARGET
                    currentJoinTarget = FormatJoinTarget(jointAngles);
                    
                    // Calculate forward kinematics for TCP position/orientation
                    var tcpTransform = robot6R.ComputeForward();
                    
                    // Extract position and rotation
                    Vector3 tcpPosition = tcpTransform.GetColumn(3); // Translation column
                    Quaternion tcpRotation = tcpTransform.rotation;  // Rotation
                    
                    // Get robot configuration
                    var robotConfiguration = new Configuration(robot6R.JointValue);
                    
                    // Generate ROBTARGET
                    currentRobTarget = FormatRobTarget(tcpPosition, tcpRotation, robotConfiguration);
                    
                    // Trigger update event
                    OnTargetsUpdated?.Invoke(currentJoinTarget, currentRobTarget);
                    
                    if (logUpdates)
                    {
                        Debug.Log($"[RAPID Target Generator] Targets updated:\nJOINTTARGET: {currentJoinTarget}\nROBTARGET: {currentRobTarget}");
                    }
                }
                else
                {
                    currentJoinTarget = "Invalid joint data";
                    currentRobTarget = "Invalid joint data";
                }
            }
            catch (System.Exception e)
            {
                string errorMsg = $"Calculation failed: {e.Message}";
                currentJoinTarget = errorMsg;
                currentRobTarget = errorMsg;
                
                if (logUpdates)
                {
                    Debug.LogWarning($"[RAPID Target Generator] {errorMsg}");
                }
            }
        }
        
        /// <summary>
        /// Get current joint angles from the robot
        /// </summary>
        private float[] GetCurrentJointAngles()
        {
            if (robot6R == null || robot6R.Joints.Count < 6) return null;
            
            var jointAngles = new float[6];
            for (int i = 0; i < 6; i++)
            {
                jointAngles[i] = robot6R[i]; // Uses MechanicalUnit indexer
            }
            return jointAngles;
        }
        
        /// <summary>
        /// Format joint angles as RAPID JOINTTARGET
        /// Format: [[j1,j2,j3,j4,j5,j6],[external_axis]]
        /// </summary>
        private string FormatJoinTarget(float[] jointAngles)
        {
            if (jointAngles == null || jointAngles.Length < 6) 
                return "JOINTTARGET: Invalid joint data";
            
            // RAPID JOINTTARGET format with external axes set to 9E9 (undefined)
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[[{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2}],[9E9,9E9,9E9,9E9,9E9,9E9]]",
                jointAngles[0], jointAngles[1], jointAngles[2], 
                jointAngles[3], jointAngles[4], jointAngles[5]);
        }
        
        /// <summary>
        /// Format position and orientation as RAPID ROBTARGET
        /// Converts from Unity coordinate system to ABB coordinate system
        /// Format: [[x,y,z],[q1,q2,q3,q4],[cf1,cf4,cf6,cfx],[external_axis]]
        /// </summary>
        private string FormatRobTarget(Vector3 position, Quaternion rotation, Configuration config)
        {
            // Convert from Unity to ABB coordinate system and to millimeters
            // Unity: X=right, Y=up, Z=forward
            // ABB: X=forward, Y=left, Z=up
            float x = position.z * 1000f;  // Unity Z -> ABB X (forward)
            float y = position.x * 1000f;  // Unity X -> ABB Y (left)
            float z = position.y * 1000f;  // Unity Y -> ABB Z (up)
            
            // Convert quaternion components to ABB coordinate system
            float qx = rotation.z;
            float qy = rotation.x;
            float qz = rotation.y; 
            float qw = rotation.w;
            
            // RAPID ROBTARGET format
            // Configuration data [cf1,cf4,cf6,cfx] from Preliy.Flange Configuration
            // External axes [9E9,9E9,9E9,9E9,9E9,9E9] = undefined
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[[{0:F2},{1:F2},{2:F2}],[{3:F6},{4:F6},{5:F6},{6:F6}],[{7},{8},{9},{10}],[9E9,9E9,9E9,9E9,9E9,9E9]]",
                x, y, z, qx, qy, qz, qw, config.Turn1, config.Turn4, config.Turn6, config.Index);
        }
        
        // Public API Methods
        
        /// <summary>
        /// Get current JOINTTARGET string
        /// </summary>
        public string GetJoinTarget()
        {
            return currentJoinTarget;
        }
        
        /// <summary>
        /// Get current ROBTARGET string
        /// </summary>
        public string GetRobTarget()
        {
            return currentRobTarget;
        }
        
        /// <summary>
        /// Get both targets formatted for display/logging
        /// </summary>
        public string GetTargetsInfo()
        {
            return $"JOINTTARGET:\n{currentJoinTarget}\n\nROBTARGET:\n{currentRobTarget}";
        }
        
        /// <summary>
        /// Manually refresh targets (useful for testing)
        /// </summary>
        [ContextMenu("Refresh Targets")]
        public void RefreshTargets()
        {
            UpdateTargets();
            Debug.Log($"[RAPID Target Generator] Manual refresh:\n{GetTargetsInfo()}");
        }
        
        /// <summary>
        /// Copy JOINTTARGET to clipboard
        /// </summary>
        [ContextMenu("Copy JOINTTARGET to Clipboard")]
        public void CopyJoinTargetToClipboard()
        {
            GUIUtility.systemCopyBuffer = currentJoinTarget;
            Debug.Log("JOINTTARGET copied to clipboard");
        }
        
        /// <summary>
        /// Copy ROBTARGET to clipboard
        /// </summary>
        [ContextMenu("Copy ROBTARGET to Clipboard")]
        public void CopyRobTargetToClipboard()
        {
            GUIUtility.systemCopyBuffer = currentRobTarget;
            Debug.Log("ROBTARGET copied to clipboard");
        }
        
        /// <summary>
        /// Copy both targets to clipboard
        /// </summary>
        [ContextMenu("Copy Both Targets to Clipboard")]
        public void CopyBothTargetsToClipboard()
        {
            string bothTargets = $"! Generated by RAPID Target Generator\nCONST jointtarget jtPos := {currentJoinTarget};\nCONST robtarget rtPos := {currentRobTarget};";
            GUIUtility.systemCopyBuffer = bothTargets;
            Debug.Log("Both RAPID targets copied to clipboard in RAPID format");
        }
        
        /// <summary>
        /// Set the robot reference manually
        /// </summary>
        public void SetRobot(Robot6RSphericalWrist robot)
        {
            if (robot6R != null)
            {
                robot6R.OnJointStateChanged -= OnJointStateChanged;
            }
            
            robot6R = robot;
            isInitialized = false;
            InitializeRapidGenerator();
        }
        
        /// <summary>
        /// Enable or disable automatic target updates
        /// </summary>
        public void SetAutoUpdate(bool enabled)
        {
            enableAutoUpdate = enabled;
            
            if (robot6R != null)
            {
                if (enabled)
                {
                    robot6R.OnJointStateChanged += OnJointStateChanged;
                }
                else
                {
                    robot6R.OnJointStateChanged -= OnJointStateChanged;
                }
            }
        }
    }
}