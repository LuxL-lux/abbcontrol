using System;
using System.Collections.Generic;
using System.Linq;
using Preliy.Flange;
using RobotSystem.Core;
using RobotSystem.Interfaces;
using UnityEngine;

namespace RobotSystem.Safety
{
    /// <summary>
    /// DH-parameter based singularity detection using Preliy.Flange framework
    /// Detects wrist, shoulder, and elbow singularities for 6R spherical wrist robots
    /// </summary>
    public class SingularityDetectionMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Singularity Detection Settings")]
        [SerializeField]
        private float wristSingularityThreshold = 5f; // degrees

        [SerializeField]
        private float elbowSingularityThreshold = 5f; // degrees

        [SerializeField]
        private float shoulderSingularityThreshold = 0.1f; // meters

        [SerializeField]
        private bool checkWristSingularity = true;

        [SerializeField]
        private bool checkShoulderSingularity = true;

        [SerializeField]
        private bool checkElbowSingularity = true;

        [Header("Robot Configuration")]
        [SerializeField]
        private bool autoFindFrames = true;

        [Header("Debug Settings")]
        [SerializeField]
        private bool debugLogging = false;

        public string MonitorName => "Singularity Detector";

        private void DebugLog(string message)
        {
            if (debugLogging)
                Debug.Log(message);
        }

        private void DebugLogWarning(string message)
        {
            if (debugLogging)
                Debug.LogWarning(message);
        }

        public bool IsActive { get; private set; } = true;

        public event Action<SafetyEvent> OnSafetyEventDetected;

        private float[] previousJointAngles = new float[6];
        private DateTime lastSingularityTime = DateTime.MinValue;
        private readonly float cooldownTime = 2.0f;

        private Frame[] robotFrames;
        private bool isInitialized = false;

        // Singularity state tracking
        private Dictionary<string, bool> currentSingularityStates = new Dictionary<string, bool>
        {
            { "Wrist", false },
            { "Shoulder", false },
            { "Elbow", false },
        };

        void Awake()
        {
            // Find robot frames for kinematic calculations
            if (autoFindFrames)
            {
                // Priority 1: Try to find Robot component directly
                var robot = FindFirstObjectByType<Robot>();
                if (robot != null)
                {
                    robotFrames = robot.GetComponentsInChildren<Frame>();
                    if (robotFrames != null && robotFrames.Length > 0)
                    {
                        Array.Sort(
                            robotFrames,
                            (a, b) =>
                                GetHierarchyDepth(a.transform)
                                    .CompareTo(GetHierarchyDepth(b.transform))
                        );
                        DebugLog(
                            $"[{MonitorName}] Found {robotFrames.Length} frames from Robot component"
                        );
                    }
                }

                // Priority 2: Check Controller's Robot reference
                if (robotFrames == null || robotFrames.Length == 0)
                {
                    var controller = FindFirstObjectByType<Preliy.Flange.Controller>();
                    if (
                        controller != null
                        && controller.MechanicalGroup != null
                        && controller.MechanicalGroup.Robot != null
                    )
                    {
                        robot = controller.MechanicalGroup.Robot;
                        robotFrames = robot.GetComponentsInChildren<Frame>();
                        if (robotFrames != null && robotFrames.Length > 0)
                        {
                            Array.Sort(
                                robotFrames,
                                (a, b) =>
                                    GetHierarchyDepth(a.transform)
                                        .CompareTo(GetHierarchyDepth(b.transform))
                            );
                            DebugLog(
                                $"[{MonitorName}] Found {robotFrames.Length} frames from Controller's Robot reference"
                            );
                        }
                    }
                }

                // Priority 3: Check Controller's Frames list directly
                if (robotFrames == null || robotFrames.Length == 0)
                {
                    var controller = FindFirstObjectByType<Preliy.Flange.Controller>();
                    if (
                        controller != null
                        && controller.Frames != null
                        && controller.Frames.Count > 0
                    )
                    {
                        // Convert ReferenceFrame list to Frame array
                        var framesList = new List<Frame>();
                        foreach (var refFrame in controller.Frames)
                        {
                            // ReferenceFrame might contain Frame components
                            if (refFrame != null && refFrame.transform != null)
                            {
                                var frame = refFrame.transform.GetComponent<Frame>();
                                if (frame != null)
                                {
                                    framesList.Add(frame);
                                }
                            }
                        }

                        if (framesList.Count > 0)
                        {
                            robotFrames = framesList.ToArray();
                            Array.Sort(
                                robotFrames,
                                (a, b) =>
                                    GetHierarchyDepth(a.transform)
                                        .CompareTo(GetHierarchyDepth(b.transform))
                            );
                            DebugLog(
                                $"[{MonitorName}] Found {robotFrames.Length} frames from Controller's Frames list"
                            );
                        }
                    }
                }

                if (robotFrames == null || robotFrames.Length == 0)
                {
                    DebugLogWarning(
                        $"[{MonitorName}] No robot frames found - singularity detection will be limited"
                    );
                }
            }

            isInitialized = true;
            DebugLog($"[{MonitorName}] Pre-initialized with {(robotFrames?.Length ?? 0)} frames");
        }

