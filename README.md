# NetworkPositionSync

Network Transform using Snapshot Interpolation and other techniques to best sync position and rotation over the network. 

## Setup

1) Download the UnityPackage or source code from [Release](https://github.com/JamesFrowen/NetworkPositionSync/releases) page.
2) Import code into your project
3) Create Packer settings Assets: Create > PositionSync > Packer
4) Add `SyncPositionSystem` to your NetworkManager call RegisterHandlers from Server and Client start methods
5) Add `SyncPositionBehaviour` to your GameObjects
6) Assign Packer created in step 3 to both `SyncPositionSystem` and `SyncPositionBehaviour`
7) Configure Packer with your world settings
    - if `SyncPositionSystem` is in the same scene then a gimzo can be enabled to show world bounds.

## Bugs?

Please report any bugs or issues [Here](https://github.com/JamesFrowen/NetworkPositionSync/issues)


# Goals

- Easy to use 
- Smoothly sync movement 
- Low bandwidth
- Low latency
- Low Cpu usage
