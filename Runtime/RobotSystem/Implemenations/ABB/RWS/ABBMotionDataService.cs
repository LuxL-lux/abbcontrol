using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using UnityEngine;
using Newtonsoft.Json;
using RobotSystem.Core;
using RobotSystem.Interfaces;

namespace RobotSystem.ABB.RWS
{
    public class ABBMotionDataService
    {
        public event Action<float[]> OnJointDataReceived;
        public event Action<string> OnError;
        
        private readonly string robotIP;
        private readonly string robName;
        private readonly HttpClient httpClient;
        private readonly object dataLock = new object();
        private string sessionCookie;
        
        private CancellationTokenSource cancellationTokenSource;
        private bool isRunning = false;
        private float[] currentJointAngles = new float[6];
        private DateTime lastUpdateTime = DateTime.MinValue;
        private int updateCount = 0;
        private double currentFrequency = 0.0;
        
        // Performance tracking
        private readonly System.Diagnostics.Stopwatch performanceStopwatch = new System.Diagnostics.Stopwatch();
        
        public bool IsRunning => isRunning;
        public float[] CurrentJointAngles 
        { 
            get 
            { 
                lock (dataLock) 
                { 
                    return (float[])currentJointAngles.Clone(); 
                } 
            } 
        }
        public double UpdateFrequency => currentFrequency;
        
        public ABBMotionDataService(string robotIP, string robName, HttpClient httpClient, string sessionCookie = null)
        {
            this.robotIP = robotIP ?? throw new ArgumentNullException(nameof(robotIP));
            this.robName = robName ?? "ROB1";
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.sessionCookie = sessionCookie;
        }
        
        public async Task StartAsync(int pollingIntervalMs = 100)
        {
            if (isRunning)
            {
                Debug.LogWarning("[ABB Motion] Service is already running");
                return;
            }
            
            cancellationTokenSource = new CancellationTokenSource();
            isRunning = true;
            
            
            try
            {
                await Task.Run(() => MotionDataPollingLoop(pollingIntervalMs, cancellationTokenSource.Token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Motion] Service error: {e.Message}");
                OnError?.Invoke($"Motion data service error: {e.Message}");
            }
            finally
            {
                isRunning = false;
            }
        }
        
        public void Stop()
        {
            if (!isRunning) return;
            
            cancellationTokenSource?.Cancel();
        }
        
        private async Task MotionDataPollingLoop(int pollingIntervalMs, CancellationToken cancellationToken)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Restart();
                
                try
                {
                    await FetchJointData(cancellationToken);
                    UpdatePerformanceMetrics();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ABB Motion] Joint data fetch error: {e.Message}");
                    OnError?.Invoke($"Joint data fetch error: {e.Message}");
                    
                    // Add delay on error to prevent spam
                    await Task.Delay(Math.Max(pollingIntervalMs, 1000), cancellationToken);
                    continue;
                }
                
                stopwatch.Stop();
                
                // Calculate sleep time to maintain polling interval
                int sleepTime = Math.Max(0, pollingIntervalMs - (int)stopwatch.ElapsedMilliseconds);
                if (sleepTime > 0)
                {
                    await Task.Delay(sleepTime, cancellationToken);
                }
            }
        }
        
        private async Task FetchJointData(CancellationToken cancellationToken)
        {
            // Use the same URL pattern as the original ABBDataStream
            string url = $"http://{robotIP}/rw/motionsystem/mechunits/{robName}/jointtarget";
            
            // Create request with proper headers (same as subscription service)
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Add("Accept", "application/hal+json;v=2.0");
            request.Headers.Add("Cookie", sessionCookie);

            // Debug.Log(request);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();

            dynamic obj = JsonConvert.DeserializeObject(result);
            var state = obj?.state?[0];
            // Debug.Log(state);

            if (state != null)
            {
                var jointData = new float[6]
                {
                    (float)(state.rax_1 ?? 0.0),
                    (float)(state.rax_2 ?? 0.0),
                    (float)(state.rax_3 ?? 0.0),
                    (float)(state.rax_4 ?? 0.0),
                    (float)(state.rax_5 ?? 0.0),
                    (float)(state.rax_6 ?? 0.0)
                };

                lock (dataLock)
                {
                    Array.Copy(jointData, currentJointAngles, 6);
                }

                // Notify listeners
                OnJointDataReceived?.Invoke((float[])jointData.Clone());
            }
        }
        
        private void UpdatePerformanceMetrics()
        {
            DateTime now = DateTime.Now;
            updateCount++;
            
            if (lastUpdateTime != DateTime.MinValue)
            {
                TimeSpan timeDiff = now - lastUpdateTime;
                if (timeDiff.TotalMilliseconds > 0)
                {
                    currentFrequency = 1000.0 / timeDiff.TotalMilliseconds;
                }
            }
            
            lastUpdateTime = now;
        }
        
        public void Dispose()
        {
            Stop();
            cancellationTokenSource?.Dispose();
        }
    }
}