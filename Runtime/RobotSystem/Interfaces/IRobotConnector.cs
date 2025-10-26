using System;

namespace RobotSystem.Interfaces
{
    public interface IRobotConnector
    {
        event System.Action<RobotSystem.Core.RobotState> OnRobotStateUpdated;
        event System.Action<bool> OnConnectionStateChanged;

        bool IsConnected { get; }
        RobotSystem.Core.RobotState CurrentState { get; }

        void Connect();
        void Disconnect();
    }
}

