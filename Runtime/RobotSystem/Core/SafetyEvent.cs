using System;
using UnityEngine;

namespace RobotSystem.Core
{
    /// <summary>
    /// Represents a safety event detected by a safety monitor
    /// </summary>
    [Serializable]
    public class SafetyEvent
    {
        [Header("Event Info")]
        public string monitorName = "";
        public SafetyEventType eventType = SafetyEventType.Warning;
        public DateTime timestamp = DateTime.Now;
        public string description = "";
        
        [Header("Robot State Snapshot")]
        public RobotStateSnapshot robotStateSnapshot;
        
        [Header("Event Specific Data")]
        public string eventDataJson = "";
        
        public SafetyEvent(string monitorName, SafetyEventType eventType, string description, RobotState currentState)
        {
            this.monitorName = monitorName;
            this.eventType = eventType;
            this.description = description;
            this.timestamp = DateTime.Now;
            this.robotStateSnapshot = currentState != null ? new RobotStateSnapshot(currentState) : null;
        }
        
        /// <summary>
        /// Set event-specific data (e.g., collision points, singularity details)
        /// Serializes the data to JSON string for proper storage
        /// </summary>
        public void SetEventData<T>(T data)
        {
            if (data != null)
            {
                try
                {
                    eventDataJson = JsonUtility.ToJson(data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SafetyEvent] Failed to serialize event data: {e.Message}");
                    eventDataJson = "";
                }
            }
            else
            {
                eventDataJson = "";
            }
        }
        
        /// <summary>
        /// Get event-specific data
        /// Deserializes from JSON string
        /// </summary>
        public T GetEventData<T>() where T : new()
        {
            if (string.IsNullOrEmpty(eventDataJson))
            {
                return new T();
            }
            
            try
            {
                return JsonUtility.FromJson<T>(eventDataJson);
            }
            catch
            {
                return new T();
            }
        }
        
        /// <summary>
        /// Convert to JSON format for logging
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }
    
    public enum SafetyEventType
    {
        Info,
        Resolved,   // For events that indicate resolution of a previous warning/critical state
        Warning,
        Critical,
        Emergency
    }
}