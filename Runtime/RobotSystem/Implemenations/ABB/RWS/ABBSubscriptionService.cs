using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace RobotSystem.ABB.RWS
{
    public class ABBSubscriptionService
    {
        private readonly string baseUrl;
        private readonly HttpClient httpClient;

        public string SubscriptionGroupId { get; private set; }
        public string WebSocketUrl { get; private set; }

        public ABBSubscriptionService(string robotIP, HttpClient httpClient)
        {
            baseUrl = $"http://{robotIP}";
            this.httpClient = httpClient;
        }

        public async Task<(bool success, string initialStateData)> CreateSubscriptionAsync(string sessionCookie)
        {
            if (string.IsNullOrEmpty(sessionCookie))
            {
                Debug.LogError("[ABB Subscription] Cannot create subscription without valid session cookie");
                return (false, null);
            }

            string subscriptionUrl = $"{baseUrl}/subscription";

            var subscriptions = new List<SubscriptionData>
            {
                new SubscriptionData(1, "/rw/iosystem/signals/Local/DRV_1/DO_GripperOpen;state", 2), //High Prio for IOSgnals
                new SubscriptionData(2, "/rw/rapid/execution;ctrlexecstate", 1),
                new SubscriptionData(3, "/rw/rapid/tasks/T_ROB1/pcp;programpointerchange", 1),
                new SubscriptionData(4, "/rw/panel/ctrl-state", 1),
                new SubscriptionData(5, "/rw/rapid/execution;rapidexeccycle", 1),
            };

            StringBuilder sb = new StringBuilder();
            foreach (var sub in subscriptions)
            {
                if (sb.Length > 0)
                {
                    sb.Append("&");
                }
                sb.Append($"resources={sub.Id}&{sub.Id}={sub.Resource}&{sub.Id}-p={sub.Priority}");
            }
            string postData = sb.ToString();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, subscriptionUrl);

                // Set cookie and accept header
                request.Headers.Add("Cookie", sessionCookie);
                
                request.Headers.TryAddWithoutValidation("Accept", "application/hal+json;v=2.0");

                // Create content
                request.Content = new StringContent(postData, Encoding.UTF8);

                // Remove the default media type
                request.Content.Headers.ContentType = null;
                // Overwrite content header to prevent being plain/charset
                request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded;v=2.0");

                // Debug.Log(await FormatRequest(request));

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.Location != null)
                    {
                        string location = response.Headers.Location.ToString();
                        SubscriptionGroupId = ExtractGroupIdFromLocation(location);
                        
                        // Handle WebSocket URL - add port if missing
                        WebSocketUrl = location;
                        
                        // Read the response body which contains initial state data
                        string initialStateData = await response.Content.ReadAsStringAsync();
                        
                        // Debug.Log($"[ABB Subscription] WebSocket URL: {WebSocketUrl}");
                        return (true, initialStateData);
                    }
                    else
                    {
                        Debug.LogError("[ABB Subscription] No Location header in subscription response");
                    }
                }
                else
                {
                    Debug.LogError($"[ABB Subscription] Subscription creation failed: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Subscription] Subscription creation failed: {e.Message}");
            }

            return (false, null);
        }

        public async Task DeleteSubscriptionAsync(string sessionCookie)
        {
            if (string.IsNullOrEmpty(SubscriptionGroupId) || string.IsNullOrEmpty(sessionCookie))
                return;

            string deleteUrl = $"{baseUrl}/subscription/{SubscriptionGroupId}";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
                request.Headers.Add("Cookie", sessionCookie);
                request.Headers.Add("Accept", "application/hal+json;v=2.0");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Debug.Log($"[ABB Subscription] Subscription {SubscriptionGroupId} deleted successfully");
                }
                else
                {
                    Debug.LogError($"[ABB Subscription] Failed to delete subscription: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Subscription] Failed to delete subscription: {e.Message}");
            }
            finally
            {
                Reset();
            }
        }

        public void Reset()
        {
            SubscriptionGroupId = null;
            WebSocketUrl = null;
        }

        private string ExtractGroupIdFromLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return null;

            int lastSlash = location.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < location.Length - 1)
            {
                string groupId = location.Substring(lastSlash + 1);

                // Remove any query parameters
                int questionMark = groupId.IndexOf('?');
                if (questionMark >= 0)
                {
                    groupId = groupId.Substring(0, questionMark);
                }

                return groupId;
            }

            return null;
        }
        private async Task<string> FormatRequest(HttpRequestMessage request)
        {
            var msg = $"{request.Method} {request.RequestUri} HTTP/{request.Version}\n";

            foreach (var header in request.Headers)
            {
                msg += $"{header.Key}: {string.Join(", ", header.Value)}\n";
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    msg += $"{header.Key}: {string.Join(", ", header.Value)}\n";
                }
                msg += "\n" + await request.Content.ReadAsStringAsync();
            }

            return msg;
        }

        private async Task<string> FormatResponse(HttpResponseMessage response)
        {
            var msg = $"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}\n";

            foreach (var header in response.Headers)
            {
                msg += $"{header.Key}: {string.Join(", ", header.Value)}\n";
            }
            if (response.Content != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    msg += $"{header.Key}: {string.Join(", ", header.Value)}\n";
                }
                msg += "\n" + await response.Content.ReadAsStringAsync();
            }

            return msg;
        }

        private class SubscriptionData
        {
            public int Id;
            public string Resource;
            public int Priority;

            public SubscriptionData(int id, string resource, int priority)
            {
                Id = id;
                Resource = resource;
                Priority = priority;
            }
        }
    }
}