using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RobotSystem.Interfaces;

namespace RobotSystem.Core
{
    public class RobotSafetyManager : MonoBehaviour
    {
        [Header("Safety Monitors")]
        [SerializeField] private List<MonoBehaviour> safetyMonitorComponents = new List<MonoBehaviour>();
        
        [Header("Logging Settings")]
        [SerializeField] private bool enableJsonLogging = true;
        [SerializeField] private bool logOnlyWhenProgramRunning = true;
        [SerializeField] private string logDirectory = "Logs";
        [SerializeField] private SafetyEventType minimumLogLevel = SafetyEventType.Warning;
        
        private List<IRobotSafetyMonitor> safetyMonitors = new List<IRobotSafetyMonitor>();
        private RobotManager robotManager;
        private Queue<RobotState> pendingStateUpdates = new Queue<RobotState>();
        
        // Safety event collection for program-based logging
        private List<SafetyEvent> currentProgramSafetyEvents = new List<SafetyEvent>();
        private string currentProgramName = "";
        private DateTime programStartTime;
        private bool isProgramCurrentlyRunning = false;
        
        public event Action<SafetyEvent> OnSafetyEventDetected;
        
        void Start()
        {
            InitializeSafetyMonitors();
            
            // Find robot manager for state access and program tracking
            robotManager = FindFirstObjectByType<RobotManager>();
            if (robotManager != null)
            {
                // Subscribe to motor state changes for program tracking
                robotManager.OnMotorStateChanged += OnMotorStateChanged;
                
                // Subscribe to state updates to forward to safety monitors
                robotManager.OnStateUpdated += OnRobotStateUpdated;
            }
            else
            {
                Debug.LogWarning("[Safety Manager] RobotManager not found. Robot state will not be available for safety events.");
            }
            
            // Ensure log directory exists in project folder
            if (enableJsonLogging)
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                string fullLogPath = Path.Combine(projectPath, logDirectory);
                if (!Directory.Exists(fullLogPath))
                {
                    Directory.CreateDirectory(fullLogPath);
                    Debug.Log($"[Safety Manager] Created log directory: {fullLogPath}");
                }
            }
        }
        
        
        private void InitializeSafetyMonitors()
        {
            safetyMonitors.Clear();
            
            // Convert MonoBehaviour components to IRobotSafetyMonitor interfaces
            foreach (var component in safetyMonitorComponents)
            {
                if (component != null && component is IRobotSafetyMonitor monitor)
                {
                    try
                    {
                        monitor.Initialize();
                        monitor.OnSafetyEventDetected += OnSafetyEventOccurred;
                        safetyMonitors.Add(monitor);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Safety Manager] Failed to initialize safety monitor {monitor.MonitorName}: {e.Message}");
                    }
                }
                else if (component != null)
                {
                    Debug.LogWarning($"[Safety Manager] Component {component.name} does not implement IRobotSafetyMonitor interface");
                }
            }
        }
        
        private void OnRobotStateUpdated(RobotState state)
        {
            // Queue state updates to be processed on main thread
            lock (pendingStateUpdates)
            {
                pendingStateUpdates.Enqueue(state);
            }
        }
        
        void Update()
        {
            // Process pending state updates on main thread
            while (pendingStateUpdates.Count > 0)
            {
                RobotState state = null;
                lock (pendingStateUpdates)
                {
                    if (pendingStateUpdates.Count > 0)
                        state = pendingStateUpdates.Dequeue();
                }
                
                if (state != null)
                {
                    // Forward state updates to all active safety monitors (now on main thread)
                    foreach (var monitor in safetyMonitors)
                    {
                        if (monitor != null && monitor.IsActive)
                        {
                            try
                            {
                                monitor.UpdateState(state);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[Safety Manager] Error updating monitor {monitor.MonitorName}: {e.Message}");
                            }
                        }
                    }
                }
            }
        }
        
        private void OnMotorStateChanged(string oldState, string newState)
        {
            // Check for program start/stop based on motor state
            if (newState.ToLower() == "running")
            {
                // Get current module name from robot state when program starts
                var currentState = robotManager?.GetCurrentState();
                string moduleName = currentState?.currentModule;
                
                if (!string.IsNullOrEmpty(moduleName) && !isProgramCurrentlyRunning)
                {
                    StartProgramLogging(moduleName);
                }
            }
            else if (newState.ToLower() == "stopped" && isProgramCurrentlyRunning)
            {
                // Program stopped
                StopProgramLogging();
            }
        }
        
        
        private void OnSafetyEventOccurred(SafetyEvent safetyEvent)
        {
            // Get current robot state from robot manager
            if (safetyEvent.robotStateSnapshot == null && robotManager != null)
            {
                var currentState = robotManager.GetCurrentState();
                if (currentState != null)
                {
                    safetyEvent.robotStateSnapshot = new RobotStateSnapshot(currentState);
                }
            }
            
            OnSafetyEventDetected?.Invoke(safetyEvent);
            
            // Always log Resolved events regardless of minimum log level (they indicate problem resolution)
            bool shouldCollectEvent = enableJsonLogging && 
                                    (safetyEvent.eventType == SafetyEventType.Resolved || safetyEvent.eventType >= minimumLogLevel) &&
                                    (safetyEvent.robotStateSnapshot == null || !logOnlyWhenProgramRunning || safetyEvent.robotStateSnapshot.isProgramRunning);
            
            if (shouldCollectEvent)
            {
                // Collect safety event for program-based logging
                CollectSafetyEvent(safetyEvent);
            }
            else
            {
                LogSafetyEventToConsole(safetyEvent);
            }
        }
        
        
        private void StartProgramLogging(string programName)
        {
            isProgramCurrentlyRunning = true;
            currentProgramName = programName;
            programStartTime = DateTime.Now;
            currentProgramSafetyEvents.Clear();
            
            Debug.Log($"[Safety Manager] Program started - collecting safety events for: {programName}");
        }
        
        private void StopProgramLogging()
        {
            Debug.Log($"[Safety Manager] Program stopped: {currentProgramName} (motor state = stopped)");
            
            if (isProgramCurrentlyRunning && currentProgramSafetyEvents.Count > 0)
            {
                SaveCollectedSafetyEvents();
            }
            else if (isProgramCurrentlyRunning)
            {
                Debug.Log($"[Safety Manager] No safety events collected for program: {currentProgramName}");
            }
            
            isProgramCurrentlyRunning = false;
            currentProgramName = "";
            currentProgramSafetyEvents.Clear();
        }
        
        private void CollectSafetyEvent(SafetyEvent safetyEvent)
        {
            if (isProgramCurrentlyRunning)
            {
                currentProgramSafetyEvents.Add(safetyEvent);
                Debug.Log($"[Safety Manager] Collected {safetyEvent.eventType} from {safetyEvent.monitorName} ({currentProgramSafetyEvents.Count} events total)");
            }
            else
            {
                // Log individual events when no program is running
                LogSafetyEventToConsole(safetyEvent);
            }
        }
        
        private void SaveCollectedSafetyEvents()
        {
            try
            {
                var programLog = new ProgramSafetyLog
                {
                    programName = currentProgramName,
                    startTime = programStartTime,
                    endTime = DateTime.Now,
                    duration = DateTime.Now - programStartTime,
                    totalSafetyEvents = currentProgramSafetyEvents.Count,
                    safetyEvents = currentProgramSafetyEvents.ToArray()
                };
                
                string fileName = $"SafetyLog_{currentProgramName.Replace(".", "_")}_{programStartTime:yyyyMMdd_HHmmss}.json";
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                string fullPath = Path.Combine(projectPath, logDirectory, fileName);
                
                string jsonContent = JsonUtility.ToJson(programLog, true);
                File.WriteAllText(fullPath, jsonContent);
                
                Debug.Log($"[Safety Manager] Saved program safety log: {fileName} ({currentProgramSafetyEvents.Count} events)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Safety Manager] Failed to save program safety log: {e.Message}");
            }
        }
        
        private void LogSafetyEventToConsole(SafetyEvent safetyEvent)
        {
            string logLevel = safetyEvent.eventType.ToString().ToUpper();
            string programContext = safetyEvent.robotStateSnapshot.GetProgramContext();
            
            Debug.Log($"[Safety Manager] {logLevel} - {safetyEvent.monitorName}: {safetyEvent.description} | Program: {programContext}");
        }
        
        public void SetMonitorActive(string monitorName, bool active)
        {
            foreach (var monitor in safetyMonitors)
            {
                if (monitor.MonitorName == monitorName)
                {
                    monitor.SetActive(active);
                    Debug.Log($"[Safety Manager] {monitorName} monitor {(active ? "enabled" : "disabled")}");
                    break;
                }
            }
        }
        
        public List<string> GetActiveMonitors()
        {
            var activeMonitors = new List<string>();
            foreach (var monitor in safetyMonitors)
            {
                if (monitor.IsActive)
                {
                    activeMonitors.Add(monitor.MonitorName);
                }
            }
            return activeMonitors;
        }
        
        void OnDestroy()
        {
            // Unsubscribe from robot manager
            if (robotManager != null)
            {
                robotManager.OnMotorStateChanged -= OnMotorStateChanged;
                robotManager.OnStateUpdated -= OnRobotStateUpdated;
            }
            
            // Clear pending updates
            lock (pendingStateUpdates)
            {
                pendingStateUpdates.Clear();
            }
            
            // Save any pending program log before shutdown
            if (isProgramCurrentlyRunning && currentProgramSafetyEvents.Count > 0)
            {
                Debug.Log("[Safety Manager] Saving program log before shutdown");
                SaveCollectedSafetyEvents();
            }
            
            foreach (var monitor in safetyMonitors)
            {
                try
                {
                    monitor.OnSafetyEventDetected -= OnSafetyEventOccurred;
                    monitor.Shutdown();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Safety Manager] Error shutting down monitor {monitor.MonitorName}: {e.Message}");
                }
            }
        }
    }
    
    [Serializable]
    public class ProgramSafetyLog
    {
        [Header("Program Information")]
        public string programName = "";
        public DateTime startTime;
        public DateTime endTime;
        public TimeSpan duration;
        public int totalSafetyEvents = 0;
        
        [Header("Safety Events")]
        public SafetyEvent[] safetyEvents = new SafetyEvent[0];
    }
}