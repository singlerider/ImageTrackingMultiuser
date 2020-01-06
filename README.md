# Magic Leap Unity Project Template

## Project

Multiuser Image Tracking

## Versions

### Unity

2019.3.x

### MLSDK

v0.23.0

### LuminOS

0.98.x

## Instructions After Downloading

1) Using Unity Hub, download Unity 2019.3.x and make sure Lumin support is checked during installation.

2) `ADD` the project using Unity Hub.

3) Open the project using Unity Hub.

4) Under File > Build Settings, make sure the build target is Lumin.

5) Under Unity preferences, set the MLSDK path.

6) Under project settings > publishing settings, set your cert path (and make sure the privkey file is in the same directory. If this is confusing, refer to and read our docs. There’s also a `README` in the privkey folder after unzipping).

7) Make sure USB debugging is enabled between your device and computer (which requires MLDB access) and you’re allowing untrusted sources.

8) Open the `ImageTrackingMultiuser` Scene from `Assets`>`Scenes`>`ImageTrackingMultiuser`.

9) From the `Assets`>`Scripts` directory, in a terminal shell, do the following:
- `pip install websockets` for the Python server (requires Python 3)
- `python websocket_server.py` to start the websocket server
- Note the machine's IP address to connect to later
- Optional - start up any number of test clients with `python websocket_test_client.py`

10) Start the Unity project, either from the simulator or a device. Make sure you set the previously saved IP address of the server in the `ImageTrackingExample` object in the `ImageTrackingMultiuser` scene. **LEAVE Client Name and Client Data blank**.

11) Observe in the logs that data is going from each client to each other client via the server.
