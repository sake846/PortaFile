# PortaFile

**English** | [日本語](./README.ja.md)

PortaFile is a Windows WPF application for transferring files and folders between two PCs over a COM port (serial communication).
By running PortaFile on both machines, you can initiate a transfer simply by dragging and dropping files or folders onto the sender window. The tool preserves folder structures and verifies data integrity using CRC-32 checksums.

## Features

- **Easy Drag & Drop**: Send single files, multiple files, or entire directory trees by dropping them onto the window.
- **Directory Structure Preservation**: Preserves the relative path structure when transferring folders.
- **1-to-1 Serial Communication**: Secure file transfer over COM ports without needing an internet connection.
- **Reliability Mode Selection**: Toggles between high-reliability transfer with ACK/NAK based retransmission (ARQ mode) and high-speed one-way streaming without reception acknowledgments (One-Way mode).
- **Flexible Duplex Settings**: Easily toggle between Full-duplex and Half-duplex modes, with optional RTS direction control for half-duplex.
- **Dual CRC-32 Validation**: Data integrity verified at both the packet level and the overall file level.
- **Robust Flow Control**: Implements ACK/NAK based automatic packet retransmission in ARQ mode.
- **Bidirectional Cancellation**: Safely cancel an active transfer from either the sender or receiver side at any time.
- **Smart Conflict Resolution**: Auto-renames duplicate filenames at the destination instead of overwriting existing files.
- **Defrag-style Progress View**: Visually tracks block-by-block progress, inspired by the classic Windows defragmentation display.

## System Requirements

- **OS**: Windows 10 / 11 64-bit
- **Framework/SDK**: .NET 10 SDK
- **Hardware**: Serial communication link (e.g., cross/null-modem cable) connecting the two PCs.

## Usage

1. Start PortaFile on both PCs.
2. Select the appropriate **COM port** and **serial configurations** on each machine.
3. Choose the **reliability mode** (ARQ or One-Way).
4. Choose the **communication mode**:
   - Full-duplex
   - Half-duplex
   - Half-duplex + RTS Control
5. Drag and drop files or folders onto the sender's window.
6. Transferred files will be saved inside the `Downloads` directory, created in the same folder as the executable.

*Note: Incoming files are temporarily saved with a `.part` extension and renamed to their actual name once CRC-32 verification succeeds.*

## Build

### Build the Project
```powershell
dotnet build
```
This is a WPF application targeting `net10.0-windows`.

## Serial Configuration

PortaFile supports the following serial configuration options:

- **COM Port Selection**
- **Parity Bit Configuration**
- **Reliability Mode**: ARQ (Reliable with ACK/NAK) / One-Way (One-way stream)
- **Duplex Mode**: Full-duplex / Half-duplex / Half-duplex with RTS direction control
- **Baud Rate Candidates**:
  - 115200 / 230400 / 460800 / 921600 / 1000000 / 2000000 / 3000000

## Protocol Overview

PortaFile uses a custom packet-based protocol to manage file transfer states and send data chunks.

### Packet Types
`HELLO` / `SEND_REQUEST` / `READY` / `BUSY` / `MANIFEST` / `FILE_START` / `DATA` / `ACK` / `NAK` / `FILE_END` / `FILE_OK` / `FILE_ERROR` / `TRANSFER_END` / `CANCEL` / `ERROR` / `DATA_BATCH_CHECK` / `DATA_BATCH_ACK`

### Transmission Details
Files are split and sent in **10 KiB** data packets. Each packet carries a CRC-32 checksum. If the receiver encounters a CRC mismatch, timeout, or an unexpected packet sequence number, an automatic retransmission is triggered (only in ARQ mode) using ACK/NAK signaling.

## Project Structure

```text
PortaFile/
├── Models/        # Selection and display models
├── Protocol/      # Packet definition, encoding/decoding, and CRC-32 math
├── Services/      # Serial port configurations, connection handling, and dialog integrations
├── Transfer/      # Manifest creation, path resolution, progress tracking, and transfer engine
├── ViewModels/    # MVVM ViewModels, commands, and data bindings
├── Views/         # WPF Window layouts (MainWindow, etc.)
└── docs/          # Functional requirements document (requirements.md)
```

## Technical Details

- **Tech Stack**: C# / WPF (.NET 10)
- **Serial Connection**: Built natively on top of `System.IO.Ports`.

## License

This project is licensed under the [MIT License](./LICENSE).
