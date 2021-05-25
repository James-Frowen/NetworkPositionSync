# Dynamically adjusting client delay

Notes from: https://www.twitch.tv/videos/867486481

Goals: 
- How to deal with client time drifting
- Dynamically adjust interpolation time

## How to deal with client time drifting

0:00 -> 50:00


#### Step by step thought process 

- each frame add deltaTime to clientTime

Exponential moving average
- Keep track of maxTime received from server
- get diff between server and client time
- add diff to EMA
- check diffWanted vs diff

**Attempt 1**
- if ahead, remove a bit
- if behind, add a bit
*Problem: oscillates back and for*

**Attempt 2**
- add threshold for moving
    - eg if within 75% of diffWanted, do nothing
- if client ahead threshold needs to be careful because if too much will run out of snapshots
- same as attempt 1 expect only move if outside threshold
- dont go below diffWanted
*Problem: can have micro-stuttering because not considering framerate, just adding constant value to clientTime*

**Attempt 3**
- change timescale instead of adding to clientime
- each frame add deltatime*timescale


#### Summary

```
OnMessage:
    diff = maxServerTime - clientTime
    diffWanted = avgDiff - goalOffset

    if diffWanted > +threshold
        add to clientTimeScale
    else if diffWanted < -threshold
        subtract from clientTimeScale
    else 
        set clientTimeScale = 1

OnUpdate:
    clientTime += deltaTime * clientTimeScale
```


## Dynamically adjust interpolation time

https://www.twitch.tv/videos/867486481?t=0h51m43s

if lower jitter you can lower snapshot delay

