using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace RobotSystem.ABB.RWS
{
    public class ABBAuthenticationService
    {
        private readonly string baseUrl;
        private readonly string authHeader;
        private readonly HttpClient httpClient;

        public string SessionCookie { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public ABBAuthenticationService(string robotIP, string username, string password, HttpClient httpClient)
        {
            baseUrl = $"http://{robotIP}";
            authHeader = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
            this.httpClient = httpClient;
        }

        public async Task<bool> AuthenticateAsync()
        {
            string loginUrl = $"{baseUrl}/";

            try
            {
                SessionCookie = "";

                var request = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                request.Headers.Add("Accept", "application/hal+json;v=2.0");
                request.Headers.Add("Authorization", $"Basic {authHeader}");

                // Debug.Log(await FormatRequest(request));

                var response = await httpClient.SendAsync(request);

                // Debug.Log(await FormatResponse(response));

                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
                    {
                        string setCookieHeader = string.Join("; ", cookieHeaders);
                        if (!string.IsNullOrEmpty(setCookieHeader))
                        {
                            SessionCookie = ExtractSessionCookie(setCookieHeader);

                            if (!string.IsNullOrEmpty(SessionCookie))
                            {
                                IsAuthenticated = true;
                                // Debug.Log($"[ABB Auth] Authentication successful");
                                return true;
                            }
                            Debug.LogError("[ABB Auth] Authentication failed: Cookie could not be parsed");
                        }
                        else
                        {
                            Debug.LogError("[ABB Auth] Authentication successful but no session cookie received.");
                        }
                    }
                    else
                    {
                        Debug.LogError("[ABB Auth] Authentication successful but no Set-Cookie header received.");
                    }
                }
                else
                {
                    Debug.LogError($"[ABB Auth] Authentication failed: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Auth] Authentication failed: {e.Message}");
            }

            return false;
        }

        public async Task LogoutAsync()
        {
            if (!IsAuthenticated || string.IsNullOrEmpty(SessionCookie))
                return;

            string logoutUrl = $"{baseUrl}/logout";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, logoutUrl);
                request.Headers.Add("Cookie", SessionCookie);
                request.Headers.Add("Accept", "application/hal+json;v=2.0");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                }
                else
                {
                    Debug.LogError($"[ABB Auth] Logout failed: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ABB Auth] Logout failed: {e.Message}");
            }
            finally
            {
                Reset();
            }
        }

        public void Reset()
        {
            IsAuthenticated = false;
            SessionCookie = null;
        }

        private string ExtractSessionCookie(string setCookieHeader)
        {
            if (string.IsNullOrEmpty(setCookieHeader))
                return null;

            // Debug.Log($"Cookie Header: {setCookieHeader}");
            
            // We need to extract BOTH session cookies: -http-session- and ABBCX
            string httpSession = null;
            string abbcx = null;
            string[] cookieParts = setCookieHeader.Split(';');
            
            foreach (string part in cookieParts)
            {
                string trimmedPart = part.Trim();
                if (trimmedPart.StartsWith("-http-session-="))
                {
                    httpSession = trimmedPart;
                }
                else if (trimmedPart.StartsWith("ABBCX="))
                {
                    abbcx = trimmedPart;
                }
            }

            // Combine both cookies if found
            if (!string.IsNullOrEmpty(httpSession) && !string.IsNullOrEmpty(abbcx))
            {
                string combinedCookie = $"{httpSession}; {abbcx}";
                // Debug.Log($"Extracted Session Cookies: {combinedCookie}");
                return combinedCookie;
            }
            else if (!string.IsNullOrEmpty(httpSession))
            {
                Debug.Log($"Found only http-session: {httpSession}");
                return httpSession;
            }
            else if (!string.IsNullOrEmpty(abbcx))
            {
                Debug.Log($"Found only ABBCX: {abbcx}");
                return abbcx;
            }

            // Fallback: if no specific session cookies found, return the first cookie
            if (cookieParts.Length > 0)
            {
                string fallbackCookie = cookieParts[0].Trim();
                Debug.Log($"Fallback Cookie: {fallbackCookie}");
                return fallbackCookie;
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


    }

    
}