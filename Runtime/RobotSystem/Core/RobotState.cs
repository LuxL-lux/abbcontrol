using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSystem.Core
{
    [Serializable]
    public class RobotState
    {
        [Header("Connection Info")]
        public string robotType = "";
        public string robotIP = "";
        public DateTime lastUpdate = DateTime.Now;
        
        [Header("Execution State")]
        public bool isRunning = false;
        public string motorState = "unknown";
        public string executionCycle = "";
        
        [Header("Program Pointer")]
        public string currentModule = "";
        public string currentRoutine = "";
        public int currentLine = 0;
        public int currentColumn = 0;
        
        [Header("Motion Data")]
        public float[] jointAngles = new float[6];
        public float[] jointVelocities = new float[6];
        public bool hasValidJointData = false;
        public DateTime lastJointUpdate = DateTime.MinValue;
        public double motionUpdateFrequencyHz = 0.0;
        
        [Header("IO Signals")]
        public Dictionary<string, object> ioSignals = new Dictionary<string, object>();
        
        [Header("Controller State")]
        public string controllerState = "";
        
        [Header("Custom Data")]
        [SerializeField] private Dictionary<string, object> customData = new Dictionary<string, object>();
        
        // Generic methods for any robot type
        public void UpdateMotorState(string state)
        {
            motorState = state;
            isRunning = (state == "running" || state == "active" || state == "executing");
            lastUpdate = DateTime.Now;
        }
        
        public void UpdateProgramPointer(string module, string routine, int line, int col)
        {
            currentModule = module;
            currentRoutine = routine;
            currentLine = line;
            currentColumn = col;
            lastUpdate = DateTime.Now;
        }
        
        public void UpdateIOSignal(string signalName, object value, string state = "", string quality = "")
        {
            string key = signalName.ToLower();
            ioSignals[key] = value;
            ioSignals[$"{key}_state"] = state;
            ioSignals[$"{key}_quality"] = quality;
            lastUpdate = DateTime.Now;
        }
        
        public void UpdateControllerState(string state)
        {
            controllerState = state;
            lastUpdate = DateTime.Now;
        }

        public void UpdateExecutionCycle(string state)
        {
            executionCycle = state;
            lastUpdate = DateTime.Now;
        }
        
        public void UpdateJointAngles(float[] angles, double updateFrequency = 0.0)
        {
            if (angles != null && angles.Length >= 6)
            {
                Array.Copy(angles, jointAngles, Math.Min(6, angles.Length));
                hasValidJointData = true;
                lastJointUpdate = DateTime.Now;
                lastUpdate = DateTime.Now;
                motionUpdateFrequencyHz = updateFrequency;
            }
        }
        
        public void UpdateJointVelocities(float[] velocities)
        {
            if (velocities != null && velocities.Length >= 6)
            {
                Array.Copy(velocities, jointVelocities, Math.Min(6, velocities.Length));
                lastUpdate = DateTime.Now;
            }
        }
        
        public float[] GetJointAngles()
        {
            return hasValidJointData ? (float[])jointAngles.Clone() : new float[6];
        }
        
        public float[] GetJointVelocities()
        {
            return (float[])jointVelocities.Clone();
        }

        public void SetCustomData(string key, object value)
        {
            customData[key] = value;
            lastUpdate = DateTime.Now;
        }
        
        public T GetCustomData<T>(string key, T defaultValue = default(T))
        {
            if (customData.ContainsKey(key) && customData[key] is T)
                return (T)customData[key];
            return defaultValue;
        }
        
        public T GetIOSignal<T>(string signalName, T defaultValue = default(T))
        {
            string key = signalName.ToLower();
            if (ioSignals.ContainsKey(key) && ioSignals[key] is T)
                return (T)ioSignals[key];
            return defaultValue;
        }
        
        public string GetIOSignalState(string signalName)
        {
            return GetIOSignal<string>($"{signalName.ToLower()}_state", "");
        }
        
        public string GetIOSignalQuality(string signalName)
        {
            return GetIOSignal<string>($"{signalName.ToLower()}_quality", "");
        }
        
        // Gripper Properties
        public bool GripperOpen => GetIOSignal<bool>("do_gripperopen", false);
        public bool GripperClosed => !GripperOpen;
    }
}