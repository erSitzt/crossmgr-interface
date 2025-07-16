# Copilot Instructions for CrossMgr RFID Interface

<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

## Project Overview

This is a C# Windows Forms application that serves as an interface for CrossMgr RFID systems. The application listens on a TCP port for messages from RFID readers (such as Impinj readers) and displays them in a formatted way.

## RFID Protocol

The application handles several message types:

1. **GT (GetTime)**: Request for current time. Responds with `GT{HHmmssfff} date={YYYYMMDD}`
2. **S0000**: Setup command - typically sent before tag reads begin
3. **DA (Data)**: Tag read messages in format: `DA{tagID} {time} 10 {count} C7 date={date}`

## Code Guidelines

- Use proper async/await patterns for network operations
- Handle client connections and disconnections gracefully
- Format tag reads in a user-friendly way in the listbox
- Use thread-safe operations when updating UI from background threads
- Include proper error handling for network operations
- Keep the UI responsive during TCP operations

## Message Format Examples

```
GT0175013116038 date=20250709
S0000
DA10000001 17:50:37.786398 10  00006      C7 date=20250709
DA11240EC8F5F402593D42762DC18C8524 17:49:03.824413 10  00002      C7 date=20250709
```

## UI Components

- ListBox for displaying messages with timestamps
- TCP port configuration
- Start/Stop server buttons
- Connection counter
- Clear messages button
- Status indicator

## Copilot Usage

- Do not revert the last change made by Copilot if premium requests limit is reached.
