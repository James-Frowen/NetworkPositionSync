# NetworkPositionSync

Network Transform using Snapshot Interpolation and other techniques to best sync position and rotation over the network. 

## Download

### Package Manager

use package manager to get versions easily, or replace `#v1.0.3` with the tag, branch or full hash of the commit.

IMPORTANT: update `v4.1.0` with latest version on release page
```json
"com.james-frowen.position-sync": "https://github.com/James-Frowen/NetworkPositionSync.git?path=/Assets/NetworkPositionSync#v4.1.0",
```

### Unity package

Download the UnityPackage or source code from [Release](https://github.com/James-Frowen/NetworkPositionSync/releases) page.

## Setup

1) Add `SyncPositionSystem` to your NetworkManager or same GameObject as `ServerObjectManager` and `ClientObjectManager`
2) Add `SyncPositionBehaviour` to your GameObjects
3) Check inspector settings to make sure they make sense for your setup

## Bugs?

Please report any bugs or issues [Here](https://github.com/JamesFrowen/NetworkPositionSync/issues)


# Goals

- Easy to use 
- Smoothly sync movement 
- Low bandwidth
- Low latency
- Low Cpu usage
