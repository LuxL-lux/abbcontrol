using System;
using System.Collections.Generic;
using UnityEngine;
using RobotSystem.Core;
using RobotSystem.Interfaces;

namespace RobotSystem.Safety
{
    /// <summary>
    /// Process flow safety monitor that ensures parts follow correct station sequences
    /// Detects invalid transitions and reports process violations as safety events
    /// </summary>
    public class ProcessFlowMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Process Flow Monitoring")]
        [SerializeField] private bool monitorAllParts = true;
        [SerializeField] private List<Part> specificParts = new List<Part>();
        [SerializeField] private bool autoDiscoverParts = true;
        [SerializeField] private bool autoDiscoverStations = true;
        
        [Header("Violation Settings")]
        [SerializeField] private bool treatSkippedStationsAsWarning = true;
        [SerializeField] private bool treatWrongSequenceAsCritical = true;
        [SerializeField] private float violationCooldownTime = 1.0f;
        
        [Header("Debug Settings")]
        [SerializeField] private bool debugLogging = false;
        
        public string MonitorName => "Process Flow Monitor";
        
        private void DebugLog(string message)
        {
            if (debugLogging) Debug.Log(message);
        }
        
        private void DebugLogWarning(string message)
        {
            if (debugLogging) Debug.LogWarning(message);
        }
        public bool IsActive { get; private set; } = true;
        
        public event Action<SafetyEvent> OnSafetyEventDetected;
        
        private List<Part> monitoredParts = new List<Part>();
        private List<Station> availableStations = new List<Station>();
        private Dictionary<string, DateTime> lastViolationTime = new Dictionary<string, DateTime>();
        private Dictionary<string, bool> partGripStates = new Dictionary<string, bool>(); // Track grip state for each part
        private Dictionary<string, Station> partLastKnownStations = new Dictionary<string, Station>(); // Track last station for each part
        
        private bool isInitialized = false;
        
        void Awake()
        {
            // Discover parts and stations on main thread
            if (autoDiscoverParts)
            {
                DiscoverParts();
            }
            
            if (autoDiscoverStations)
            {
                DiscoverStations();
            }
            
            // Subscribe to station events
            SubscribeToStationEvents();
            
            // Subscribe to part events
            SubscribeToPartEvents();
            
            isInitialized = true;
            //Debug.Log($"[{MonitorName}] Pre-initialized with {monitoredParts.Count} parts and {availableStations.Count} stations");
        }
        
        public void Initialize()
        {
            if (!isInitialized)
            {
                DebugLogWarning($"[{MonitorName}] Initialize called but component not properly pre-initialized in Awake");
            }
            else
            {
                //Debug.Log($"[{MonitorName}] Initialization confirmed - monitoring {monitoredParts.Count} parts across {availableStations.Count} stations");
            }
        }
        
        public void UpdateState(RobotState state)
        {
            // Process flow monitoring is purely event-driven
            // No periodic state updates needed
        }
        
        public void SetActive(bool active)
        {
            IsActive = active;
        }
        
        public void Shutdown()
        {
            IsActive = false;
            UnsubscribeFromStationEvents();
            UnsubscribeFromPartEvents();
        }
        
        private void DiscoverParts()
        {
            monitoredParts.Clear();
            
            if (monitorAllParts)
            {
                // Find all parts in scene
                Part[] allParts = FindObjectsByType<Part>(FindObjectsSortMode.None);
                monitoredParts.AddRange(allParts);
            }
            else
            {
                // Use specifically assigned parts
                foreach (var part in specificParts)
                {
                    if (part != null && !monitoredParts.Contains(part))
                    {
                        monitoredParts.Add(part);
                    }
                }
            }
            
            //Debug.Log($"[{MonitorName}] Discovered {monitoredParts.Count} parts to monitor");
        }
        
        private void DiscoverStations()
        {
            availableStations.Clear();
            
            Station[] allStations = FindObjectsByType<Station>(FindObjectsSortMode.None);
            availableStations.AddRange(allStations);
            
            // Sort the station by their index
            availableStations.Sort((a, b) => a.StationIndex.CompareTo(b.StationIndex));
            
            // Debug.Log($"[{MonitorName}] Discovered {availableStations.Count} stations");
        }
        
        private void SubscribeToStationEvents()
        {
            foreach (var station in availableStations)
            {
                if (station != null)
                {
                    station.OnPartEntered += OnPartEnteredStation;
                    station.OnPartExited += OnPartExitedStation;
                }
            }
        }
        
        private void UnsubscribeFromStationEvents()
        {
            foreach (var station in availableStations)
            {
                if (station != null)
                {
                    station.OnPartEntered -= OnPartEnteredStation;
                    station.OnPartExited -= OnPartExitedStation;
                }
            }
        }
        
