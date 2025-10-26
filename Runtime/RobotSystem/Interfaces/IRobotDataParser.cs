namespace RobotSystem.Interfaces
{
    public interface IRobotDataParser
    {
        void ParseData(string rawData, RobotSystem.Core.RobotState robotState);
        bool CanParse(string rawData);
    }
}