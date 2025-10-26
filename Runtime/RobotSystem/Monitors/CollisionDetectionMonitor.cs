using System;
using System.Collections.Generic;
using UnityEngine;
using RobotSystem.Core;
using RobotSystem.Interfaces;
using Preliy.Flange;

namespace RobotSystem.Safety
{
    /// <summary>
    /// Collision detection safety monitor using Flange library and Unity colliders
    /// </summary>
    public class CollisionDetectionMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Collision Detection Settings")]
        [SerializeField] private LayerMask collisionLayers = -1;
        [SerializeField] private bool excludeProcessFlowLayer = true;
        public LayerMask CollisionLayers => GetFilteredCollisionLayers();
        [SerializeField] private bool useExistingCollidersOnly = false;
        [SerializeField] private float cooldownTime = 1.0f;
        [SerializeField] private List<string> criticalCollisionTags = new List<string> { "Machine", "Obstacles" };
        
        [Header("Robot Links")]
        [SerializeField] private List<Transform> robotLinks = new List<Transform>();
        [SerializeField] private bool autoFindRobotParts = true;
        
        [Header("Debug Settings")]
        [SerializeField] private bool debugLogging = false;
        
        public string MonitorName => "Collision Detector";
        
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
        
        private DateTime lastCollisionTime = DateTime.MinValue;
        private readonly List<CollisionInfo> activeCollisions = new List<CollisionInfo>();
        private Dictionary<Transform, List<Collider>> robotColliders = new Dictionary<Transform, List<Collider>>();
        private Dictionary<string, DateTime> collisionCooldowns = new Dictionary<string, DateTime>();
        private List<RobotPartCollisionDetector> cachedDetectors = new List<RobotPartCollisionDetector>();
        
        private bool isInitialized = false;
        
        void Awake()
        {
            // Perform Unity component discovery on main thread
            if (autoFindRobotParts)
            {
                FindRobotParts();
            }
            
            // Setup collision detection on robot parts
            SetupCollisionDetection();
            
            isInitialized = true;
            DebugLog($"[{MonitorName}] Pre-initialized with {robotLinks.Count} robot links");
        }
        
        public void Initialize()
        {
            // This method is now called from background threads, so we just verify initialization
            if (!isInitialized)
            {
                Debug.LogWarning($"[{MonitorName}] Initialize called but component not properly pre-initialized in Awake");
            }
            else
            {
                DebugLog($"[{MonitorName}] Initialization confirmed - {robotLinks.Count} robot links ready");
            }
        }
        
        public void UpdateState(RobotState state)
        {
            // No action needed - collision detection is purely event-driven through Unity triggers
            // The safety manager already has the current robot state when creating safety events
        }
        
        public void SetActive(bool active)
        {
            IsActive = active;
            if (!active)
            {
                activeCollisions.Clear();
            }
        }
        
        public void Shutdown()
        {
            activeCollisions.Clear();
            IsActive = false;
        }
        
        private void FindRobotParts()
        {
            robotLinks.Clear();
            
            // Find all Frame components - these represent the kinematic chain
            Frame[] frames = GetComponentsInChildren<Frame>();
            
            // Sort frames by hierarchy depth to establish parent-child relationships
            System.Array.Sort(frames, (a, b) => GetHierarchyDepth(a.transform).CompareTo(GetHierarchyDepth(b.transform)));
            
            foreach (var frame in frames)
            {
                robotLinks.Add(frame.transform);
            }
            
            // Also find Tool components (end effector/toolhead)
            Tool[] tools = GetComponentsInChildren<Tool>();
            foreach (var tool in tools)
            {
                robotLinks.Add(tool.transform);
            }
        }
        
