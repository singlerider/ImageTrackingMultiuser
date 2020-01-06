#!/usr/bin/env python3

# pip install websockets

from common import Position, Rotation, OffsetTransform, Client, ClientReport

import asyncio
import json
import logging
import socket
import websockets

import time

logging.basicConfig(level=logging.INFO)

CLIENTS = {}


def offset_report_event():
    return json.dumps({"type": "state"})


def state_event(websocket):
    client_report = ClientReport(CLIENTS[websocket])
    return json.dumps({"type": "state", **client_report.to_dict()})


def clients_event():
    return json.dumps({"type": "clients", "count": len(CLIENTS)})


def ip_address_event(websocket):
    return json.dumps({
        "type": "ipAddress", "ipAddress": websocket.remote_address[0]
    })


async def notify_state(reporting_websocket):
    if CLIENTS:  # asyncio.wait doesn't accept an empty list
        message = state_event(reporting_websocket)
        for client_websocket in CLIENTS:
            if client_websocket == reporting_websocket:
                continue
            await client_websocket.send(message)


async def notify_clients(reporting_websocket):
    if CLIENTS:  # asyncio.wait doesn't accept an empty list
        message = clients_event()
        for client_websocket in CLIENTS:
            await client_websocket.send(message)


async def update_client_from_websocket(websocket):
    async for message in websocket:
        websocket_data = json.loads(message)
        CLIENTS[websocket].name = websocket_data["clientName"]
        CLIENTS[websocket].address = websocket.remote_address[0]
        CLIENTS[websocket].offset_transform = OffsetTransform(
            Position.from_dict({
                "positionX": websocket_data["positionX"],
                "positionY": websocket_data["positionY"],
                "positionZ": websocket_data["positionZ"]
            }),
            Rotation.from_dict({
                "rotationX": websocket_data["rotationX"],
                "rotationY": websocket_data["rotationY"],
                "rotationZ": websocket_data["rotationZ"]
            })
        )
        CLIENTS[websocket].timestamp = websocket_data["lastTimestamp"]
        CLIENTS[websocket].data = websocket_data["data"] or {}
        await notify_state(websocket)


async def register(websocket, websocket_data):
    if websocket not in CLIENTS:
        try:
            timestamp = websocket_data["lastTimestamp"]
        except KeyError:
            timestamp = time.time()
        client = Client(
            websocket=websocket,
            name=websocket_data["clientName"],
            address=websocket.remote_address[0],
            timestamp=timestamp
        )
        CLIENTS[websocket] = client
        logging.info(
            f"{websocket.remote_address[0]} has registered. "
            f"(name: {client.name})"
        )
        logging.info(f"{len(CLIENTS)} clients connected.")
        await websocket.send(ip_address_event(websocket))
        await notify_clients(websocket)


async def unregister(websocket, reason):
    if websocket in CLIENTS:
        client_name = CLIENTS[websocket].name
        del CLIENTS[websocket]
        logging.info(
            f"{websocket.remote_address[0]}"
            " has unregistered "
            f"(name: {client_name})"
            f"({reason})"
        )
        logging.info(f"{len(CLIENTS)} clients connected.")
    await notify_clients(websocket)


async def ingest(websocket, path):
    try:
        async for message in websocket:
            websocket_data = json.loads(message)
            if websocket_data["type"] == "connect":
                await register(websocket, websocket_data)
            elif websocket_data["type"] == "sync":
                logging.debug(websocket_data)
                await update_client_from_websocket(websocket)
    except websockets.ConnectionClosed as error:
        await unregister(websocket, error)
    except asyncio.streams.IncompleteReadError as error:
        await unregister(websocket, error)


start_server = websockets.serve(ingest, "0.0.0.0", 6789, ping_interval=None)
asyncio.get_event_loop().run_until_complete(start_server)
logging.info(f"Server started at {socket.gethostname()}:6789")
asyncio.get_event_loop().run_forever()
