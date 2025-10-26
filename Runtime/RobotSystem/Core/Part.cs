using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSystem.Core
{
    /// <summary>
    /// Represents a part in the pick and place process with defined station sequence
    /// Tracks current position and validates process flow
    /// </summary>
    public class Part : MonoBehaviour
    {
        [Header("Part Configuration")]
        [SerializeField] private string partId = "";
        [SerializeField] private string partName = "";
        [SerializeField] private string partType = "";
        
        [Header("Process Flow")]
        [SerializeField] private Station[] requiredStationSequence = new Station[0];
        [SerializeField] private bool enforceSequence = true;
        [SerializeField] private bool allowSkipStations = false;
        
        [Header("Current State")]
        [SerializeField] private Station currentStation = null;
        [SerializeField] private DateTime lastStationChangeTime;
        
        // Runtime tracking (not serialized)
        private int currentSequenceIndex = -1;
        
        public string PartId => partId;
        public string PartName => partName;
        public string PartType => partType;
        public Station CurrentStation => currentStation;
        public int CurrentSequenceIndex => currentSequenceIndex;
        public bool EnforceSequence => enforceSequence;
        public Station[] RequiredStationSequence => requiredStationSequence;
        
        // Events for process flow monitoring
        public event Action<Part, Station, Station> OnStationChanged; // (part, fromStation, toStation)
        public event Action<Part, Station, Station> OnInvalidStationTransition; // (part, fromStation, attemptedStation)
        
        // Process tracking
        private List<StationVisit> visitHistory = new List<StationVisit>();
        private bool isInitialized = false;
        
        void Awake()
        {
            // Set Part to Parts layer (layer 30) for station detection
            // This allows the part to be detected by station triggers while still
            // participating in normal collision detection
            gameObject.layer = 30; // Parts layer
            
            lastStationChangeTime = DateTime.Now;
            isInitialized = true;
        }
        
        /// <summary>
        /// Get the required station sequence for this part
        /// </summary>
        public Station[] GetStationSequence()
        {
            return requiredStationSequence;
        }
        
        /// <summary>
        /// Attempt to move to a new station - validates sequence if enforced
        /// </summary>
        public bool TryMoveToStation(Station newStation)
        {
            if (!isInitialized || newStation == null) return false;
            
            // If no sequence is defined or not enforced, allow any movement
            if (requiredStationSequence.Length == 0 || !enforceSequence)
            {
                return MoveToStation(newStation);
            }
            
            // Validate sequence
            if (IsValidNextStation(newStation))
            {
                return MoveToStation(newStation);
            }
            else
            {
                OnInvalidStationTransition?.Invoke(this, currentStation, newStation);
                return false;
            }
        }
        
        /// <summary>
        /// Check if the given station is a valid next station in the sequence
        /// </summary>
        public bool IsValidNextStation(Station station)
        {
            if (station == null || requiredStationSequence.Length == 0) return true;
            
            // Find target station in sequence
            int targetIndex = Array.FindIndex(requiredStationSequence, s => s != null && s.StationName == station.StationName);
            if (targetIndex == -1) return false; // Station not in sequence
            
            // If no current station, can only start at first station
            if (currentStation == null)
            {
                return targetIndex == 0;
            }
            
            // Find current station in sequence
            int currentIndex = Array.FindIndex(requiredStationSequence, s => s != null && s.StationName == currentStation.StationName);
            if (currentIndex == -1) return false; // Current station not in sequence
            
            if (allowSkipStations)
            {
                // Can move to any station later in sequence
                return targetIndex > currentIndex;
            }
            else
            {
                // Must move to immediate next station
                return targetIndex == currentIndex + 1;
            }
        }
        
        /// <summary>
        /// Force move to station (bypasses validation) - used by Station triggers
        /// </summary>
        public bool MoveToStation(Station newStation)
        {
            Station previousStation = currentStation;
            currentStation = newStation;
            lastStationChangeTime = DateTime.Now;
            
            // Update sequence index
            if (newStation != null && requiredStationSequence.Length > 0)
            {
                currentSequenceIndex = Array.FindIndex(requiredStationSequence, s => s != null && s.StationName == newStation.StationName);
            }
            else
            {
                currentSequenceIndex = -1;
            }
            
            // Record visit
            visitHistory.Add(new StationVisit
            {
                station = newStation,
                timestamp = lastStationChangeTime,
                sequenceIndex = currentSequenceIndex
            });
            // Trigger event
            OnStationChanged?.Invoke(this, previousStation, newStation);
            
            return true;
        }
        
        /// <summary>
        /// Get description of the required station sequence
        /// </summary>
        public string GetSequenceDescription()
        {
            if (requiredStationSequence.Length == 0) return "No sequence defined";
            
            var stationNames = new List<string>();
            foreach (var station in requiredStationSequence)
            {
                stationNames.Add(station != null ? station.StationName : "NULL");
            }
            
            return string.Join(" -> ", stationNames);
        }
        
        /// <summary>
        /// Get the next required station in sequence
        /// </summary>
        public Station GetNextRequiredStation()
        {
            if (requiredStationSequence.Length == 0 || currentSequenceIndex >= requiredStationSequence.Length - 1)
                return null;
                
            return requiredStationSequence[currentSequenceIndex + 1];
        }
        
        /// <summary>
        /// Check if the part has completed the entire sequence
        /// </summary>
        public bool IsSequenceComplete()
        {
            if (requiredStationSequence.Length == 0) return true;
            return currentSequenceIndex >= requiredStationSequence.Length - 1;
        }
        
        /// <summary>
        /// Get process completion percentage
        /// </summary>
        public float GetCompletionPercentage()
        {
            if (requiredStationSequence.Length == 0) return 100f;
            if (currentSequenceIndex < 0) return 0f;
            
            return ((float)(currentSequenceIndex + 1) / requiredStationSequence.Length) * 100f;
        }
        
        /// <summary>
        /// Get visit history for this part
        /// </summary>
        public List<StationVisit> GetVisitHistory()
        {
            return new List<StationVisit>(visitHistory);
        }
        
        /// <summary>
        /// Reset part to beginning of process
        /// </summary>
        public void ResetProcess()
        {
            currentStation = null;
            currentSequenceIndex = -1;
            visitHistory.Clear();
            lastStationChangeTime = DateTime.Now;
        }
        
        void OnDrawGizmosSelected()
        {
            // Draw connection lines to required stations
            if (requiredStationSequence.Length > 0)
            {
                Gizmos.color = Color.yellow;
                Vector3 partPos = transform.position;
                
                for (int i = 0; i < requiredStationSequence.Length - 1; i++)
                {
                    if (requiredStationSequence[i] != null && requiredStationSequence[i + 1] != null)
                    {
                        Vector3 fromPos = requiredStationSequence[i].transform.position;
                        Vector3 toPos = requiredStationSequence[i + 1].transform.position;
                        
                        // Draw arrow from station to station
                        Gizmos.DrawLine(fromPos, toPos);
                        
                        // Draw arrow head
                        Vector3 direction = (toPos - fromPos).normalized;
                        Vector3 arrowHead1 = toPos - direction * 0.2f + Vector3.Cross(direction, Vector3.up) * 0.1f;
                        Vector3 arrowHead2 = toPos - direction * 0.2f - Vector3.Cross(direction, Vector3.up) * 0.1f;
                        
                        Gizmos.DrawLine(toPos, arrowHead1);
                        Gizmos.DrawLine(toPos, arrowHead2);
                    }
                }
                
                // Highlight current station
                if (currentStation != null)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(currentStation.transform.position, 0.3f);
                }
            }
        }
    }
    
    [Serializable]
    public class StationVisit
    {
        public Station station;
        public DateTime timestamp;
        public int sequenceIndex;
        
        public string GetVisitInfo()
        {
            return $"{station?.StationName ?? "None"} at {timestamp:HH:mm:ss} (Index: {sequenceIndex})";
        }
    }
}