# EepyDeepy

A server-side Valheim mod that gently — then not so gently — encourages your fellow Vikings to go to bed.

When any player gets into a bed, a sequence of increasingly unhinged messages is broadcast to all players. The sequence resets when everyone successfully sleeps, or when everyone gets out of bed.

---

## Features

- Escalating message sequence triggered when a player gets into bed
- Sequence resets on successful sleep or when all players leave their beds
- Use the `/rest` emote to trigger the sequence for testing (no bed required)
- Fully configurable sequence — edit the messages and timing to your liking
- Live config reloading — changes take effect immediately without restarting the server

---

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) on your Valheim server and clients
2. Copy `EepyDeepy.dll` into `BepInEx/plugins/` on both server and clients
3. Copy `eepydeepy.cfg` into `BepInEx/config/`
4. Start the server

**This mod must be installed on both the server and all clients.**

---

## Configuration

The config file lives at `BepInEx/config/eepydeepy.cfg`.

Each line is a sequence entry in the format `<seconds> <message>`:

```
# seconds to wait before showing this message, then the message text
20  Eepy Deepy?
20  Eepy Deepy Schmeepies?
18  Eepy Deepy Schmeepy Beepy?
10  just... go... to... sleep...
8   please.
4   YOU'RE A MONSTER !!!!
```

- Lines starting with `#` are comments and are ignored
- The config file is watched for changes — edit and save while the server is running and it reloads automatically

---

## Compatibility

- Valheim `0.221.12` (network version 36)
- BepInEx `5.4.23.x`
- Required on both server and client

## Acknowlegements

soundtrack is "Lullaby Baby Sleep Music" by ikoliks_aj, from Pixabay
https://pixabay.com/music/lullabies-lullaby-baby-sleep-music-331777/

Original score is Wiegenlied (Op. 49, No. 4) by Johannes Brahms.