        private void SubscribeToPartEvents()
        {
            foreach (var part in monitoredParts)
            {
                if (part != null)
                {
                    part.OnInvalidStationTransition += OnInvalidStationTransition;
                    part.OnStationChanged += OnPartStationChanged;
                }
            }
        }
        
        private void UnsubscribeFromPartEvents()
        {
            foreach (var part in monitoredParts)
            {
                if (part != null)
                {
                    part.OnInvalidStationTransition -= OnInvalidStationTransition;
                    part.OnStationChanged -= OnPartStationChanged;
                }
            }
        }
        
        private void OnPartEnteredStation(Part part, Station station)
        {
            if (!IsActive || part == null || station == null) return;
            
            // Check if this part should be monitored
            if (!ShouldMonitorPart(part)) return;
            
            // Update grip state and process semantic events
            ProcessPartStationEvent(part, station, isEntering: true);
        }
        
        private void OnPartExitedStation(Part part, Station station)
        {
            if (!IsActive || part == null || station == null) return;
            
            // Check if this part should be monitored
            if (!ShouldMonitorPart(part)) return;
            
            // Update grip state and process semantic events
            ProcessPartStationEvent(part, station, isEntering: false);
        }
        
        private void OnPartStationChanged(Part part, Station fromStation, Station toStation)
        {
            // This event is now handled by ProcessPartStationEvent - no additional logging needed
        }
        
        /// <summary>
        /// Process part station events with grip state awareness for semantic logging
        /// </summary>
        private void ProcessPartStationEvent(Part part, Station station, bool isEntering)
        {
            string partId = part.PartId;
            bool wasGripped = partGripStates.GetValueOrDefault(partId, false);
            bool isGripped = IsPartGrippedByRobot(part);
            Station lastStation = partLastKnownStations.GetValueOrDefault(partId, null);
            
            // Update states
            partGripStates[partId] = isGripped;
            if (isEntering && !isGripped)
            {
                partLastKnownStations[partId] = station;
            }
            
            // Handle semantic transitions based on grip state changes
            if (isEntering)
            {
                HandlePartEntered(part, station, wasGripped, isGripped, lastStation);
            }
            else
            {
                HandlePartExited(part, station, wasGripped, isGripped);
            }
        }
        
        private void HandlePartEntered(Part part, Station station, bool wasGripped, bool isGripped, Station lastStation)
        {
            if (!wasGripped && !isGripped)
            {
                // Part placed at station (ungripped → ungripped)
                if (lastStation != null && lastStation != station)
                {
                    // Validate transition
                    if (part.IsValidNextStation(station))
                    {
                        part.MoveToStation(station);
                        DebugLog($"[{MonitorName}] Part '{part.PartName}' placed at {station.StationName} ({part.GetCompletionPercentage():F1}% complete)");
                    }
                    else
                    {
                        HandleInvalidStationEntry(part, station);
                    }
                }
                else if (part.CurrentStation == null || part.CurrentStation == station)
                {
                    // Initial placement or confirming current station
                    if (part.CurrentStation == null)
                    {
                        part.MoveToStation(station);
                        DebugLog($"[{MonitorName}] Part '{part.PartName}' initialized at {station.StationName}");
                    }
                }
            }
            // Ignore gripped parts entering stations (re-parenting noise)
        }
        
        private void HandlePartExited(Part part, Station station, bool wasGripped, bool isGripped)
        {
            if (!wasGripped && isGripped)
            {
                // Part picked up from station (ungripped → gripped)
                DebugLog($"[{MonitorName}] Part '{part.PartName}' picked up from {station.StationName}");
            }
            // Ignore all other exit combinations (re-parenting noise)
        }
        
        /// <summary>
        /// Check if the part is currently gripped by this robot (child of robot hierarchy)
        /// </summary>
        private bool IsPartGrippedByRobot(Part part)
        {
            if (part == null) return false;
            
            // Check if part is a descendant of this robot GameObject
            Transform current = part.transform.parent;
            while (current != null)
            {
                if (current == this.transform)
                {
                    return true;
                }
                current = current.parent;
            }
            
            return false;
        }
        
        private void OnInvalidStationTransition(Part part, Station fromStation, Station attemptedStation)
        {
            if (!IsActive) return;
            
            HandleInvalidStationTransition(part, fromStation, attemptedStation);
        }
        
