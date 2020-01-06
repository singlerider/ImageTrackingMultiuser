// %BANNER_BEGIN%
// ---------------------------------------------------------------------
// %COPYRIGHT_BEGIN%
//
// Copyright (c) 2018-present, Magic Leap, Inc. All Rights Reserved.
// Use of this file is governed by the Creator Agreement, located
// here: https://id.magicleap.com/creator-terms
//
// %COPYRIGHT_END%
// ---------------------------------------------------------------------
// %BANNER_END%

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.MagicLeap;

public class IpAddress {
    public string type = "ipAddress";
    public string ipAddress;
}

public class ReceivedJsonMessage {
    public string type;
}

public class Position {
    public float x;
    public float y;
    public float z;

    public Position (Vector3 position) {
        this.x = position[0];
        this.y = position[1];
        this.z = position[2];
    }

    public string SaveToString () {
        return JsonUtility.ToJson (this);
    }
}

public class Rotation {
    public float x;
    public float y;
    public float z;

    public Rotation (Quaternion rotation) {
        Vector3 eulerAngles = rotation.eulerAngles;
        this.x = eulerAngles[0];
        this.y = eulerAngles[1];
        this.z = eulerAngles[2];
    }

    public Rotation (Vector3 eulerAngles) {
        this.x = eulerAngles[0];
        this.y = eulerAngles[1];
        this.z = eulerAngles[2];
    }

    public string SaveToString () {
        return JsonUtility.ToJson (this);
    }
}

public class OffsetTransform {
    public Position position;
    public Rotation rotation;

    public OffsetTransform FromString (string offsetTransformString) {
        OffsetTransformString offsetTransformStringObject = JsonUtility.FromJson<OffsetTransformString> (offsetTransformString);
        Position position = JsonUtility.FromJson<Position> (offsetTransformStringObject.position);
        Rotation rotation = JsonUtility.FromJson<Rotation> (offsetTransformStringObject.rotation);
        return new OffsetTransform (position, rotation);
    }

    public OffsetTransform (Position position, Rotation rotation) {
        this.position = position;
        this.rotation = rotation;
    }
}

public class OffsetTransformString {

    public string position;
    public string rotation;

    public OffsetTransformString (OffsetTransform offsetTransform) {
        this.position = offsetTransform.position.SaveToString ();
        this.rotation = offsetTransform.rotation.SaveToString ();
    }

    public string SaveToString () {
        return JsonUtility.ToJson (this);
    }
}

[System.Serializable]
public class ClientReport {
    public string clientName;
    public string clientAddress;
    public float positionX;
    public float positionY;
    public float positionZ;
    public float rotationX;
    public float rotationY;
    public float rotationZ;
    public double lastTimestamp;
    public string data;
    public string type = "sync";

    public ClientReport (string clientName, string clientAddress, double timestamp, string data) {
        this.clientName = clientName;
        this.clientAddress = clientAddress;
        this.lastTimestamp = timestamp;
        this.data = data;
        this.type = "connect";
    }

    public ClientReport (
        string clientName, string clientAddress,
        float positionX, float positionY, float positionZ,
        float rotationX, float rotationY, float rotationZ,
        double timestamp, string data
    ) {
        this.clientName = clientName;
        this.clientAddress = clientAddress;
        this.positionX = positionX;
        this.positionY = positionY;
        this.positionZ = positionZ;
        this.rotationX = rotationX;
        this.rotationY = rotationY;
        this.rotationZ = rotationZ;
        this.lastTimestamp = timestamp;
        this.data = data;
    }

    public ClientReport (
        string clientName, string clientAddress,
        Transform offsetTransform,
        double timestamp, string data
    ) {
        this.clientName = clientName;
        this.clientAddress = clientAddress;
        Position position = new Position (offsetTransform.position);
        Rotation rotation = new Rotation (offsetTransform.rotation);
        this.positionX = position.x;
        this.positionY = position.y;
        this.positionZ = position.z;
        this.rotationX = rotation.x;
        this.rotationY = rotation.y;
        this.rotationZ = rotation.z;
        this.lastTimestamp = timestamp;
        this.data = data;
    }

