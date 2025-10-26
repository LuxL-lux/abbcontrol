using System;

namespace RobotSystem.Interfaces
{
    /// <summary>
    /// Generic interface for robot visualization systems (Flange, custom visualizers, etc.)
    /// </summary>
    public interface IRobotVisualization
    {
        event Action<float[]> OnJointAnglesRequested;
        
        bool IsConnected { get; }
        bool IsValid { get; }
        string VisualizationType { get; }
        
        void UpdateJointAngles(float[] jointAngles);
        bool TryUpdateJointAngles(float[] jointAngles);
        void Initialize();
        void Shutdown();
    }
}