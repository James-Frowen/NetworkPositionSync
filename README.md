# NetworkPositionSync

Network Transform using Snapshot Interpolation and other techniques to best sync position and rotation over the network. 

## Setup

1) Download the UnityPackage or source code from [Release](https://github.com/JamesFrowen/NetworkPositionSync/releases) page.
2) Import code into your project
3) Add `SyncPositionSystem` to your NetworkManager or same GameObject as `ServerObjectManager` and `ClientObjectManager`
4) Add `SyncPositionBehaviour` to your GameObjects
5) Check inspector settings to make sure they make sense for your setup


## Bugs?

Please report any bugs or issues [Here](https://github.com/JamesFrowen/NetworkPositionSync/issues)


# Goals

- Easy to use 
- Smoothly sync movement 
- Low bandwidth
- Low latency
- Low Cpu usage