        private void HandleInvalidStationEntry(Part part, Station station)
        {
            string violationKey = $"{part.PartId}_{station.StationName}";
            
            // Check cooldown to prevent spam
            if (lastViolationTime.ContainsKey(violationKey))
            {
                if ((DateTime.Now - lastViolationTime[violationKey]).TotalSeconds < violationCooldownTime)
                {
                    return;
                }
            }
            
            lastViolationTime[violationKey] = DateTime.Now;
            
            HandleInvalidStationTransition(part, part.CurrentStation, station);
        }
        
        private void HandleInvalidStationTransition(Part part, Station fromStation, Station attemptedStation)
        {
            if (!ShouldMonitorPart(part)) return;
            
            var violationData = new ProcessFlowViolation()
            {
                partId = part.PartId,
                partName = part.PartName,
                fromStation = fromStation?.StationName ?? "None",
                fromStationName = fromStation?.StationName ?? "None",
                attemptedStation = attemptedStation?.StationName ?? "None",
                attemptedStationName = attemptedStation?.StationName ?? "None",
                requiredSequence = GetSequenceIds(part.RequiredStationSequence),
                currentSequenceIndex = part.CurrentSequenceIndex,
                expectedNextStation = part.GetNextRequiredStation()?.StationName ?? "None",
                violationType = DetermineViolationType(part, fromStation, attemptedStation)
            };
            
            // Determine safety event type based on violation severity
            SafetyEventType eventType = violationData.violationType switch
            {
                ProcessViolationType.WrongSequence => treatWrongSequenceAsCritical ? SafetyEventType.Critical : SafetyEventType.Warning,
                ProcessViolationType.SkippedStation => treatSkippedStationsAsWarning ? SafetyEventType.Warning : SafetyEventType.Info,
                ProcessViolationType.UnknownStation => SafetyEventType.Warning,
                _ => SafetyEventType.Warning
            };
            
            string description = $"Process flow violation: Part '{part.PartName}' attempted invalid transition from {violationData.fromStationName} to {violationData.attemptedStationName}. " +
                               $"Expected: {violationData.expectedNextStation}. Violation type: {violationData.violationType}";
            
            // Create safety event
            var safetyEvent = new SafetyEvent(
                MonitorName,
                eventType,
                description,
                null // Safety manager will provide robot state
            );
            
            // Add process flow specific data
            safetyEvent.SetEventData(violationData);
            
            // Trigger event
            OnSafetyEventDetected?.Invoke(safetyEvent);
        }
        
        private ProcessViolationType DetermineViolationType(Part part, Station fromStation, Station attemptedStation)
        {
            var sequence = part.RequiredStationSequence;
            if (sequence.Length == 0) return ProcessViolationType.UnknownStation;
            
            int fromIndex = fromStation != null ? Array.FindIndex(sequence, s => s != null && s.StationName == fromStation.StationName) : -1;
            int attemptedIndex = Array.FindIndex(sequence, s => s != null && s.StationName == attemptedStation.StationName);
            
            if (attemptedIndex == -1)
            {
                return ProcessViolationType.UnknownStation;
            }
            
            if (fromIndex == -1)
            {
                // Starting from unknown station
                return attemptedIndex == 0 ? ProcessViolationType.ValidTransition : ProcessViolationType.WrongSequence;
            }
            
            if (attemptedIndex == fromIndex + 1)
            {
                return ProcessViolationType.ValidTransition;
            }
            else if (attemptedIndex > fromIndex + 1)
            {
                return ProcessViolationType.SkippedStation;
            }
            else
            {
                return ProcessViolationType.WrongSequence;
            }
        }
        
        private bool ShouldMonitorPart(Part part)
        {
            if (!monitorAllParts)
            {
                return specificParts.Contains(part);
            }
            
            return monitoredParts.Contains(part);
        }
        
        
        private string[] GetSequenceIds(Station[] stations)
        {
            var ids = new List<string>();
            foreach (var station in stations)
            {
                ids.Add(station != null ? station.StationName : "NULL");
            }
            return ids.ToArray();
        }
        
        void OnDestroy()
        {
            Shutdown();
        }
    }
    
    [Serializable]
    public class ProcessFlowViolation
    {
        public string partId;
        public string partName;
        public string fromStation;
        public string fromStationName;
        public string attemptedStation;
        public string attemptedStationName;
        public string[] requiredSequence;
        public int currentSequenceIndex;
        public string expectedNextStation;
        public ProcessViolationType violationType;
        public string detectionTime;
        
        public ProcessFlowViolation()
        {
            detectionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
    
    public enum ProcessViolationType
    {
        ValidTransition,
        WrongSequence,      // Attempting to go backwards or to wrong station
        SkippedStation,     // Skipping required stations in sequence
        UnknownStation      // Station not in required sequence
    }
}