    public string SaveToString () {
        return JsonUtility.ToJson (this);
    }
}

public class ClientState {
    public string clientName;
    public string clientAddress;
    public OffsetTransform offsetTransform;
    public double lastTimestamp;
    public string data;

    public ClientState (ClientReport clientReport) {
        this.clientName = clientReport.clientName;
        this.clientAddress = clientReport.clientAddress;
        this.offsetTransform = new OffsetTransform (
            new Position (new Vector3 (clientReport.positionX, clientReport.positionY, clientReport.positionZ)),
            new Rotation (new Vector3 (clientReport.rotationX, clientReport.rotationY, clientReport.rotationZ))
        );
        this.lastTimestamp = clientReport.lastTimestamp;
        this.data = clientReport.data;
    }

    public string SaveToString () {
        return JsonUtility.ToJson (this);
    }
}

[RequireComponent (typeof (MLImageTrackerBehavior))]
public class MultiuserManager : MonoBehaviour {
    public delegate void ReceiveAction (string message);
    public event ReceiveAction OnReceived;

    public string clientName; // should be unique; gets auto-assigned if empty
    public string clientData;

    [SerializeField]
    private string serverAddress;
    private string serverAddressUri;
    private Transform offsetTransform;
    private Dictionary<string, ClientState> connectedClients = new Dictionary<string, ClientState> ();
    private Dictionary<string, GameObject> connectedClientGameObjects = new Dictionary<string, GameObject> ();
    private bool isConnected = false;
    private ClientWebSocket clientWebSocket; // this client's instance of a websocket connection to server
    private Task connect;
    private string clientAddress;

    private MLImageTrackerBehavior _trackerBehavior = null;
    private bool _targetFound = false;
    private ViewMode _lastViewMode = ViewMode.All;

    private GameObject _axis = null;
    private GameObject _trackingCube = null;
    private GameObject _demo = null;

    private string _eventString = "";

    public enum ViewMode : int {
        All = 0,
        AxisOnly,
        TrackingCubeOnly,
        DemoOnly,
    }

    // Start is called before the first frame update
    void Start () {
        // _trackerBehavior = GetComponent<MLImageTrackerBehavior> ();
        // _trackerBehavior.OnTargetFound += OnTargetFound;
        // _trackerBehavior.OnTargetLost += OnTargetLost;
        serverAddressUri = $"ws://{serverAddress}";
        if (clientName == String.Empty) {
            this.clientName = System.Guid.NewGuid ().ToString ();
        }
        Debug.Log (clientName);
        Debug.Log ($"Connecting... to {serverAddressUri}");
        // connect to server
        connect = Connect (serverAddressUri);
    }

    void OnDestroy () {
        // _trackerBehavior.OnTargetFound -= OnTargetFound;
        // _trackerBehavior.OnTargetLost -= OnTargetLost;
        if (clientWebSocket != null)
            clientWebSocket.Dispose ();
        isConnected = false;
        Debug.Log ("WebSocket closed.");
        Debug.Log ("Quitting application; Socket closed.");
        Application.Quit ();
    }

    // Update is called once per frame
    void Update () {
        // if offset to image target is observed, send OffsetReport() to server
        if (isConnected) {
            if (this.offsetTransform == null) {
                this.offsetTransform = new GameObject ().transform;
            }
            ClientReport clientConnectionReport = new ClientReport (
                this.clientName, this.clientAddress, this.offsetTransform, (DateTime.Now.ToUniversalTime () - new DateTime (1970, 1, 1)).TotalMilliseconds, clientData
            );
            string message = AssembleWebsocketStringFromClientReport (clientConnectionReport);
            Send (message);
        }
    }

    public async Task Connect (string uri) {
        try {
            clientWebSocket = new ClientWebSocket ();
            await clientWebSocket.ConnectAsync (new Uri (uri), CancellationToken.None);
            ClientReport clientConnectionReport = new ClientReport (this.clientName, this.clientAddress, (DateTime.Now.ToUniversalTime () - new DateTime (1970, 1, 1)).TotalMilliseconds, clientData);
            string message = AssembleWebsocketStringFromClientReport (clientConnectionReport);
            Debug.Log (message);
            Send (message);
            isConnected = true;
            await Receive ();
        } catch (Exception ex) {
            Debug.Log (ex);
            Debug.Log ("Quitting application; Socket closed.");
            Application.Quit ();
        }
    }

