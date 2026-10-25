# NetworkPositionSync

Network Transform using Snapshot Interpolation and other techniques to best sync position and rotation over the network. 

## Download

### Package Manager

use package manager to get versions easily, or replace `#v6.0.0` with the tag, branch or full hash of the commit.


IMPORTANT: update `v6.0.0` with latest version on release page
```json
"com.james-frowen.position-sync": "https://github.com/James-Frowen/NetworkPositionSync.git?path=/Assets/NetworkPositionSync#v6.0.0",
```

### Unity package

Download the UnityPackage or source code from [Release](https://github.com/James-Frowen/NetworkPositionSync/releases) page.

## Requirements

- [Mirage](https://github.com/MirageNet/Mirage) 159.1.1 or newer
- Unity 2021.3 or newer

## Setup

### Quick start

Add `NetworkTransform3D` (or `NetworkTransform2D`) to your networked GameObjects. That's it.

Without a `SyncPositionSystem` each behaviour syncs itself using unreliable RPCs (standalone mode). This can be used to replace Mirage's legacy `NetworkTransform`:

| Legacy `NetworkTransform` | `NetworkTransform3D` |
|---|---|
| `ClientAuthority` | `ClientAuthority` (also in inspector) |
| `LocalPositionSensitivity` / `LocalRotationSensitivity` | Position / Rotation Sensitivity |
| uses local position/rotation | `Coordinates`: World, Local or Relative to another transform |
| `NetworkTransformChild` | add `NetworkTransform3D` to the child object |
| syncs scale | scale is not synced |

### Batched (recommended for many objects)

1) Add `SyncPositionSystem` to your NetworkManager or same GameObject as `NetworkServer` and `NetworkClient`
2) Add `NetworkTransform3D` or `NetworkTransform2D` to your GameObjects
3) Check inspector settings to make sure they make sense for your setup

When a `SyncPositionSystem` is set up, it syncs all the behaviours in a few batched messages instead of 1 RPC per object, and all objects share 1 interpolation clock. The system must be set up on both server and clients.

### Settings

- **Coordinates**: World, Local, or Relative (position and rotation in another transform's space, eg on a moving ship)
- **Client Authority**: owner client controls the object, server checks the sender owns it
- **Pack Settings**: optional `PackSettings3D`/`PackSettings2D` asset to control compression precision
- **Standalone Sync Interval / Interpolation Delay**: only used when there is no `SyncPositionSystem`

## Bugs?

Please report any bugs or issues [Here](https://github.com/JamesFrowen/NetworkPositionSync/issues)


# Goals

- Easy to use 
- Smoothly sync movement 
- Low bandwidth
- Low latency
- Low Cpu usage
