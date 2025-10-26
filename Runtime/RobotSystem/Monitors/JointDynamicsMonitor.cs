using System;
using System.Collections.Generic;
using Preliy.Flange;
using RobotSystem.Core;
using RobotSystem.Interfaces;
using UnityEngine;

namespace RobotSystem.Safety
{
    ///
    public class JointDynamicsMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Monitor Configuration")]
        [SerializeField]
        private bool isActive = true;

        [SerializeField]
        private string monitorName = "Joint Dynamics Monitor";

        [Header("Joint Limits from Flange")]
        [SerializeField]
        private bool useFlangeLimits = true;

        [SerializeField]
        private float limitSafetyFactor = 0.8f; // Use 80% of max limits for safety

        [Header("Manual Limits - ABB IRB 6700-200/2.60 Specifications")]
        [SerializeField]
        private float[] maxJointAngles = { 170f, 85f, 70f, 300f, 130f, 360f };

        [SerializeField]
        private float[] minJointAngles = { -170f, -65f, -180f, -300f, -130f, -360f };

        [SerializeField]
        private float[] maxJointVelocities = { 110f, 110f, 110f, 190f, 150f, 210f };

        [SerializeField]
        private float[] maxJointAccelerations = { 800f, 800f, 800f, 1500f, 1200f, 1800f };

        [SerializeField]
        private float checkEveryNthUpdate = 2; // Every second update

        [SerializeField]
        private int historyBufferSize = 15;

        [Header("Data Smoothing")]
        [SerializeField]
        private bool enableSmoothing = true;

        [Header("Debug Settings")]
        [SerializeField]
        private bool debugLogging = false;

        [SerializeField]
        [Range(0.1f, 1.0f)]
        private float smoothingAlpha = 0.2f; // Exponential moving average factor (0-1, lower = more smoothing)

        [SerializeField]
        [Range(3, 20)]
        private int smoothingWindowSize = 8; // Number of samples for moving average

        [SerializeField]
        [Range(0.05f, 0.5f)]
        private float velocityOutlierThreshold = 0.2f; // Outlier rejection threshold for velocities

        [SerializeField]
        [Range(0.05f, 0.5f)]
        private float accelerationOutlierThreshold = 0.15f; // Outlier rejection threshold for accelerations

        // Interface implementation
        public bool IsActive => isActive;
        public string MonitorName => monitorName;
        public event Action<SafetyEvent> OnSafetyEventDetected;

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

        private void DebugLogError(string message)
        {
            if (debugLogging)
                Debug.LogError(message);
        }

        // Monitoring state
        private Preliy.Flange.Common.Gripper gripperComponent;
        private List<JointState> jointHistory = new List<JointState>();
        private float[] currentJointVelocities = new float[6];
        private float[] currentJointAccelerations = new float[6];
        private float[] previousJointAngles = new float[6];
        private DateTime lastUpdateTime;
        private int updateCounter = 0; // Counter for nth update monitoring

        // Smoothing state
        private float[] smoothedVelocities = new float[6];
        private float[] smoothedAccelerations = new float[6];
        private List<float[]> velocityBuffer = new List<float[]>();
        private List<float[]> accelerationBuffer = new List<float[]>();

        // Safety state tracking
        private bool[] jointAngleViolations = new bool[6];
        private bool[] jointVelocityViolations = new bool[6];
        private bool[] jointAccelerationViolations = new bool[6];
        private DateTime lastViolationTime = DateTime.MinValue;