    private string AssembleWebsocketStringFromClientReport (ClientReport clientReport) {
        return clientReport.SaveToString ();
    }

    private async Task Send (string message) {
        var encoded = Encoding.UTF8.GetBytes (message);
        var buffer = new ArraySegment<Byte> (encoded, 0, encoded.Length);
        await clientWebSocket.SendAsync (buffer, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private void HandleClientStateUpdate (string message) {
        ClientReport clientReport = JsonUtility.FromJson<ClientReport> (message);
        ClientState clientState = new ClientState (clientReport);
        connectedClients[clientState.clientName] = clientState;
        if (!connectedClientGameObjects.ContainsKey (clientState.clientName)) {
            connectedClientGameObjects[clientState.clientName] = new GameObject ();
        }
        GameObject connectedClientGameObject = connectedClientGameObjects[clientState.clientName];
        connectedClientGameObject.transform.position = new Vector3 (
            clientState.offsetTransform.position.x, clientState.offsetTransform.position.y, clientState.offsetTransform.position.z
        );
        connectedClientGameObject.transform.rotation = Quaternion.Euler (new Vector3 (
            clientState.offsetTransform.rotation.x, clientState.offsetTransform.rotation.y, clientState.offsetTransform.rotation.z
        ));
    }

    private async Task Receive () {
        ArraySegment<Byte> buffer = new ArraySegment<byte> (new Byte[8192]);
        while (clientWebSocket.State == WebSocketState.Open) {
            WebSocketReceiveResult result = null;
            using (var memoryStream = new MemoryStream ()) {
                do {
                    result = await clientWebSocket.ReceiveAsync (buffer, CancellationToken.None);
                    memoryStream.Write (buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);
                memoryStream.Seek (0, SeekOrigin.Begin);
                if (result.MessageType == WebSocketMessageType.Text) {
                    using (var reader = new StreamReader (memoryStream, Encoding.UTF8)) {
                        string message = reader.ReadToEnd ();
                        string messageType = JsonUtility.FromJson<ReceivedJsonMessage> (message).type;
                        switch (messageType) {
                            case "ipAddress":
                                this.clientAddress = JsonUtility.FromJson<IpAddress> (message).ipAddress;
                                break;
                            case "state":
                                HandleClientStateUpdate (message);
                                break;
                            default:
                                break;
                        }
                        if (OnReceived != null) {
                            OnReceived (message);
                        }
                    }
                } else if (result.MessageType == WebSocketMessageType.Close) {
                    await clientWebSocket.CloseAsync (WebSocketCloseStatus.NormalClosure, String.Empty, CancellationToken.None);
                    Debug.Log ("Quitting application; Socket closed.");
                    Application.Quit ();
                }
            }
        }
    }

    private void RefreshViewMode () {
        switch (_lastViewMode) {
            case ViewMode.All:
                _axis.SetActive (_targetFound);
                _trackingCube.SetActive (_targetFound);
                _demo.SetActive (_targetFound);
                break;
            case ViewMode.AxisOnly:
                _axis.SetActive (_targetFound);
                _trackingCube.SetActive (false);
                _demo.SetActive (false);
                break;
            case ViewMode.TrackingCubeOnly:
                _axis.SetActive (false);
                _trackingCube.SetActive (_targetFound);
                _demo.SetActive (false);
                break;
            case ViewMode.DemoOnly:
                _axis.SetActive (false);
                _trackingCube.SetActive (false);
                _demo.SetActive (_targetFound);
                break;
        }
    }

    private void OnTargetFound (bool isReliable) {
        _eventString = String.Format ("Target Found ({0})", (isReliable ? "Reliable" : "Unreliable"));
        _targetFound = true;
        RefreshViewMode ();
    }

    private void OnTargetLost () {
        _eventString = "Target Lost";
        _targetFound = false;
        RefreshViewMode ();
    }
}