        private int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            Transform current = transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }
        
        private MeshRenderer[] GetMeshesForFrame(Transform frameTransform)
        {
            List<MeshRenderer> frameMeshes = new List<MeshRenderer>();
            
            // Get all mesh renderers under this frame
            MeshRenderer[] allMeshes = frameTransform.GetComponentsInChildren<MeshRenderer>();
            
            // Filter out meshes that belong to child frames
            Frame[] childFrames = frameTransform.GetComponentsInChildren<Frame>();
            
            foreach (var mesh in allMeshes)
            {
                bool belongsToChildFrame = false;
                
                // Check if this mesh belongs to a child frame
                foreach (var childFrame in childFrames)
                {
                    if (childFrame.transform == frameTransform) continue; // Skip self
                    
                    if (IsChildOf(mesh.transform, childFrame.transform))
                    {
                        belongsToChildFrame = true;
                        break;
                    }
                }
                
                if (!belongsToChildFrame)
                {
                    frameMeshes.Add(mesh);
                }
            }
            
            return frameMeshes.ToArray();
        }
        
        private bool IsChildOf(Transform child, Transform parent)
        {
            Transform current = child.parent;
            while (current != null)
            {
                if (current == parent) return true;
                current = current.parent;
            }
            return false;
        }
        
        private Collider CreateColliderForMesh(MeshRenderer meshRenderer)
        {
            MeshCollider meshCollider = meshRenderer.gameObject.AddComponent<MeshCollider>();
            meshCollider.convex = true;
            meshCollider.isTrigger = true;
            return meshCollider;
        }
        
        private void SetupCollisionDetection()
        {
            foreach (var robotPart in robotLinks)
            {
                if (robotPart == null) continue;
                
                List<Collider> colliders = new List<Collider>();
                
                // Find mesh renderers that belong to this Frame but not to child Frames
                MeshRenderer[] meshes = GetMeshesForFrame(robotPart);
                
                if (useExistingCollidersOnly)
                {
                    // Only use existing colliders on the mesh objects
                    foreach (var mesh in meshes)
                    {
                        Collider[] existingColliders = mesh.GetComponents<Collider>();
                        colliders.AddRange(existingColliders);
                    }
                    
                    if (colliders.Count == 0)
                    {
                        DebugLogWarning($"[{MonitorName}] No colliders found for frame {robotPart.name} - skipping collision detection");
                        continue;
                    }
                }
                else
                {
                    // Add colliders to mesh objects
                    foreach (var mesh in meshes)
                    {
                        Collider[] existingColliders = mesh.GetComponents<Collider>();
                        if (existingColliders.Length > 0)
                        {
                            colliders.AddRange(existingColliders);
                        }
                        else
                        {
                            // Create appropriate collider for the mesh
                            Collider newCollider = CreateColliderForMesh(mesh);
                            if (newCollider != null)
                            {
                                colliders.Add(newCollider);
                            }
                        }
                    }
                }
                
                // Setup collision detection on all colliders for this frame
                foreach (var collider in colliders)
                {
                    // Ensure colliders are triggers for detection
                    if (!collider.isTrigger)
                    {
                        collider.isTrigger = true;
                    }
                    
                    // Add collision detector component
                    RobotPartCollisionDetector detector = collider.gameObject.GetComponent<RobotPartCollisionDetector>();
                    if (detector == null)
                    {
                        detector = collider.gameObject.AddComponent<RobotPartCollisionDetector>();
                    }
                    detector.Initialize(this, robotPart.name);
                    
                    // Cache detector for thread-safe access
                    cachedDetectors.Add(detector);
                }
                
                robotColliders[robotPart] = colliders;
            }
            
            // Setup collision ignoring between adjacent frames
            SetupAdjacentFrameIgnoring();
        }
        
        
        private void SetupAdjacentFrameIgnoring()
        {
            // Dynamically ignore collisions between adjacent frames in the kinematic chain
            Frame[] frames = GetComponentsInChildren<Frame>();
            
            // Sort by hierarchy depth to establish parent-child order
            System.Array.Sort(frames, (a, b) => GetHierarchyDepth(a.transform).CompareTo(GetHierarchyDepth(b.transform)));
            
            // Ignore collisions between consecutive frames in the chain
            for (int i = 0; i < frames.Length - 1; i++)
            {
                Transform currentFrame = frames[i].transform;
                Transform nextFrame = frames[i + 1].transform;
                
                IgnoreCollisionsBetweenParts(currentFrame, nextFrame);
            }
        }
        
        private void IgnoreCollisionsBetweenParts(Transform part1, Transform part2)
        {
            if (!robotColliders.ContainsKey(part1) || !robotColliders.ContainsKey(part2)) return;
            
            var colliders1 = robotColliders[part1];
            var colliders2 = robotColliders[part2];
            
            foreach (var col1 in colliders1)
            {
                foreach (var col2 in colliders2)
                {
                    if (col1 != null && col2 != null)
                    {
                        Physics.IgnoreCollision(col1, col2, true);
                    }
                }
            }
        }
        
        private bool IsExistingCollision(CollisionInfo newCollision)
        {
            foreach (var existing in activeCollisions)
            {
                if (existing.robotLink == newCollision.robotLink && 
                    existing.collisionObject == newCollision.collisionObject)
                {
                    return true;
                }
            }
            return false;
        }
        
        public void OnRobotPartCollision(string robotPartName, Collider hitCollider)
        {
            // Safety check for null collider
            if (hitCollider == null)
            {
                DebugLogWarning($"[{MonitorName}] Null collider detected for part: {robotPartName}");
                return;
            }
            
            // Check if this is a gripped workobject/part - ignore collision if gripped
            if (IsGrippedPart(hitCollider))
            {
                return; // Don't report collision with gripped parts
            }
            
            string hitObjectName = hitCollider.gameObject.name;
            Vector3 collisionPoint = hitCollider.ClosestPoint(transform.position);
            
            // Check collision cooldown
            string collisionKey = $"{robotPartName}_{hitObjectName}";
            if (collisionCooldowns.ContainsKey(collisionKey))
            {
                if ((DateTime.Now - collisionCooldowns[collisionKey]).TotalSeconds < cooldownTime)
                {
                    return; // Still in cooldown
                }
            }
            
            // Update cooldown
            collisionCooldowns[collisionKey] = DateTime.Now;
            lastCollisionTime = DateTime.Now;
            
            // Check if this is a critical collision
            bool isCritical = IsCriticalCollision(hitCollider);
            
            // Create collision info
            var collision = new CollisionInfo()
            {
                robotLink = robotPartName,
                collisionObject = hitObjectName,
                collisionPoint = collisionPoint,
                distance = Vector3.Distance(transform.position, collisionPoint)
            };
            
            // Create safety event - safety manager will provide robot state
            var eventType = isCritical ? SafetyEventType.Critical : SafetyEventType.Warning;
            var description = isCritical ? 
                $"CRITICAL COLLISION: {robotPartName} -> {hitObjectName}" :
                $"Collision detected between {robotPartName} and {hitObjectName}";
                
            var safetyEvent = new SafetyEvent(
                MonitorName,
                eventType,
                description,
                null // Safety manager will provide robot state
            );
            
            // Add collision-specific data
            safetyEvent.SetEventData(collision);
            
            // Trigger event
            OnSafetyEventDetected?.Invoke(safetyEvent);
        }
        
        private bool IsCriticalCollision(Collider hitCollider)
        {
            // Check the hit object itself
            if (HasCriticalTag(hitCollider.gameObject))
            {
                return true;
            }
            
            // Check parent objects up the hierarchy
            Transform current = hitCollider.transform.parent;
            while (current != null)
            {
                if (HasCriticalTag(current.gameObject))
                {
                    return true;
                }
                current = current.parent;
            }
            
            return false;
        }
        
        private bool HasCriticalTag(GameObject obj)
        {
            foreach (string criticalTag in criticalCollisionTags)
            {
                if (obj.CompareTag(criticalTag) || 
                    obj.name.ToLower().Contains(criticalTag.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Check if the colliding object is a part that's currently gripped by the robot
        /// </summary>
        private bool IsGrippedPart(Collider hitCollider)
        {
            // Check if the collider has a Part component
            var part = hitCollider.GetComponent<RobotSystem.Core.Part>();
            if (part == null) return false;
            
            // Find gripper and check if it's gripping this part
            var gripper = FindFirstObjectByType<Preliy.Flange.Common.Gripper>();
            if (gripper == null || !gripper.Gripped) return false;
            
            // Check if this part is a child of the gripper (indicating it's gripped)
            Transform current = hitCollider.transform;
            while (current != null)
            {
                if (current == gripper.transform)
                {
                    return true;
                }
                current = current.parent;
            }
            
            return false;
        }
        
        
        void OnDrawGizmosSelected()
        {
            if (!IsActive) return;
            
            // Draw collision detection status indicators
            Gizmos.color = Color.green;
            
            foreach (var robotPart in robotLinks)
            {
                if (robotPart != null)
                {
                    Gizmos.DrawWireSphere(robotPart.position, 0.1f);
                }
            }
            
            // Draw collision points
            Gizmos.color = Color.yellow;
            foreach (var collision in activeCollisions)
            {
                Gizmos.DrawSphere(collision.collisionPoint, 0.02f);
            }
        }
        
        /// <summary>
        /// Get collision layers with ProcessFlow layer excluded if enabled
        /// </summary>
        private LayerMask GetFilteredCollisionLayers()
        {
            LayerMask filteredLayers = collisionLayers;
            
            if (excludeProcessFlowLayer)
            {
                // Remove ProcessFlow layer (31) from collision detection
                int processFlowLayer = 31;
                filteredLayers &= ~(1 << processFlowLayer);
            }
            
            return filteredLayers;
        }
    }
    
    // Helper component for individual robot parts
    public class RobotPartCollisionDetector : MonoBehaviour
    {
        private CollisionDetectionMonitor parentDetector;
        private string partName;
        
        public void Initialize(CollisionDetectionMonitor parent, string name)
        {
            parentDetector = parent;
            partName = name;
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (parentDetector != null)
            {
                // Check if we should detect collision with this layer
                int otherLayer = 1 << other.gameObject.layer;
                if ((parentDetector.CollisionLayers.value & otherLayer) != 0)
                {
                    parentDetector.OnRobotPartCollision(partName, other);
                }
            }
        }
    }
    
    [Serializable]
    public class CollisionInfo
    {
        public string robotLink;
        public string collisionObject;
        public Vector3 collisionPoint;
        public float distance;
        public string detectionTime;
        
        public CollisionInfo()
        {
            detectionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
}