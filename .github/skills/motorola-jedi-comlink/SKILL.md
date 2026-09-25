---
name: motorola-jedi-comlink
description: "Use when implementing, debugging, or reviewing Motorola Jedi-series radio programming, especially MTS2000 codeplug parsing, serialization, SB9600/SBEP serial communication, EEPROM access, Toolproof authentication, radio emulation, or recovery workflows."
---

# Motorola JediComlink

Use this skill for Motorola Jedi-series two-way radio programming, with emphasis on the MTS2000 family and the technical reference at `lf73/JediComlink`.

## Scope

This skill covers:

- MTS2000 codeplug binary parsing and serialization
- Motorola SB9600 control-bus communication
- SBEP extended memory operations
- Internal and external EEPROM layout
- Codeplug block trees and vector references
- Toolproof authentication and auth-code recalculation
- Radio emulation and fixture-based protocol testing
- Safe read, edit, validate, backup, and write workflows

Do not copy source code from the reference repository. Use its documented protocol and format facts as a reference, preserve independent implementation structure, and check repository licensing before reusing any implementation detail beyond protocol behavior.

## Architecture

Keep the system separated into these layers:

1. Serial transport
   - COM-port configuration and byte I/O
   - DTR/RTS handling
   - echo, timeout, retry, and buffer management
2. SB9600 control bus
   - fixed five-byte packets
   - programming-mode entry
   - SBEP-mode entry and exit
   - firmware-version query and reset
3. SBEP memory protocol
   - variable-length framed requests and replies
   - EEPROM read opcode `0x11`
   - EEPROM write opcode `0x17`
   - echo and acknowledgement handling
4. Codeplug image
   - raw internal and external EEPROM bytes
   - block parsing and serialization
   - vector resolution and block checksums
5. Domain model and UI
   - radio identity and settings
   - zones, channels, scan lists, signaling, and display data
   - validation, backup, and user confirmation

The parser/serializer must not depend on WPF, dialogs, or a live serial port. The transport must not know the meaning of channel or radio-setting fields.

## Known codeplug structure

The codeplug is an approximately 33 KB binary image with total size `0x8200` bytes when internal and external EEPROM are represented together.

- Internal EEPROM occupies the beginning of the image.
- The internal root is Block `0x01`.
- The external root is Block `0x30`.
- Block `0x01` contains the external-codeplug vector.
- The external vector is commonly `0x0200` for 800 MHz radios and `0x0280` for other bands, but it must be read from the image rather than assumed.
- Internal codeplug data can overflow from internal EEPROM into external EEPROM.
- Blocks are variable-length records identified by a one-byte block ID, followed by a length/payload structure and an eight-bit checksum.
- Most relationships are represented by two-byte vector references.
- Block payload offsets are relative to the payload, not absolute EEPROM addresses.

Do not hard-code a single model or band layout. Preserve unknown bytes and unknown blocks during a decode/encode round trip whenever possible.

## Important blocks

At minimum, treat these as distinct areas in the model:

- Block `0x01`: internal radio block, including identity, serial, model, feature references, and Toolproof auth data.
- Block `0x02`: hardware configuration radio block; some VHF variants have a different length.
- Block `0x10`: feature descriptor block and feature/flashcode-related data.
- Block `0x30`: external codeplug root.
- Additional external blocks: channels, scan lists, signaling, display strings, and model-specific feature data.

Do not label undecoded bytes as known fields. Represent them as reserved or unknown data and keep them available for lossless serialization.

## Toolproof authentication

Toolproof is a radio safety boundary, not an optional enhancement.

The codeplug auth code is tied to radio-specific data, including the model string, factory code, firmware signature bytes, and feature/flashcode information. The auth code is stored in the internal radio block; the reference documents a ten-byte field at payload offsets `0x30-0x39` for Block `0x01`.

The factory-code validation also compares EEPROM tail bytes at `0x81F8-0x81FF` with the corresponding flash region at `0x3FFF8-0x3FFFF`.

Rules:

- Never write an edited codeplug without validating or deliberately recalculating its auth code.
- Never silently replace an unknown auth input with zeros.
- If auth inputs are missing, fail closed and offer raw backup/export instead.
- Test auth calculations against known-good fixtures before connecting them to a live write path.
- A failed Toolproof check can produce a radio fault such as `Fail 01/93`; treat this as a potentially unrecoverable programming error.

