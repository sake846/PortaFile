# PortaFile

PortaFile is a Windows WPF application for transferring files and folders
between two PCs over a COM port.

Both PCs run PortaFile. The sender drops files or folders onto the application
window, and PortaFile transfers them to the peer while preserving folder
structure and validating data with CRC-32.

## Features

- Send files, multiple files, or folders by drag and drop
- Preserve relative paths when transferring folders
- 1-to-1 serial communication over a COM port
- Full-duplex and half-duplex modes
- Optional RTS direction control for half-duplex communication
- Packet-level CRC-32 and file-level CRC-32 validation
- ACK/NAK based retransmission
- Transfer cancellation from either side
- Duplicate destination filenames are renamed instead of overwritten
- Progress view inspired by the Windows defragmentation block display

## Requirements

- Windows 11 64-bit
- .NET 10 SDK
- A serial connection between the two PCs

## Build

```powershell
dotnet build
```

The project targets `net10.0-windows` and uses WPF.

## Usage

1. Start PortaFile on both PCs.
2. Select the COM port and serial settings on each side.
3. Choose the communication mode:
   - Full-duplex
   - Half-duplex
   - Half-duplex with RTS control
4. Drop one or more files, or a folder, onto the sender window.
5. Received files are saved under a `Downloads` folder next to the executable.

During reception, files are written as `.part` files first. After CRC-32
verification succeeds, they are renamed to their final filenames.

## Serial Settings

PortaFile supports these configuration items:

- COM port
- Baud rate
- Parity
- Full-duplex or half-duplex mode
- Half-duplex direction control

Supported baud rate candidates:

- 9600
- 19200
- 38400
- 57600
- 115200
- 230400
- 460800
- 921600
- 1000000
- 2000000
- 3000000

## Project Structure

```text
PortaFile/
├─ Models/
├─ Protocol/
├─ Services/
├─ Transfer/
├─ ViewModels/
├─ Views/
└─ docs/
```

- `Views/`: WPF views. `MainWindow.xaml.cs` only creates the ViewModel and bridges view-specific events such as drag and drop
- `ViewModels/`: screen state, commands, and binding logic
- `Models/`: display and selection models used by ViewModels
- `Protocol/`: packet types, packet encoding, and CRC-32 support
- `Services/`: serial port settings, transport layer, and Windows dialog integration
- `Transfer/`: manifest creation, path resolution, progress, and transfer engine
- `docs/requirements.md`: functional requirements

## Protocol Overview

PortaFile exchanges typed packets such as:

- `HELLO`
- `SEND_REQUEST`
- `READY`
- `BUSY`
- `MANIFEST`
- `FILE_START`
- `DATA`
- `ACK`
- `NAK`
- `FILE_END`
- `FILE_OK`
- `FILE_ERROR`
- `TRANSFER_END`
- `CANCEL`
- `ERROR`

Data packets use 10 KiB blocks. Retransmission is triggered by CRC mismatch,
timeout, or unexpected sequence numbers.
