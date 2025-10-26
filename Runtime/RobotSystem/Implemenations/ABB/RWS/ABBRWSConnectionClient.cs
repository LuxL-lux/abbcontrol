using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using UnityEngine;
using RobotSystem.Interfaces;
using RobotSystem.Core;

namespace RobotSystem.ABB.RWS
{
    public class ABBRWSConnectionClient : MonoBehaviour, IRobotConnector
    {
        [Header("Connection Settings")]
        public string robotIP = "127.0.0.1";
        public string username = "Default User";
        public string password = "robotics";
        
        [Header("Motion Data Settings")]
        [SerializeField] private bool enableMotionData = true;
        [SerializeField] private bool enableMetadata = false;
        [SerializeField] private int motionPollingIntervalMs = 50;
        [SerializeField] private string robName = "ROB_1";

        [Header("Inspector Controls")]
        [Space(10)]
        [Button("Start Connection")]
        public bool startConnection;
        [Button("Stop Connection")]
        public bool stopConnection;

        [Header("Robot State")]
        [SerializeField] private RobotState robotState = new RobotState();
        
        [Header("Data Parsing")]
        [SerializeField] private List<IRobotDataParser> dataParsers = new List<IRobotDataParser>();
        
        // Service components
        private ABBAuthenticationService authService;
        private ABBSubscriptionService subscriptionService;
        private ABBWebSocketService webSocketService;
        private ABBMotionDataService motionDataService;
        private HttpClient sharedHttpClient;
        
        private bool isConnected = false;
        
        // IRobotConnector Implementation
        public event Action<RobotState> OnRobotStateUpdated;
        public event Action<bool> OnConnectionStateChanged;
        
        // Additional events for motion data
        public event Action<float[]> OnJointDataReceived;
        
        public bool IsConnected => isConnected;
        public RobotState CurrentState => robotState;

        void Start()
        {
            // Initialize robot state
            robotState.robotType = "ABB";
            robotState.robotIP = robotIP;
            
            // Add default ABB parser
            if (dataParsers.Count == 0)
            {
                dataParsers.Add(new ABBRWSDataParser());
            }
            
            // Initialize HTTP client and services
            InitializeHttpClient();
            InitializeServices();
        }
        
        private void InitializeHttpClient()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Credentials = new System.Net.NetworkCredential(username, password),
                    Proxy = null,
                    UseProxy = false
                };
                
                sharedHttpClient = new HttpClient(handler);
                sharedHttpClient.Timeout = TimeSpan.FromSeconds(10);
                
