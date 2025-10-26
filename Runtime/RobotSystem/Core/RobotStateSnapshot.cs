using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSystem.Core
{
    /// <summary>
    /// Immutable snapshot of robot state for safety event logging
    /// </summary>
    [Serializable]
    public class RobotStateSnapshot
    {
        [Header("Timestamp")]
        public DateTime captureTime;
        
        [Header("Program Info")]
        public bool isProgramRunning;
        public string currentModule;
        public string currentRoutine;
        public int currentLine;
        public int currentColumn;
        public string executionCycle;
        
        [Header("Robot State")]
        public string motorState;
        public string controllerState;
        public float[] jointAngles = new float[6];
        public bool hasValidJointData;
        public double motionUpdateFrequencyHz;
        
        [Header("IO Signals")]
        public bool gripperOpen;
        
        [Header("Connection Info")]
        public string robotType;
        public string robotIP;
        
        public RobotStateSnapshot(RobotState state)
        {
            if (state != null)
            {
                captureTime = DateTime.Now;
                
                // Program info
                isProgramRunning = state.isRunning;
                currentModule = state.currentModule ?? "";
                currentRoutine = state.currentRoutine ?? "";
                currentLine = state.currentLine;
                currentColumn = state.currentColumn;
                executionCycle = state.executionCycle ?? "";
                
                // Robot state
                motorState = state.motorState ?? "";
                controllerState = state.controllerState ?? "";
                jointAngles = state.GetJointAngles();
                hasValidJointData = state.hasValidJointData;
                motionUpdateFrequencyHz = state.motionUpdateFrequencyHz;
                
                // IO signals
                gripperOpen = state.GripperOpen;
                
                // Connection info
                robotType = state.robotType ?? "";
                robotIP = state.robotIP ?? "";
            }
        }
        
        /// <summary>
        /// Get program context as string
        /// </summary>
        public string GetProgramContext()
        {
            if (isProgramRunning && !string.IsNullOrEmpty(currentModule))
            {
                return $"{currentModule}.{currentRoutine}:{currentLine}:{currentColumn}";
            }
            return "No program running";
        }
    }
}