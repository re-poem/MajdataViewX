MajProtocol
===

MajProtocol is a binary protocol for communicating playback state between a Simai player and an editor.

This solution is split into:

- MajProtocol.Standard: This stores the packet types and property constants.
- MajProtocol.Server: An example implementation of a server that serves MajProtocol packets over WebSocket.
- MajProtocol.Client: An example implementation of a client that serves MajProtocol packets over WebSocket.

Format
---

The format is dictated by the order of fields in the packet structs defined in MajProtocol.Standard.

Fields are written from top-down, sequentially into the packet.

For unmanaged types, they're written to the packet as-is.

For strings, the string length is written first as a 32-bit integer, then the string is written in UTF-8 encoding.

Other managed types are not supported.

Establishing a Connection
---

This project assumes there is **only one connection** established by the server.

The client is an editor or any program that is able to control the playback status of the server (E.g. MajdataEdit).

The server is a player that receives control packets from the client and plays back the data for viewing (E.g.
MajdataView or AstroDX).

After the connection is made through your preferred communication method, the client **MUST** send a `HandshakeRequest`
packet.

The `protocolVersion` delivered through this packet must match that on the server.

The server **MUST** respond with a `HandshakeResponse`, where `accepted` is `true` if the `protocolVersion` of the
client matches that on the server, otherwise a `false` is sent, and the server **MUST** terminate the connection.

Sending and Receiving Data
---

The client **SHOULD** send a `LoadRequest` to the server to load basic information regarding the song.
If any of the fields can't be supplied, fill the fields with `string.Empty`.

The server **MUST** respond with a `LoadResponse` where `accepted` is `true` if the files are loaded
successfully.

The client **MUST** send a `ParseRequest` to the server to load the chart before playback. The content of this request
is the full body of a single difficulty (i.e. content of an inote property in the maidata.txt file).

The server **MUST** respond with a `ParseResponse` where `accepted` is `true` if the chart has been properly loaded.

Playback
---

When the client wants to start playback, it **MUST** send a `PlayRequest` to the server, containing the playback
position.

The server **MUST** respond with a `PlayResponse` where `accepted` is `true` if playback is able to begin.

When the client wants to pause playback , it **MUST** send a `PauseRequest` to the server.

The server **MUST** respond with a `PauseResponse` where `accepted` is `true` if playback is able to pause.

If the client wants to unload the chart and relevant data provided through the `LoadRequest` and the `ParseRequest`, it
**SHOULD** send an `UnloadRequest`.

The server **MUST** respond with an `UnloadResponse`, where `accepted` is true if the server can successfully unload.

Closing the Connection
---

Connection termination **SHOULD** begin with the client. Termination should be handled by the framework of the client,
for example a WebSocket.

The server **SHOULD** dispose data served to it during the connection, for example, chart data. This may be similar to
an `UnloadRequest`.
