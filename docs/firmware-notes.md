# Firmware notes: Predator Helios 300 PH315-52

Everything below was verified on a real PH315-52 (BIOS V1.12, Windows 11) in November 2026, with read-only probes first and then supervised write tests (set, read back, restore).

The encodings come from [rusty-predator](https://github.com/yavcsd/rusty-predator) `docs/firmware-interface.md` (MIT), decoded from PredatorSense 3.0.3136 on a Triton 500 PT515-51. Where the PH315-52 differs, it says so.

## Classes and calling convention

Namespace `root\WMI`. Calls need administrator rights.

| Class | Arguments | Used for |
|---|---|---|
| `AcerGamingFunction` (v2.63, instance `ACPI\PNP0C14\APGe_0`) | `gmInput` / `gmOutput` | Sensors, fans, Turbo, keyboard, misc settings |
| `APGeAction` | `uiInput` / `uiOutput` | CoolBoost, USB charging when off, keyboard light timeout |
| `AcerBiosConfigurationTool` | | BIOS options. **Never used by this app.** |

- Getters take `uint32` and return `uint64`. Most setters take `uint64` (WMI passes it as a decimal string). `SetGamingRgbKb` takes `uint32`. `SetGamingKBBacklight` takes `uint8[16]`.
- Byte 0 of every output is a status: 0 OK, 1 unsupported, 2 invalid parameter (`APGeAction` uses `0xE1` / `0xE2`). The payload is `output >> 8`.

## Sensors: `GetGamingSysInfo((id << 8) | 1)`, value in bits 8..23

| id | Reading |
|---|---|
| 1 | CPU temperature °C |
| 2 | CPU fan RPM |
| 3 | System temperature °C |
| 6 | GPU fan RPM |
| 10 | GPU temperature °C |
| 12, 16, 17 | Fan duty %: **read 0 on the PH315-52** (no duty sensors) |

## Fans

- Read mode: `GetGamingFanBehavior(0x9)`: CPU mode in bits 8-9, GPU in bits 14-15. 1 Auto, 2 Max, 3 Custom.
- Set mode: `SetGamingFanBehavior(slots | mode << 16 | mode << 22)` with slots `0x9` (slot 0 = CPU, slot 3 = GPU).
  Auto = `0x410009`, Max = `0x820009`, Custom = `0xC30009`.
- Custom speed: `SetGamingFanSpeed(fanId | pct << 8)`, fan id 1 = CPU, 4 = GPU. Only applies in Custom mode. `GetGamingFanSpeed(id)` returns the value in byte 1.
- The percentage is a firmware value, not a duty cycle, and **0 % stops the fan**. Measured (both fans, settled after ~7 s):

| Firmware % | 40 | 60 | 80 | Max mode |
|---|---|---|---|---|
| RPM | ~3,450 | ~4,150 | ~5,400 | ~6,400 |

- Fan mode can be changed while Turbo is on. It does not change the Turbo flag.

## Turbo

- Read: `GetGamingLED(1)` payload bits 0-1 (1 = LED on); misc 1 = Turbo flag; misc 5 = GPU overclock level (mask `0x0F`).
- On: `SetGamingLED(0x10001)`, misc 1 = 1, fans Max, misc 5 = 2.
- Off: `SetGamingLED(0x1)`, misc 1 = 0, fans Auto, misc 5 = 0.
- Read each value first and write only when it differs: a same-value write can act like a press of the TURBO key.
- **PH315-52:** the LED write already flips misc 1, so the flag write is always skipped.
- The physical TURBO key changes these values without any Windows event. Poll them.

## Misc settings: `GetGamingMiscSetting(idx)` / `SetGamingMiscSetting(idx | value << 8)`

| idx | Meaning | PH315-52 |
|---|---|---|
| 1 | Turbo flag 0/1 | works |
| 2 | GPU MUX | reads `0xFF` (no MUX). Never written. |
| 5 | GPU overclock 0 Normal / 1 Faster / 2 Turbo | works (level 2 = Turbo mode) |
| 6 | Boot animation and sound 0/1 | works |
| 3, 4 | | status 1, unsupported |
| 0, 7..0x20 | Thermal profiles, CPU overclock, ... | status 2, invalid |

## Profile word: `GetGamingProfile(0)` = `0xFF000001000000`

- Byte 3: Windows key (1 = works). Set with `SetGamingProfile(0x02 | on << 24)`. Works.
- Byte 6 = `0xFF`: LCD overdrive not supported.

## Keyboard (4 zones)

- Zone color: `SetGamingRgbKb(zoneBit | R << 8 | G << 16 | B << 24)`, zone bits 1, 2, 4, 8. `GetGamingRgbKb(zoneBit)` reads it back. Works on all 4 zones.
- Effect: `SetGamingKBBacklight(uint8[16])` = `[effect, speed, brightness, pattern, direction, R, G, B]` twice.
  Effects 0 Static, 1 Breathing, 2 Neon, 3 Wave, 4 Shifting, 5 Zoom. Speed 1-9, brightness 0-100, pattern `0x08` for Wave and Shifting, direction 1 or 2 for Wave and Zoom, color for Breathing, Neon and Zoom. All accepted. It cannot be read back.
- After returning to Static, send the 4 zone colors again.

## `APGeAction`

| Setting | Get | Set |
|---|---|---|
| CoolBoost | `GetFunction(0x207)`, byte 1 = state | `SetFunction(7 \| on << 16)` |
| USB charging when off | `GetFunction(0x204)`: byte 1 `0x0F` on / `0x1F` off, byte 2 = battery floor % | `SetFunction(0x04 \| state << 8 \| floor << 16)`, floors 10 / 20 / 30, off sent with floor 10 |
| Keyboard light timeout | `GetFunction(0x88401)` | see below |

**Keyboard light timeout on the PH315-52 has only two states:**

- Off after 30 s: `SetFunction(0x88402 | 100 << 32 | 30 << 40)`, reads back `0x1E0000080000`.
- Never: `SetFunction(0x88402)` (brightness byte 0), reads back `0x6400000000`.
- Other values for the seconds are accepted with status 0 and ignored.

## Not available on the PH315-52

Thermal profiles, CPU overclock, battery health / charge limit (no `BatteryControl` class), LCD overdrive, fan duty sensors, `GetGamingLEDBehavior` / `GetGamingLEDColor` (status 1).

CPU undervolting is locked by the BIOS and is outside this interface.