## Communication workflow

The normal control flow is:

1. Open the selected COM port at the radio-appropriate serial settings.
2. Enter programming mode over SB9600.
3. Query firmware/version information to verify the link.
4. Enter SBEP extended mode.
5. Read or write EEPROM in protocol-sized chunks.
6. Exit SBEP mode cleanly.
7. Reset the radio only when the operation and user confirmation require it.

The reference identifies these SB9600 messages:

- Enter SBEP: `00 12 01 06`
- Enter programming mode: `01 02 00 40`
- Reset: `00 00 01 08`
- Firmware-version request: `08 00 00 C0` with a reply expected

The exact wire framing and checksum must remain centralized in protocol classes. Do not duplicate frame construction in view models or codeplug classes.

## SBEP memory operations

The reference identifies:

- `0x11`: read EEPROM/memory
- `0x17`: write EEPROM/memory
- `0x10`: exit SBEP mode

Validate every operation:

- address is within `0x0000-0xFFFF`
- requested count is valid for one frame
- reply status is successful
- echoed address matches the request
- returned byte count matches the request
- write acknowledgement is received before advancing

Use bounded retries with clear status events. Never retry a write indefinitely or after an ambiguous acknowledgement without first determining whether the radio accepted the previous frame.

## Read and write pipeline

### Read

1. Read the complete required EEPROM image or the exact model-specific regions.
2. Preserve the original bytes as a backup.
3. Validate block boundaries, lengths, checksums, vectors, and required roots.
4. Decode known blocks into the domain model.
5. Preserve unknown fields and blocks.
6. Report warnings separately from fatal parse errors.

### Write

1. Start from a validated original image whenever possible.
2. Apply only supported model changes.
3. Recalculate affected block checksums.
4. Recalculate Toolproof auth data from verified inputs.
5. Validate the complete encoded image again.
6. Require an explicit user confirmation with the radio identity and backup path.
7. Write in bounded chunks while reporting progress.
8. Read back and compare the affected regions when the radio protocol permits it.
9. Exit programming mode and reset only after all checks pass.

Never implement write support as `Encode(model)` followed by an unconditional full-image write until unknown-byte preservation, checksums, and auth validation are proven.

## Testing strategy

Prefer a radio emulator or deterministic fake transport over a live radio for normal tests.

Required test categories:

- SB9600 frame encoding/decoding and checksum behavior
- SBEP frame encoding/decoding, echo handling, acknowledgement, retry, and timeout behavior
- EEPROM address boundaries and maximum chunk sizes
- Block length and checksum validation
- Vector resolution across internal/external EEPROM
- Decode/encode round trips that preserve unknown bytes
- Known-good codeplug fixtures for each supported model/band
- Auth-code calculation against independent expected vectors
- Rejection of malformed, truncated, mismatched, or unauthenticated images
- Read-back verification after writes using an emulator

A test that only checks a helper formula is insufficient. Assert the actual bytes and values consumed by the production parser or transport path.

## Safety boundaries

Treat the following as high-risk operations:

- EEPROM writes
- firmware flashing
- bootstrap uploads
- recovery or unbrick procedures
- writes involving unknown auth or factory-code inputs

Keep firmware flashing and bootstrap/recovery outside the normal codeplug workflow. They require separate explicit commands, backups, model checks, and operator confirmation. Do not expose them through a generic `WriteCodeplug` operation.

Before live hardware testing:

- save a complete raw EEPROM backup
- record radio model, band, serial, firmware, and factory-code metadata
- verify the cable and COM port
- test the exact image in an emulator or fixture
- begin with read-only operations
- use a known-good recovery path

## Review checklist

When reviewing a Jedi/MTS2000 implementation, verify:

- protocol framing is isolated from domain parsing
- internal/external vector handling is data-driven
- unknown blocks and bytes survive round trips
- checksums are validated on read and recomputed on write
- Toolproof inputs and auth failures are explicit
- reads are cancellable and writes are not falsely reported as complete
- retries cannot duplicate an ambiguous write silently
- raw backups are produced before destructive operations
- UI commands do not bypass service-layer validation
- tests exercise real encoded bytes and emulator behavior
