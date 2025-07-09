# CrossMgr RFID Interface

A C# Windows Forms application that listens for TCP messages from RFID readers used with CrossMgr race timing software.

## Features

- **TCP Server**: Listens on a configurable port for incoming RFID reader connections
- **Protocol Support**: Handles CrossMgr RFID protocol messages (GT, S0000, DA)
- **Real-time Display**: Shows formatted tag reads in a scrollable listbox
- **Multiple Connections**: Supports multiple simultaneous RFID reader connections
- **Time Synchronization**: Responds to GetTime (GT) requests with current system time
- **User-friendly UI**: Clean interface with connection status and controls

## Supported Message Types

### GetTime (GT)

Request from RFID reader for current time synchronization.

- **Request**: `GT`
- **Response**: `GT{HHmmssfff} date={YYYYMMDD}`

### Setup (S0000)

Setup command sent before tag reads begin.

- **Format**: `S0000`

### Data/Tag Reads (DA)

RFID tag detection messages.

- **Format**: `DA{tagID} {time} 10 {count} C7 date={date}`
- **Example**: `DA10000001 17:50:37.786398 10 00006 C7 date=20250709`

## Usage

1. **Start the Application**: Run the CrossMgrInterface.exe
2. **Configure Port**: Set the TCP port (default: 53135)
3. **Start Server**: Click "Start" to begin listening for connections
4. **Monitor Messages**: View incoming tag reads in the message list
5. **Stop Server**: Click "Stop" to stop the TCP server

## Building

This project requires:

- .NET 6.0 or later
- Windows Forms support

```bash
dotnet build
dotnet run
```

## Protocol Details

Based on the CrossMgr RFID implementation, this interface handles the standard timing protocol used by Impinj and similar RFID readers. The application automatically responds to time synchronization requests and logs all tag read events with timestamps.

## License

This project is provided as-is for CrossMgr timing system integration.