        public void Initialize()
        {
            // Initialization handled in Awake
        }

        public void UpdateState(RobotState state)
        {
            if (!IsActive || state == null || !state.hasValidJointData)
                return;

            // Get joint angles from RobotState
            var jointAngles = state.GetJointAngles();
            if (jointAngles != null && jointAngles.Length >= 6)
            {
                CheckForSingularities(jointAngles);
                Array.Copy(jointAngles, previousJointAngles, 6);
            }
        }

        public void SetActive(bool active)
        {
            IsActive = active;
        }

        public void Shutdown()
        {
            IsActive = false;
        }

        private void CheckForSingularities(float[] jointAngles)
        {
            if (robotFrames == null || robotFrames.Length < 6)
                return;

            // Check each singularity type and track state changes
            if (checkWristSingularity)
            {
                bool isInWristSingularity = IsWristSingularityDH(jointAngles);
                CheckSingularityStateChange(
                    "Wrist",
                    "Wrist Singularity (θ₅ ≈ 0°)",
                    isInWristSingularity,
                    jointAngles
                );
            }

            if (checkShoulderSingularity)
            {
                bool isInShoulderSingularity = IsShoulderSingularityDH(jointAngles);
                CheckSingularityStateChange(
                    "Shoulder",
                    "Shoulder Singularity (Wrist on Y₀)",
                    isInShoulderSingularity,
                    jointAngles
                );
            }

            if (checkElbowSingularity)
            {
                bool isInElbowSingularity = IsElbowSingularityDH(jointAngles);
                CheckSingularityStateChange(
                    "Elbow",
                    "Elbow Singularity (J2-J3-J5 Coplanar)",
                    isInElbowSingularity,
                    jointAngles
                );
            }
        }

        private void CheckSingularityStateChange(
            string singularityType,
            string description,
            bool isCurrentlyInSingularity,
            float[] jointAngles
        )
        {
            bool wasInSingularity = currentSingularityStates[singularityType];

            // State change detected
            if (isCurrentlyInSingularity != wasInSingularity)
            {
                currentSingularityStates[singularityType] = isCurrentlyInSingularity;

                if (isCurrentlyInSingularity)
                {
                    // Entering singularity
                    HandleSingularityDetected(description, jointAngles, true);
                }
                else
                {
                    // Exiting singularity - use same description for consistency
                    HandleSingularityDetected(description, jointAngles, false);
                }
            }
            // No state change = no event (prevents spam)
        }

        private bool IsWristSingularityDH(float[] jointAngles)
        {
            // Get Alignment of Wrist Axis and Axis 4
            Vector3 y4 = ComputeJointAxis(jointAngles, 4, 1); // joint 4 green axis
            Vector3 y6 = ComputeJointAxis(jointAngles, 6, 1); // joint 6 green axis

            // If angle between the vectors < threshold then singularity
            float angle = Vector3.Angle(y4, y6);

            return angle < wristSingularityThreshold
                || Mathf.Abs(180f - angle) < wristSingularityThreshold;
        }