        private void Awake()
        {
            gripperComponent = FindFirstObjectByType<Preliy.Flange.Common.Gripper>();
            // Initialize limits from Flange if enabled
            if (useFlangeLimits)
            {
                InitializeLimitsFromFlange();
            }

            lastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// Initialize joint limits from Flange JointConfig
        /// </summary>
        private void InitializeLimitsFromFlange()
        {
            var robot6R = FindFirstObjectByType<Robot6RSphericalWrist>();
            if (robot6R == null || robot6R.Joints.Count < 6)
            {
                DebugLogWarning(
                    $"[{MonitorName}] Cannot get limits from Flange - robot not properly configured"
                );
                useFlangeLimits = false;
                return;
            }

            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var joint = robot6R.Joints[i];

                    // Check if joint is valid
                    if (joint == null)
                    {
                        DebugLogWarning($"[{MonitorName}] Joint {i} is null, using manual limits");
                        useFlangeLimits = false;
                        return;
                    }

                    var config = joint.Config;

                    // Get angle limits
                    minJointAngles[i] = config.Limits.x;
                    maxJointAngles[i] = config.Limits.y;

                    // Get velocity and acceleration limits with safety factor
                    maxJointVelocities[i] = config.SpeedMax * limitSafetyFactor;
                    maxJointAccelerations[i] = config.AccMax * limitSafetyFactor;
                }
            }
            catch (System.Exception e)
            {
                DebugLogError(
                    $"[{MonitorName}] Failed to get limits from Flange: {e.Message}\nStack: {e.StackTrace}"
                );
                useFlangeLimits = false;
            }
        }

        private void CalculateVelocities()
        {
            if (jointHistory.Count < 2)
                return;

            var current = jointHistory[jointHistory.Count - 1];
            var previous = jointHistory[jointHistory.Count - 2];

            // Calculate raw velocities
            float[] rawVelocities = new float[6];
            for (int i = 0; i < 6; i++)
            {
                float angleDelta = current.angles[i] - previous.angles[i];
                rawVelocities[i] = angleDelta / current.deltaTime;
            }

            // Apply smoothing if enabled
            if (enableSmoothing)
            {
                currentJointVelocities = SmoothVelocities(rawVelocities);
            }
            else
            {
                currentJointVelocities = rawVelocities;
            }
        }

        private void CalculateAccelerations()
        {
            if (jointHistory.Count < 3)
                return;

            var current = jointHistory[jointHistory.Count - 1];
            var previous = jointHistory[jointHistory.Count - 2];
            var beforePrevious = jointHistory[jointHistory.Count - 3];

            // Calculate raw accelerations
            float[] rawAccelerations = new float[6];
            for (int i = 0; i < 6; i++)
            {
                float currentVelocity =
                    (current.angles[i] - previous.angles[i]) / current.deltaTime;
                float previousVelocity =
                    (previous.angles[i] - beforePrevious.angles[i]) / previous.deltaTime;

                rawAccelerations[i] = (currentVelocity - previousVelocity) / current.deltaTime;
            }

            // Apply smoothing if enabled
            if (enableSmoothing)
            {
                currentJointAccelerations = SmoothAccelerations(rawAccelerations);
            }
            else
            {
                currentJointAccelerations = rawAccelerations;
            }
        }

        private void CheckJointLimits(float[] jointAngles)
        {
            for (int i = 0; i < 6; i++)
            {
                CheckJointAngleLimits(i, jointAngles[i]);
                CheckJointVelocityLimits(i, currentJointVelocities[i]);
                CheckJointAccelerationLimits(i, currentJointAccelerations[i]);
            }
        }

        /// <summary>
        /// Apply exponential moving average and moving window smoothing to velocity data
        /// </summary>
        private float[] SmoothVelocities(float[] rawVelocities)
        {
            // Add current velocities to buffer
            velocityBuffer.Add((float[])rawVelocities.Clone());

            // Maintain buffer size
            if (velocityBuffer.Count > smoothingWindowSize)
            {
                velocityBuffer.RemoveAt(0);
            }

            float[] result = new float[6];

            for (int i = 0; i < 6; i++)
            {
                // Apply exponential moving average first
                smoothedVelocities[i] =
                    (smoothingAlpha * rawVelocities[i])
                    + ((1f - smoothingAlpha) * smoothedVelocities[i]);

                // Then apply moving window average if we have enough data
                if (velocityBuffer.Count >= smoothingWindowSize)
                {
                    float windowSum = 0f;
                    for (int j = 0; j < velocityBuffer.Count; j++)
                    {
                        windowSum += velocityBuffer[j][i];
                    }
                    result[i] = windowSum / velocityBuffer.Count;
                }
                else
                {
                    // Use exponential average if not enough samples for window
                    result[i] = smoothedVelocities[i];
                }

                // Apply outlier rejection - clamp extreme values
                float maxChange = maxJointVelocities[i] * velocityOutlierThreshold;
                if (velocityBuffer.Count > 1)
                {
                    float previousSmoothed =
                        velocityBuffer.Count >= 2
                            ? GetMovingAverage(velocityBuffer, i, velocityBuffer.Count - 1)
                            : smoothedVelocities[i];
                    float change = Mathf.Abs(result[i] - previousSmoothed);

                    if (change > maxChange)
                    {
                        // Limit the change to prevent spikes
                        result[i] =
                            previousSmoothed + Mathf.Sign(result[i] - previousSmoothed) * maxChange;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Apply exponential moving average and moving window smoothing to acceleration data
        /// </summary>
        private float[] SmoothAccelerations(float[] rawAccelerations)
        {
            // Add current accelerations to buffer
            accelerationBuffer.Add((float[])rawAccelerations.Clone());

            // Maintain buffer size
            if (accelerationBuffer.Count > smoothingWindowSize)
            {
                accelerationBuffer.RemoveAt(0);
            }

            float[] result = new float[6];

            for (int i = 0; i < 6; i++)
            {
                // Apply exponential moving average first
                smoothedAccelerations[i] =
                    (smoothingAlpha * rawAccelerations[i])
                    + ((1f - smoothingAlpha) * smoothedAccelerations[i]);

                // Then apply moving window average if we have enough data
                if (accelerationBuffer.Count >= smoothingWindowSize)
                {
                    float windowSum = 0f;
                    for (int j = 0; j < accelerationBuffer.Count; j++)
                    {
                        windowSum += accelerationBuffer[j][i];
                    }
                    result[i] = windowSum / accelerationBuffer.Count;
                }
                else
                {
                    // Use exponential average if not enough samples for window
                    result[i] = smoothedAccelerations[i];
                }

                // Apply outlier rejection - clamp extreme values
                float maxChange = maxJointAccelerations[i] * accelerationOutlierThreshold;
                if (accelerationBuffer.Count > 1)
                {
                    float previousSmoothed =
                        accelerationBuffer.Count >= 2
                            ? GetMovingAverage(accelerationBuffer, i, accelerationBuffer.Count - 1)
                            : smoothedAccelerations[i];
                    float change = Mathf.Abs(result[i] - previousSmoothed);

                    if (change > maxChange)
                    {
                        // Limit the change to prevent spikes
                        result[i] =
                            previousSmoothed + Mathf.Sign(result[i] - previousSmoothed) * maxChange;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Helper method to get moving average from buffer at specific index
        /// </summary>
        private float GetMovingAverage(List<float[]> buffer, int jointIndex, int upToIndex)
        {
            if (buffer.Count == 0 || upToIndex < 0)
                return 0f;

            float sum = 0f;
            int count = Mathf.Min(upToIndex + 1, buffer.Count);

            for (int i = 0; i < count; i++)
            {
                sum += buffer[i][jointIndex];
            }

            return sum / count;
        }

        private void CheckJointAngleLimits(int jointIndex, float angle)
        {
            bool isViolation =
                angle < minJointAngles[jointIndex] || angle > maxJointAngles[jointIndex];

            if (isViolation && !jointAngleViolations[jointIndex])
            {
                // Entering violation
                jointAngleViolations[jointIndex] = true;
                GenerateSafetyEvent(
                    "JointAngleLimit",
                    jointIndex,
                    angle,
                    minJointAngles[jointIndex],
                    maxJointAngles[jointIndex]
                );
            }
            else if (!isViolation && jointAngleViolations[jointIndex])
            {
                // Exiting violation
                jointAngleViolations[jointIndex] = false;
                GenerateSafetyEvent(
                    "JointAngleLimitResolved",
                    jointIndex,
                    angle,
                    minJointAngles[jointIndex],
                    maxJointAngles[jointIndex]
                );
            }
        }

        private void CheckJointVelocityLimits(int jointIndex, float velocity)
        {
            bool isViolation = Mathf.Abs(velocity) > maxJointVelocities[jointIndex];

            if (isViolation && !jointVelocityViolations[jointIndex])
            {
                jointVelocityViolations[jointIndex] = true;
                GenerateSafetyEvent(
                    "JointVelocityLimit",
                    jointIndex,
                    velocity,
                    -maxJointVelocities[jointIndex],
                    maxJointVelocities[jointIndex]
                );
            }
            else if (!isViolation && jointVelocityViolations[jointIndex])
            {
                jointVelocityViolations[jointIndex] = false;
                GenerateSafetyEvent(
                    "JointVelocityLimitResolved",
                    jointIndex,
                    velocity,
                    -maxJointVelocities[jointIndex],
                    maxJointVelocities[jointIndex]
                );
            }
        }

        private void CheckJointAccelerationLimits(int jointIndex, float acceleration)
        {
            bool isViolation = Mathf.Abs(acceleration) > maxJointAccelerations[jointIndex];

            if (isViolation && !jointAccelerationViolations[jointIndex])
            {
                jointAccelerationViolations[jointIndex] = true;
                GenerateSafetyEvent(
                    "JointAccelerationLimit",
                    jointIndex,
                    acceleration,
                    -maxJointAccelerations[jointIndex],
                    maxJointAccelerations[jointIndex]
                );
            }
            else if (!isViolation && jointAccelerationViolations[jointIndex])
            {
                jointAccelerationViolations[jointIndex] = false;
                GenerateSafetyEvent(
                    "JointAccelerationLimitResolved",
                    jointIndex,
                    acceleration,
                    -maxJointAccelerations[jointIndex],
                    maxJointAccelerations[jointIndex]
                );
            }
        }

        private void GenerateSafetyEvent(
            string eventType,
            int jointIndex,
            float currentValue,
            float minLimit,
            float maxLimit
        )
        {
            lastViolationTime = DateTime.Now;

            var eventData = new JointDynamicsInfo()
            {
                eventType = eventType,
                jointIndex = jointIndex,
                currentValue = currentValue,
                minLimit = minLimit,
                maxLimit = maxLimit,
                jointAngles = (float[])previousJointAngles.Clone(), // Use the stored joint angles
                jointVelocities = (float[])currentJointVelocities.Clone(),
                jointAccelerations = (float[])currentJointAccelerations.Clone(),
                partId = "",
                smoothingEnabled = enableSmoothing,
                smoothingAlpha = smoothingAlpha,
                smoothingWindowSize = smoothingWindowSize,
            };

            var safetyEvent = new SafetyEvent(
                MonitorName,
                GetSafetyEventType(eventType),
                GetEventDescription(eventType, jointIndex, currentValue),
                null
            );

            // Add joint dynamics specific data
            safetyEvent.SetEventData(eventData);

            OnSafetyEventDetected?.Invoke(safetyEvent);
        }

        private SafetyEventType GetSafetyEventType(string eventType)
        {
            // All "Resolved" events should use the Resolved type
            if (eventType.Contains("Resolved"))
                return SafetyEventType.Resolved;
            
            if (eventType.Contains("Acceleration"))
                return SafetyEventType.Critical;
            if (eventType.Contains("Velocity"))
                return SafetyEventType.Warning;
            return SafetyEventType.Info;
        }

        private string GetEventDescription(string eventType, int jointIndex, float value)
        {
            string jointName = $"Joint {jointIndex + 1}";

            return eventType switch
            {
                "JointAngleLimit" => $"{jointName} angle limit exceeded: {value:F2}°",
                "JointAngleLimitResolved" =>
                    $"{jointName} angle limit resolved: {value:F2}°",
                "JointVelocityLimit" =>
                    $"{jointName} velocity limit exceeded: {value:F2}°/s",
                "JointVelocityLimitResolved" =>
                    $"{jointName} velocity limit resolved: {value:F2}°/s",
                "JointAccelerationLimit" =>
                    $"{jointName} acceleration limit exceeded: {value:F2}°/s²",
                "JointAccelerationLimitResolved" =>
                    $"{jointName} acceleration limit resolved: {value:F2}°/s²",
                _ => $"{jointName} dynamics event: {eventType}",
            };
        }

        private void ResetViolationStates()
        {
            for (int i = 0; i < 6; i++)
            {
                jointAngleViolations[i] = false;
                jointVelocityViolations[i] = false;
                jointAccelerationViolations[i] = false;

                // Reset smoothing data
                smoothedVelocities[i] = 0f;
                smoothedAccelerations[i] = 0f;
            }

            jointHistory.Clear();
            velocityBuffer.Clear();
            accelerationBuffer.Clear();
            updateCounter = 0; // Reset update counter
        }

        // Public API
        public float[] CurrentJointVelocities => (float[])currentJointVelocities.Clone();
        public float[] CurrentJointAccelerations => (float[])currentJointAccelerations.Clone();

        public void SetActive(bool active)
        {
            isActive = active;
            if (!active)
            {
                ResetViolationStates();
            }
        }

        // Interface implementation method
        public void Initialize()
        {
            // Notihing
        }

        public void UpdateState(RobotState state)
        {
            if (!IsActive || state == null || !state.hasValidJointData)
                return;

            // Increment update counter
            updateCounter++;

            // Check if this is the nth update using modulo
            if (updateCounter % checkEveryNthUpdate != 0)
            {
                return; // Skip this update
            }

            try
            {
                // Get joint angles from RobotState
                float[] currentJointAngles = state.GetJointAngles();
                if (currentJointAngles == null || currentJointAngles.Length < 6)
                    return;

                // Add current state to history
                var currentTime = DateTime.Now;
                var deltaTime = (float)(currentTime - lastUpdateTime).TotalSeconds;
                
                var jointState = new JointState()
                {
                    angles = (float[])currentJointAngles.Clone(),
                    deltaTime = deltaTime
                };

                jointHistory.Add(jointState);

                // Maintain history buffer size
                if (jointHistory.Count > historyBufferSize)
                {
                    jointHistory.RemoveAt(0);
                }

                // Calculate velocities and accelerations
                CalculateVelocities();
                CalculateAccelerations();
                
                lastUpdateTime = currentTime;

                // Check for violations
                CheckJointLimits(currentJointAngles);

                // Store current angles for next iteration
                Array.Copy(currentJointAngles, previousJointAngles, 6);
            }
            catch (System.Exception e)
            {
                DebugLogError($"[{MonitorName}] Error in UpdateState: {e.Message}");
            }
        }

        public void Shutdown()
        {
            isActive = false;
            // No longer using InvokeRepeating
            ResetViolationStates();
        }

        // Manual testing methods
        [ContextMenu("Check Joint Dynamics Now")]
        public void CheckJointDynamicsNow()
        {
            DebugLogWarning(
                $"[{MonitorName}] Manual checking requires RobotState - use UpdateState instead"
            );
        }

        [ContextMenu("Reset Smoothing Data")]
        public void ResetSmoothingData()
        {
            for (int i = 0; i < 6; i++)
            {
                smoothedVelocities[i] = 0f;
                smoothedAccelerations[i] = 0f;
            }
            velocityBuffer.Clear();
            accelerationBuffer.Clear();
        }

        [ContextMenu("Toggle Smoothing")]
        public void ToggleSmoothing()
        {
            enableSmoothing = !enableSmoothing;
            if (!enableSmoothing)
            {
                ResetSmoothingData();
            }
        }

    }

    [Serializable]
    public class JointDynamicsInfo
    {
        public string eventType;
        public int jointIndex;
        public float currentValue;
        public float minLimit;
        public float maxLimit;
        public float[] jointAngles;
        public float[] jointVelocities;
        public float[] jointAccelerations;
        public string attachedPart;
        public string partId;
        public bool smoothingEnabled;
        public float smoothingAlpha;
        public int smoothingWindowSize;
        public string detectionTime;
        
        public JointDynamicsInfo()
        {
            detectionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }

    [Serializable]
    public class JointState
    {
        public float[] angles;
        public string timestamp;
        public float deltaTime;
        
        public JointState()
        {
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
}
