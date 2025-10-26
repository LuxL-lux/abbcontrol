using System;
using RobotSystem.Core;

namespace RobotSystem.Interfaces
{
    /// <summary>
    /// Interface for robot safety monitoring modules (collision, singularity, etc.)
    /// </summary>
    public interface IRobotSafetyMonitor
    {
        /// <summary>
        /// Name of the safety monitor (e.g., "Collision Detector", "Singularity Monitor")
        /// </summary>
        string MonitorName { get; }
        
        /// <summary>
        /// Whether this monitor is currently active
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Event triggered when a safety issue is detected
        /// </summary>
        event Action<SafetyEvent> OnSafetyEventDetected;
        
        /// <summary>
        /// Initialize the monitor
        /// </summary>
        void Initialize();
        
        /// <summary>
        /// Update the monitor with current robot state
        /// </summary>
        /// <param name="state">Current robot state</param>
        void UpdateState(RobotState state);
        
        /// <summary>
        /// Enable/disable the monitor
        /// </summary>
        void SetActive(bool active);
        
        /// <summary>
        /// Cleanup resources
        /// </summary>
        void Shutdown();
    }
}