        private bool IsShoulderSingularityDH(float[] jointAngles)
        {
            if (robotFrames.Length < 4)
                return false;

            // Calculate wrist center position using forward kinematics
            Vector3 wristCenter = ComputeJointPosition(jointAngles, 5);

            // Shoulder singularity: wrist center lies on Y₀ axis (base rotation axis in Unity)
            // Y-axis is typically the vertical/rotation axis for base joint
            Vector3 basePosition = robotFrames[0].transform.position;
            Vector3 wristToBase = wristCenter - basePosition;

            // Project onto XZ plane (senkrecht to Y₀ in Unity coordinate system)
            // Basically calculates the horizontal distance from the center point to wrist center
            float distanceFromY0 = Mathf.Sqrt(
                wristToBase.x * wristToBase.x + wristToBase.z * wristToBase.z
            );

            return distanceFromY0 < shoulderSingularityThreshold;
        }

        private bool IsElbowSingularityDH(float[] jointAngles)
        {
            if (robotFrames.Length < 4)
                return false;
            // Calculate positions of joint 2, joint 3, and joint 5 (wrist center) using forward kinematics
            Vector3 joint2Position = ComputeJointPosition(jointAngles, 2);
            Vector3 joint3Position = ComputeJointPosition(jointAngles, 3);
            Vector3 joint5Position = ComputeJointPosition(jointAngles, 5);

            // Vectors from joint 2
            Vector3 v23 = joint3Position - joint2Position;
            Vector3 v25 = joint5Position - joint2Position;

            // Angle between vectors in degrees
            float angle = Vector3.Angle(v23, v25);

            // Check for near-straight or near-folded arm
            return (angle < elbowSingularityThreshold || angle > 180f - elbowSingularityThreshold);
        }

        private Vector3 ComputeJointPosition(float[] jointAngles, int jointIndex)
        {
            // Use forward kinematics to compute position after applying jointIndex transformations

            Matrix4x4 baseTransform = Matrix4x4.identity;

            // Apply joint transformations to get to desired joint position
            // To get joint N position, apply transformations 0 to N-1
            for (int i = 0; i < jointIndex - 1 && i < robotFrames.Length - 1; i++)
            {
                var frame = robotFrames[i + 1]; // Frame i+1 corresponds to joint i
                var config = frame.Config;

                // Create transformation matrix using DH parameters
                float theta = jointAngles[i] * Mathf.Deg2Rad + config.Theta;
                Matrix4x4 dhTransform = HomogeneousMatrix.CreateRaw(
                    new FrameConfig(config.Alpha, config.A, config.D, theta)
                );

                baseTransform = baseTransform * dhTransform;
            }

            return baseTransform.GetPosition();
        }

        private Vector3 ComputeJointAxis(float[] jointAngles, int jointIndex, int axisIndex)
        {
            Matrix4x4 baseTransform = Matrix4x4.identity;

            for (int i = 0; i < jointIndex - 1 && i < robotFrames.Length - 1; i++)
            {
                var frame = robotFrames[i + 1];
                var config = frame.Config;

                float theta = jointAngles[i] * Mathf.Deg2Rad + config.Theta;
                Matrix4x4 dhTransform = HomogeneousMatrix.CreateRaw(
                    new FrameConfig(config.Alpha, config.A, config.D, theta)
                );

                baseTransform *= dhTransform;
            }

            return baseTransform.GetColumn(axisIndex).normalized;
        }

        private int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            Transform current = transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private void HandleSingularityDetected(
            string singularityType,
            float[] jointAngles,
            bool entering = true
        )
        {
            lastSingularityTime = DateTime.Now;

            var singularityData = new SingularityInfo()
            {
                singularityType = singularityType,
                jointAngles = (float[])jointAngles.Clone(),
                wristThreshold = wristSingularityThreshold,
                shoulderThreshold = shoulderSingularityThreshold,
                elbowThreshold = elbowSingularityThreshold,
                manipulability = GetManipulability(jointAngles),
                isEntering = entering,
            };

            string eventDescription = entering
                ? $"Entering {singularityType} at joint configuration: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]°"
                : $"Exiting {singularityType} at joint configuration: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]°";

            // Create safety event - safety manager will provide robot state
            var safetyEvent = new SafetyEvent(
                MonitorName,
                entering ? SafetyEventType.Warning : SafetyEventType.Resolved,
                eventDescription,
                null // Safety manager will provide robot state
            );

            // Add singularity-specific data
            safetyEvent.SetEventData(singularityData);

            // Trigger event
            OnSafetyEventDetected?.Invoke(safetyEvent);
        }

