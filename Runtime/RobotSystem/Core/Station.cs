using System;
using UnityEngine;

namespace RobotSystem.Core
{
    /// <summary>
    /// Represents a process station in the pick and place workflow
    /// Uses colliders to detect part presence and validate process flow
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Station : MonoBehaviour
    {
        [Header("Station Configuration")]
        [SerializeField] private string stationName = "";
        [SerializeField] private int stationIndex = 0;
        [SerializeField] private Color stationColor = Color.blue;
        
        [Header("Detection Settings")]
        [SerializeField] private LayerMask partDetectionLayers = -1;
        [SerializeField] private float detectionDelay = 0.1f;
        [SerializeField] private bool autoConfigureForProcessFlow = true;
        
        public string StationName => stationName;
        public int StationIndex => stationIndex;
        public Color StationColor => stationColor;
        
        // Events for process flow monitoring
        public event Action<Part, Station> OnPartEntered;
        public event Action<Part, Station> OnPartExited;
        
        private Collider stationCollider;
        private bool isInitialized = false;
        
        void Awake()
        {
            // Initialize on main thread
            stationCollider = GetComponent<Collider>();
            if (stationCollider != null)
            {
                stationCollider.isTrigger = true;
            }
            
            // Auto-configure for process flow to avoid collision detection interference
            if (autoConfigureForProcessFlow)
            {
                ConfigureForProcessFlow();
            }
            
            // Auto-generate name if empty
            if (string.IsNullOrEmpty(stationName))
            {
                stationName = $"Station {stationIndex}";
            }
            
            isInitialized = true;
        }
        
        void OnTriggerEnter(Collider other)
        {
            if (!isInitialized) return;
            
            // Directly check if its a part component
            Part part = other.GetComponent<Part>();
            
            if (part != null)
            {
                OnPartEntered?.Invoke(part, this);
            }
        }
        
        void OnTriggerExit(Collider other)
        {
            if (!isInitialized) return;
            
            // Directly check for Part component - no layer filtering
            Part part = other.GetComponent<Part>();
            if (part == null)
            {
                // Try to find Part component in parent objects
                part = other.GetComponentInParent<Part>();
            }
            
            if (part != null)
            {
                OnPartExited?.Invoke(part, this);
            }
        }
        
        /// <summary>
        /// Check if this station can be the next valid station for a part coming from another station
        /// </summary>
        public bool IsValidNextStation(Station fromStation, Part part)
        {
            if (fromStation == null || part == null) return false;
            
            // Get the required station sequence from the part
            var requiredSequence = part.GetStationSequence();
            if (requiredSequence == null || requiredSequence.Length == 0) return true; // No restrictions
            
            // Find current position in sequence
            int fromIndex = Array.FindIndex(requiredSequence, s => s.StationName == fromStation.StationName);
            int toIndex = Array.FindIndex(requiredSequence, s => s.StationName == this.StationName);
            
            if (fromIndex == -1 || toIndex == -1) return false; // Station not in sequence
            
            // Check if moving to the next station in sequence
            return toIndex == fromIndex + 1;
        }
        
        void OnDrawGizmosSelected()
        {
            // Draw station bounds and info
            if (stationCollider != null)
            {
                Gizmos.color = stationColor;
                Gizmos.matrix = transform.localToWorldMatrix;
                
                if (stationCollider is BoxCollider box)
                {
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                else if (stationCollider is SphereCollider sphere)
                {
                    Gizmos.DrawWireSphere(sphere.center, sphere.radius);
                }
            }
            
            // Draw station label
            Vector3 labelPosition = transform.position + Vector3.up * 0.5f;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(labelPosition, $"{stationName}\n(Index: {stationIndex})");
#endif
        }
        
        /// <summary>
        /// Configure station for process flow detection without interfering with collision detection
        /// </summary>
        private void ConfigureForProcessFlow()
        {
            // Set to ProcessFlow layer (layer 31 - usually unused)
            int processFlowLayer = 31;
            gameObject.layer = processFlowLayer;
            
            // Only detect Parts layer (layer 30) to avoid robot collision detection
            int partsLayer = 30;
            partDetectionLayers = 1 << partsLayer;
            
            // Ensure station collider doesn't interfere with physics/collision detection
            if (stationCollider != null)
            {
                stationCollider.isTrigger = true;
                
                // Make sure collision detection system ignores ProcessFlow layer
                // This prevents station triggers from being detected as collisions
                for (int layer = 0; layer < 32; layer++)
                {
                    if (layer != partsLayer && layer != processFlowLayer)
                    {
                        Physics.IgnoreLayerCollision(processFlowLayer, layer, true);
                    }
                }
            }
        }
        
        /// <summary>
        /// Manual configuration for custom layer setup
        /// </summary>
        [ContextMenu("Configure for Process Flow")]
        public void ManualConfigureForProcessFlow()
        {
            ConfigureForProcessFlow();
        }
        
    }
}