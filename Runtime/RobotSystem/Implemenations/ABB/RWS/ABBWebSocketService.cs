using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
using RobotSystem.Core;
using RobotSystem.Interfaces;
using UnityEngine;

namespace RobotSystem.ABB.RWS
{
    public class ABBWebSocketService
    {
        private WebSocket websocket;
        private readonly List<IRobotDataParser> dataParsers;
        private readonly MonoBehaviour owner;

        public bool IsConnected => websocket?.State == WebSocketState.Open;

        public event Action<RobotState> OnDataReceived;
        public event Action<bool> OnConnectionStateChanged;

        public ABBWebSocketService(MonoBehaviour owner, List<IRobotDataParser> dataParsers)
        {
            this.owner = owner;
            this.dataParsers = dataParsers ?? new List<IRobotDataParser>();
        }

        public async Task<bool> SetupWebSocketAsync(string websocketUrl, string sessionCookie)
        {
            if (string.IsNullOrEmpty(websocketUrl))
            {
                Debug.LogError(
                    "[ABB WebSocket] Cannot setup WebSocket: No WebSocket URL available"
                );
                return false;
            }

            // Debug.Log($"[ABB WebSocket] Setting up WebSocket connection to: {websocketUrl}");
            // Debug.Log($"[ABB WebSocket] Session Cookie: {sessionCookie}");

            try
            {
                var headers = new Dictionary<string, string> { { "Cookie", sessionCookie } };
                websocket = new WebSocket(websocketUrl, "rws_subscription", headers);

                websocket.OnOpen += () =>
                {
                    OnConnectionStateChanged?.Invoke(true);
                };

                websocket.OnError += (e) =>
                {
                    Debug.LogError("[ABB WebSocket] WebSocket Error: " + e);
                    OnConnectionStateChanged?.Invoke(false);
                };

                websocket.OnClose += (e) =>
                {
                    OnConnectionStateChanged?.Invoke(false);
                };

                websocket.OnMessage += (bytes) =>
                {
                    try
                    {
                        string message = System.Text.Encoding.UTF8.GetString(bytes);
                        ProcessMessage(message);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[ABB WebSocket] Error processing WebSocket message: {ex.Message}"
                        );
                    }
                };

                // Debug.Log("[ABB WebSocket] Connecting to WebSocket...");

                // Connect using async pattern from example
                await websocket.Connect();

                // Add 2 second wait for connection to stabilize (from working implementation)
                await Task.Delay(2000);

                if (websocket.State == WebSocketState.Open)
                {
                    return true;
                }
                else
                {
                    Debug.LogError(
                        $"[ABB WebSocket] WebSocket setup failed. State: {websocket.State}"
                    );
                    OnConnectionStateChanged?.Invoke(false);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB WebSocket] WebSocket setup failed: {e.Message}");
                OnConnectionStateChanged?.Invoke(false);
                return false;
            }
        }

        public async Task CloseWebSocketAsync()
        {
            if (websocket != null)
            {
                try
                {
                    if (
                        websocket.State == WebSocketState.Open
                        || websocket.State == WebSocketState.Connecting
                    )
                    {
                        await websocket.Close();

                        // Wait a bit for close to complete
                        await Task.Delay(1000);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ABB WebSocket] Error closing WebSocket: {e.Message}");
                }
                finally
                {
                    websocket = null;
                    OnConnectionStateChanged?.Invoke(false);
                }
            }
        }

        public void ForceClose()
        {
            if (websocket != null)
            {
                try
                {
                    if (
                        websocket.State == WebSocketState.Open
                        || websocket.State == WebSocketState.Connecting
                    )
                    {
                        // Force close synchronously
                        _ = websocket.Close(); // Fire and forget
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ABB WebSocket] Error force closing WebSocket: {e.Message}");
                }

                websocket = null;
                OnConnectionStateChanged?.Invoke(false);
            }
        }

        public void Update()
        {
            // Essential for NativeWebSocket message processing
            if (websocket != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                websocket.DispatchMessageQueue();
#endif
            }
        }

        private void ProcessMessage(string message)
        {
            if (string.IsNullOrEmpty(message) || dataParsers == null)
                return;

            var robotState = new RobotState();

            foreach (var parser in dataParsers)
            {
                if (parser != null && parser.CanParse(message))
                {
                    parser.ParseData(message, robotState);
                    OnDataReceived?.Invoke(robotState);
                    break;
                }
                else
                {
                    Debug.LogWarning($"[ABB WebSocket] Cant parse following message: {message}");
                }
            }
        }
    }
}