                // Debug.Log("[ABB RWS] HTTP client initialized");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB RWS] Failed to initialize HTTP client: {e.Message}");
            }
        }
        
        private void InitializeServices()
        {
            // Initialize authentication service
            authService = new ABBAuthenticationService(robotIP, username, password, sharedHttpClient);
            
            // Initialize subscription service
            subscriptionService = new ABBSubscriptionService(robotIP, sharedHttpClient);
            
            // Initialize WebSocket service
            webSocketService = new ABBWebSocketService(this, dataParsers);
            webSocketService.OnDataReceived += OnWebSocketDataReceived;
            webSocketService.OnConnectionStateChanged += OnWebSocketConnectionChanged;
            
        }

        void Update()
        {
            // Handle inspector button clicks
            if (startConnection && !isConnected)
            {
                startConnection = false;
                Connect();
            }
            
            if (stopConnection && isConnected)
            {
                stopConnection = false;
                Disconnect();
            }
            
            // Update WebSocket message queue
            webSocketService?.Update();
        }

        // IRobotConnector Interface Methods
        public void Connect()
        {
            if (isConnected) return;
            
            StartCoroutine(ConnectSequence());
        }
        
        public void Disconnect()
        {
            if (!isConnected) return;
            
            StartCoroutine(DisconnectSequence());
        }

        private IEnumerator ConnectSequence()
        {
            // Step 1: Authenticate
            var authTask = authService.AuthenticateAsync();
            yield return new WaitUntil(() => authTask.IsCompleted);
            
            if (!authService.IsAuthenticated)
            {
                Debug.LogError("[ABB RWS] Authentication failed");
                yield break;
            }

            if (enableMetadata)
            {
                var subscriptionTask = subscriptionService.CreateSubscriptionAsync(authService.SessionCookie);
                yield return new WaitUntil(() => subscriptionTask.IsCompleted);

                var (success, initialStateData) = subscriptionTask.Result;
                if (!success || string.IsNullOrEmpty(subscriptionService.SubscriptionGroupId))
                {
                    Debug.LogError("[ABB RWS] Subscription creation failed");
                    yield break;
                }

                // Process initial state data from subscription response
                if (!string.IsNullOrEmpty(initialStateData))
                {
                    ProcessInitialStateData(initialStateData);
                }

                // Step 3: Setup WebSocket (fire and forget - events will handle success/failure)
                _ = webSocketService.SetupWebSocketAsync(
                    subscriptionService.WebSocketUrl,
                    authService.SessionCookie);
            }
            
            // Start motion data service if enabled (independent of WebSocket)
            if (enableMotionData)
            {
                StartMotionDataService();
            }
            
            isConnected = true;
            OnConnectionStateChanged?.Invoke(true);
        }
        
        private IEnumerator DisconnectSequence()
        {
            // Stop motion data service first
            if (motionDataService != null)
            {
                motionDataService.OnJointDataReceived -= OnJointDataReceivedInternal;
                motionDataService.OnError -= OnMotionDataError;
                motionDataService.Stop();
                motionDataService.Dispose();
                motionDataService = null;
            }
            
            // Close WebSocket
            if (webSocketService.IsConnected)
            {
                var wsCloseTask = webSocketService.CloseWebSocketAsync();
                yield return new WaitUntil(() => wsCloseTask.IsCompleted);
            }
            
            // Delete subscription
            if (!string.IsNullOrEmpty(subscriptionService?.SubscriptionGroupId))
            {
                var deleteTask = subscriptionService.DeleteSubscriptionAsync(authService.SessionCookie);
                yield return new WaitUntil(() => deleteTask.IsCompleted);
            }
            
            // Logout
            if (authService.IsAuthenticated)
            {
                var logoutTask = authService.LogoutAsync();
                yield return new WaitUntil(() => logoutTask.IsCompleted);
            }
            
            isConnected = false;
            OnConnectionStateChanged?.Invoke(false);
        }
        
        private void StartMotionDataService()
        {
            try
            {
                motionDataService = new ABBMotionDataService(robotIP, robName, sharedHttpClient, authService.SessionCookie);
                
                // Subscribe to motion data events
                motionDataService.OnJointDataReceived += OnJointDataReceivedInternal;
                motionDataService.OnError += OnMotionDataError;
                
                
                // Start the service (non-blocking)
                _ = motionDataService.StartAsync(motionPollingIntervalMs);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB RWS] Failed to start motion data service: {e.Message}");
            }
        }
        
        private void OnJointDataReceivedInternal(float[] jointAngles)
        {
            if (jointAngles != null && jointAngles.Length >= 6)
            {
                //Debug.Log($"[ABB RWS] Joint data received: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F2")))}]");
                
                // Update robot state
                robotState.UpdateJointAngles(jointAngles, motionDataService?.UpdateFrequency ?? 0.0);
                
                // Notify external listeners
                OnJointDataReceived?.Invoke(jointAngles);
                OnRobotStateUpdated?.Invoke(robotState);
            }
            else
            {
                Debug.LogWarning("[ABB RWS] Invalid joint data received");
            }
        }
        
        private void OnMotionDataError(string error)
        {
            Debug.LogError($"[ABB RWS] Motion data error: {error}");
        }
        
        private void OnWebSocketDataReceived(RobotState state)
        {
            if (state != null)
            {
                // Selective merge: only update fields that have meaningful values
                bool hasUpdate = false;
                
                // Only update motor/execution state if it's not default
                if (!string.IsNullOrEmpty(state.motorState) && state.motorState != "unknown")
                {
                    robotState.UpdateMotorState(state.motorState);
                    hasUpdate = true;
                }
                
                // Only update program pointer if module is not empty
                if (!string.IsNullOrEmpty(state.currentModule))
                {
                    robotState.UpdateProgramPointer(state.currentModule, state.currentRoutine, state.currentLine, state.currentColumn);
                    hasUpdate = true;
                }
                
                // Only update controller state if it's not default
                if (!string.IsNullOrEmpty(state.controllerState))
                {
                    robotState.UpdateControllerState(state.controllerState);
                    hasUpdate = true;
                }

                // Only update execution cycle state if it's not default
                if (!string.IsNullOrEmpty(state.executionCycle))
                {
                    robotState.UpdateExecutionCycle(state.executionCycle);
                    hasUpdate = true;
                }
                
                // Copy IO signals from incoming state (these are the actual updates)
                if (state.ioSignals != null && state.ioSignals.Count > 0)
                {
                    foreach (var signal in state.ioSignals)
                    {
                        robotState.UpdateIOSignal(signal.Key, signal.Value);
                        hasUpdate = true;
                    }
                }
                
                // Only update if we actually received new data
                if (hasUpdate)
                {
                    robotState.lastUpdate = DateTime.Now;
                    
                    // Force inspector refresh in editor
                    #if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(this);
                    #endif
                                    
                    OnRobotStateUpdated?.Invoke(robotState);
                }
            }
        }
        
        private void OnWebSocketConnectionChanged(bool connected)
        {
            if (!connected && isConnected)
            {
                // WebSocket disconnected unexpectedly
                Debug.LogWarning("[ABB RWS] WebSocket disconnected unexpectedly");
            }
        }

        void OnDestroy()
        {
            // Perform synchronous cleanup for graceful shutdown
            PerformSynchronousCleanup();
        }
        
        private void PerformSynchronousCleanup()
        {
            
            // Stop motion data service
            if (motionDataService != null)
            {
                motionDataService.OnJointDataReceived -= OnJointDataReceivedInternal;
                motionDataService.OnError -= OnMotionDataError;
                motionDataService.Stop();
                motionDataService.Dispose();
                motionDataService = null;
            }
            
            // Force close WebSocket
            webSocketService?.ForceClose();
            
            // Quick logout attempt
            if (authService?.IsAuthenticated == true)
            {
                try
                {
                    _ = System.Threading.Tasks.Task.Run(() => PerformQuickLogout());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ABB RWS] Error during quick logout: {e.Message}");
                }
            }
            
            // Clean up HTTP client
            if (sharedHttpClient != null)
            {
                sharedHttpClient.Dispose();
                sharedHttpClient = null;
            }
            
            // Reset connection state
            isConnected = false;
            
        }
        
        private void ProcessInitialStateData(string initialStateData)
        {
            try
            {
                // Use the same parsers as WebSocket to process initial state
                if (dataParsers != null && dataParsers.Count > 0)
                {
                    var tempRobotState = new RobotState();
                    
                    foreach (var parser in dataParsers)
                    {
                        if (parser != null && parser.CanParse(initialStateData))
                        {
                            parser.ParseData(initialStateData, tempRobotState);
                            
                            // Update the main robot state with parsed initial data
                            UpdateRobotStateFromParsed(tempRobotState);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB RWS] Failed to process initial state data: {e.Message}");
            }
        }
        
        private void UpdateRobotStateFromParsed(RobotState parsedState)
        {
            // Selectively update robot state with non-default/empty values from parsed data
            if (!string.IsNullOrEmpty(parsedState.motorState) && parsedState.motorState != "unknown")
            {
                robotState.UpdateMotorState(parsedState.motorState);
            }
            
            if (!string.IsNullOrEmpty(parsedState.controllerState) && parsedState.controllerState != "unknown")
            {
                robotState.UpdateControllerState(parsedState.controllerState);
            }

            if (!string.IsNullOrEmpty(parsedState.executionCycle) && parsedState.executionCycle != "unknown")
            {
                robotState.UpdateExecutionCycle(parsedState.executionCycle);
            }
            
            if (!string.IsNullOrEmpty(parsedState.currentModule))
            {
                robotState.UpdateProgramPointer(parsedState.currentModule, parsedState.currentRoutine, parsedState.currentLine, parsedState.currentColumn);
            }
            
            // Update IO signals
            if (parsedState.ioSignals != null)
            {
                foreach (var signal in parsedState.ioSignals)
                {
                    robotState.UpdateIOSignal(signal.Key, signal.Value);
                }
            }
            
            // Trigger state updated event
            OnRobotStateUpdated?.Invoke(robotState);
        }

        private async System.Threading.Tasks.Task PerformQuickLogout()
        {
            try
            {
                await authService.LogoutAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ABB RWS] Quick logout failed: {e.Message}");
            }
        }
    }
}