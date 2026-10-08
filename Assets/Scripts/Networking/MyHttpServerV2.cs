using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Collections;
using TMPro;

// Local-only web server for OAuth callbacks that come back to this PC's browser (Spotify).
// Nothing here needs to be reachable from the internet.
public class MyHttpServerV2 : MonoBehaviour
{
    [SerializeField] private GameManager _gameManager;
    [SerializeField] private TwitchApi _twitchApi;
    [SerializeField] private SpotifyDJ _spotifyDJ;

    private HttpListener _listener;
    
    public void Start()
    {
        StartListener();
    }

    public void RestartListener()
    {
        StartCoroutine(CRestartListener()); 
    }

    public IEnumerator CRestartListener()
    {
        StopListener();
        // Introduce a brief delay to allow socket resources to release
        yield return new WaitForSeconds(1);

        StartListener();
    }

    public void StopListener()
    {
        if (_listener != null)
        {
            _listener.Stop();
            _listener.Close();
            _listener.Abort();

            _listener = null;  // Clear reference for re-creation
            Debug.Log("Stopping listener on " + AppConfig.inst.GetS("HostToListenOn") + ":" + AppConfig.inst.GetI("localHostPort").ToString() + "/");
        }
    }

    public void StartListener()
    {
        try
        {
            if (_listener == null)
            {
                _listener = new HttpListener();
                string listenerURL = AppConfig.inst.GetS("HostToListenOn") + ":" + AppConfig.inst.GetI("localHostPort").ToString() + "/";
                _listener.Prefixes.Add(listenerURL);
            }

            if (_listener.IsListening)
                return;

            _listener.Start();
            Debug.Log("Starting listening on: " + AppConfig.inst.GetS("HostToListenOn") + ":" + AppConfig.inst.GetI("localHostPort").ToString() +
                            "\n islistening: " + _listener.IsListening);
            Receive();
        } catch(Exception e)
        {
            Debug.LogException(e);
        }

    }


    private void Receive()
    {
        _listener.BeginGetContext(new AsyncCallback(ListenerCallback), _listener);
    }


    private async void ListenerCallback(IAsyncResult result)
    {
        if (!_listener.IsListening)
            return;

        try
        {
            var context = _listener.EndGetContext(result);
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            byte[] responseData = await GetResponseData(request);

            // write response
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=UTF-8";
            response.Headers.Add("Access-Control-Allow-Origin: *");
            response.Headers.Add("Access-Control-Allow-Methods: GET,POST");
            response.OutputStream.Write(responseData, 0, responseData.Length);
            response.OutputStream.Close();
        }
        catch(Exception e)
        {
            Debug.LogError($"Exception caught in HttpServer Listener Callback: {e}");
        }

        Receive();
        
    }

    private async Task<byte[]> GetResponseData(HttpListenerRequest request)
    {

        byte[] responseData = new byte[0];

        if (request.HttpMethod == "OPTIONS" || request.RawUrl == "/favicon.ico")
        {
            return responseData; 
        }
        if (request.RawUrl == "/logs")
        {
            return Encoding.UTF8.GetBytes(
                $"<h1>Logs</h1>" +
                $"<pre>fake log contents here</pre>"
                );
        }

        string rawPostData = null;

        NameValueCollection parsedPostData = new NameValueCollection();

        // Tikfinity webhook content type: application/x-www-form-urlencoded
        // ChaosBot content type: application/x-www-form-urlencoded
        if (request.HttpMethod == "POST" && request.HasEntityBody)
        {
            var body = request.InputStream;
            var encoding = request.ContentEncoding;
            var reader = new StreamReader(body, encoding);
            rawPostData = reader.ReadToEnd();
            reader.Close();
            body.Close();
            Debug.Log("content type: " + request.ContentType + "rawPostData: " + rawPostData);

            parsedPostData = HttpUtility.ParseQueryString(rawPostData);
        }

        Debug.Log($"In ThreadId: {Thread.CurrentThread.ManagedThreadId} Process HttpRequest: URL=" + request.RawUrl + " Data=" + (rawPostData ?? ""));

        StringBuilder output = new StringBuilder();
        foreach (string key in parsedPostData.AllKeys)
        {
            output.AppendLine($"{key}: {parsedPostData[key]}");
        }
        Debug.Log(output.ToString());

        if (request.Url.LocalPath == "/spotifyToken")
        {
            if (!request.IsLocal)
                return UnauthorisedResponse();

            string spotifyState = request.QueryString.Get("state");
            if (_spotifyDJ._state != spotifyState)
                return UnauthorisedResponse();

            string code = request.QueryString.Get("code");//parsedPostData["code"];
            Debug.Log($"Received spotify code: {code}. Now using this code to request access token");
            using (var client = new HttpClient())
            {
                string redirectUri = "http://localhost:3001/spotifyToken";

                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AppConfig.inst.GetS("SpotifyClientID")}:{AppConfig.inst.GetS("SpotifyClientSecret")}")));
                tokenRequest.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", redirectUri)
                });

                var tokenResponseMsg = await client.SendAsync(tokenRequest);
                string tokenJson = await tokenResponseMsg.Content.ReadAsStringAsync();

                TokenResponse tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(tokenJson);

                await UnityMainThreadDispatcher.Instance().EnqueueAsync(async () => { await _spotifyDJ.ParseTokenResponse(tokenResponse); });
                return responseData;
            }

        }
        return responseData;

    }

    public byte[] UnauthorisedResponse()
    {
        return Encoding.UTF8.GetBytes(@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>OAuth Callback</title>
                </head>
                <body>
                    Unauthorized.
                </body>
                </html>");
    }

    public void OnDestroy()
    {
        if (_listener != null)
        {
            _listener.Stop();
            Debug.Log("Stopping listener on " + AppConfig.inst.GetS("HostToListenOn") + ":" + AppConfig.inst.GetI("localHostPort").ToString() + "/");
        }
    }


}

