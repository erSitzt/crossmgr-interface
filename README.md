# CrossMgr RFID Interface

A C# Windows Forms application that serves as an interface for CrossMgr RFID race timing systems. The application listens for messages from RFID readers (such as Impinj readers) and provides real-time race management with lap tracking, predictions, and statistics.

## Features

### Core RFID Protocol Support

- **TCP Server**: Listens on configurable port for RFID reader connections
- **CrossMgr Protocol**: Full support for GT (GetTime), S0000 (Setup), and DA (Data) messages
- **Multiple Readers**: Handles multiple concurrent RFID reader connections
- **Real-time Processing**: Live tag read processing with timestamps

### Race Management

- **Race Duration**: Configurable race duration (default: 20 minutes, range: 1-180 minutes)
- **Automatic Start**: Race automatically starts on first tag read
- **End Time Tracking**: Displays race end time and remaining time
- **Race Warnings**: Automatic warnings at 5 minutes and 1 minute remaining
- **Race Reset**: Clear all data and reset race state
- **Tag Prefix Filter**: Filter tags by prefix to only process expected rider tags

### Lap Tracking & Predictions

- **Individual Lap Tracking**: Each rider's lap times, best lap, average lap
- **Lap Prediction**: Weighted average of recent laps predicts next lap time
- **Next Crossing Prediction**: Estimated time for each rider's next finish line crossing
- **Total Lap Prediction**: Predicts total laps for the race leader based on remaining time
- **Overdue Detection**: Highlights riders who are overdue for their predicted crossing

### User Interface

#### Live Feed Tab

- Real-time message display with timestamps
- Protocol handshake logging
- Tag read notifications with lap information
- Connection status and client management

#### Riders Tab (Leaderboard)

- **Real-time Leaderboard**: Sorted by laps completed, then by total time
- **Position Indicators**: Color-coded positions (Gold/Silver/Bronze for top 3)
- **Comprehensive Data**: Position, Tag ID, Laps, Last/Best/Average lap times
- **Predictions**: Predicted lap time, next crossing time, countdown to next crossing
- **Gap Information**: Time/lap gap to leader
- **Visual Indicators**: Red highlighting for overdue riders

#### Race Statistics Tab

- **Race Time**: Current elapsed race time
- **Participation**: Total riders and total laps completed
- **Last Activity**: Most recent tag read with time since
- **Next Expected**: Which rider is expected to cross next and when
- **Race End**: Scheduled race end time
- **Time Remaining**: Countdown to race end with color coding
- **Predicted Laps**: Estimated total laps the leader will complete

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

### Starting the Server

1. Set the desired TCP port (default: 53135)
2. Click "Start" to begin listening for RFID readers
3. Readers will connect and complete the CrossMgr handshake automatically

### Setting Race Duration

1. Adjust the "Race Duration (min)" value (1-180 minutes)
2. Click "Set" to apply the new duration
3. Duration can be changed even during an active race

### Setting Tag Prefix Filter

1. Enter tag prefixes in the "Tag Filter" field (e.g., "RIDER" or "RIDER,BIKE,1000")
2. Multiple prefixes can be separated by commas
3. Check "Filter Enabled" to activate filtering
4. Click "Set Filter" to apply
5. Only tags starting with the specified prefixes will be processed for lap tracking
6. Filtered tags are logged but don't affect race statistics

### Race Operation

1. **Race Start**: Automatically starts on first tag read
2. **Live Monitoring**: Switch to "Riders" tab for real-time leaderboard
3. **Statistics**: Use "Race Statistics" tab for race overview and predictions
4. **Reset**: Use "Clear Riders" to reset all data and start a new race

## Prediction Algorithms

### Lap Time Prediction

- Uses weighted average of rider's last 3 laps
- More recent laps have higher weight
- Fallback to race-time-based estimation for new riders

### Total Race Laps Prediction

- Based on current leader's performance
- Considers remaining time and predicted lap times
- Accounts for partial laps and crossing timing
- Updates in real-time as race progresses

### Next Crossing Prediction

- Predicts when each rider will next cross the finish line
- Based on last crossing time + predicted lap time
- Provides countdown timers and overdue detection

## Configuration

### Default Settings

- **Port**: 53135 (standard CrossMgr port)
- **Race Duration**: 20 minutes
- **Update Interval**: 1 second
- **Prediction Window**: Last 3 laps for lap time prediction
- **Warning Times**: 5 minutes and 1 minute before race end

### Customizable Parameters

- TCP port (1-65535)
- Race duration (1-180 minutes)
- All timing predictions update dynamically

## Requirements

- .NET 6.0 or later
- Windows operating system
- TCP network connectivity to RFID readers
- Compatible RFID readers (Impinj or CrossMgr protocol)

## Building

This project requires:

- .NET 6.0 or later
- Windows Forms support

```bash
dotnet build
dotnet run
```

## Error Handling

The application includes comprehensive error handling for:

- Network connection issues
- Invalid message formats
- Client disconnections
- Threading synchronization
- UI update failures

All errors are logged to the Live Feed with timestamps for debugging.

## Protocol Details

Based on the CrossMgr RFID implementation, this interface handles the standard timing protocol used by Impinj and similar RFID readers. The application automatically responds to time synchronization requests and logs all tag read events with timestamps.

## License

This project is provided as-is for CrossMgr timing system integration.