        public double GetManipulability(float[] jointAngles)
        {
            // Build the Jacobian for all joints (except base, assuming robotFrames includes all frames)
            int jointCount = robotFrames.Length - 1;
            double[,] J = BuildJacobian(jointAngles, jointCount);

            // Compute the Yoshikawa manipulability measure
            double manipulability = ComputeManipulability(J);

            return manipulability;
        }

        private double[,] BuildJacobian(float[] jointAngles, int jointCount)
        {
            double[,] J = new double[6, jointCount];

            Vector3 pE = ComputeJointPosition(jointAngles, 5); // wrist center pont

            for (int i = 0; i < jointCount; i++)
            {
                Vector3 pi = ComputeJointPosition(jointAngles, i + 1);
                Vector3 zi = ComputeJointAxis(jointAngles, i + 1, 1); // use Y-axis

                Vector3 Jv = Vector3.Cross(zi, pE - pi); // linear part
                Vector3 Jw = zi; // angular part

                J[0, i] = Jv.x;
                J[1, i] = Jv.y;
                J[2, i] = Jv.z;
                J[3, i] = Jw.x;
                J[4, i] = Jw.y;
                J[5, i] = Jw.z;
            }

            return J;
        }

        // Simple manipulability measure: w = sqrt(det(J * J^T))
        private double ComputeManipulability(double[,] J)
        {
            int m = J.GetLength(0);
            int n = J.GetLength(1);

            // Compute J * J^T (6x6)
            double[,] JJt = new double[m, m];
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < m; c++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < n; k++)
                        sum += J[r, k] * J[c, k];
                    JJt[r, c] = sum;
                }
            }

            // Determinant via naive LU decomposition (works for 6x6)
            double det = Det6x6(JJt);
            if (det < 0)
                det = 0; // numerical safety
            return Mathf.Sqrt((float)det);
        }

        // Example LU-based determinant for 6x6 matrix
        private static double Det6x6(double[,] A)
        {
            int N = 6;
            double[,] M = new double[N, N];
            System.Array.Copy(A, M, A.Length);
            double det = 1.0;

            for (int k = 0; k < N; k++)
            {
                // pivot
                int piv = k;
                double maxAbs = Mathf.Abs((float)M[k, k]);
                for (int r = k + 1; r < N; r++)
                {
                    double v = Mathf.Abs((float)M[r, k]);
                    if (v > maxAbs)
                    {
                        maxAbs = v;
                        piv = r;
                    }
                }
                if (maxAbs < 1e-12)
                    return 0.0;

                if (piv != k)
                {
                    for (int c = k; c < N; c++)
                    {
                        double tmp = M[k, c];
                        M[k, c] = M[piv, c];
                        M[piv, c] = tmp;
                    }
                    det = -det;
                }

                double pivot = M[k, k];
                det *= pivot;

                for (int r = k + 1; r < N; r++)
                {
                    double f = M[r, k] / pivot;
                    for (int c = k + 1; c < N; c++)
                        M[r, c] -= f * M[k, c];
                    M[r, k] = 0.0;
                }
            }

            return det;
        }
    }

    [Serializable]
    public class SingularityInfo
    {
        public string singularityType;
        public float[] jointAngles;
        public float wristThreshold;
        public float shoulderThreshold;
        public float elbowThreshold;
        public string detectionTime;
        public double manipulability;
        public bool isEntering; // true = entering singularity, false = exiting

        public SingularityInfo()
        {
            detectionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            // Don't set default for isEntering - it will be set explicitly
        }
    }
}

