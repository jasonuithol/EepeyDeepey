# Changelog

## 1.0.3

- Bundled `lullaby.ogg` in the package. Earlier releases shipped without it, so the lullaby never played for anyone who installed via Thunderstore — only the chat sequence was audible.
- Fixed bed-enter/exit detection on dedicated servers — the previous patches on `Bed.Interact` and `Bed.SetOwner` never fired in practice, so getting into a bed produced no sequence and no lullaby. Detection now runs from `Player.AttachStart` / `Player.AttachStop` on the client and notifies the server via the existing RPC.
- Fixed a stuck `/rest` pose in single-player and host games. The `StopEmote` patch was throwing in single-player (no remote server peer), preventing the original from running and clearing the emote. Patches now self-dispatch when the local player is the server.
- Fixed lullaby silence on hosts. Jötunn's `CustomRPC.SendPackage(Everybody, …)` does not self-deliver to the host, so the host now triggers `StartMusic` / fade-out locally alongside the broadcast.
- Fixed multiplayer bed tracking. The previous global `inBed` flag clamped concurrent sleepers to 1 and reset the sequence the moment any player left a bed; now players are tracked individually in a `HashSet<long>` keyed on network UID so the sequence only resets when the last player leaves.

## 1.0.2

- Migrated client→server messaging to Jotunn's CustomRPC for cleaner registration and serialization.
