# [8.0.0](https://github.com/James-Frowen/NetworkPositionSync/compare/v7.0.2...v8.0.0) (2024-04-01)


* feat!: changing SyncMode to just have SendToAll and SendToObservers ([b9eedeb](https://github.com/James-Frowen/NetworkPositionSync/commit/b9eedeb7efab204967c743105404f51b2acf2a48))


### BREAKING CHANGES

* SyncMode now only has two modes. SendToDirtyObservers_PackOnce mode renamed to SendToObservers and index changed from 4 to 2, Validate should auto convert from 4 to 2

## [7.0.2](https://github.com/James-Frowen/NetworkPositionSync/compare/v7.0.1...v7.0.2) (2024-02-23)


### Bug Fixes

* stopping warnings from use of Obsolete InterpolationTimeField ([051e618](https://github.com/James-Frowen/NetworkPositionSync/commit/051e618f5f6ae81976c30a7be293fb540561e7e6))

## [7.0.1](https://github.com/James-Frowen/NetworkPositionSync/compare/v7.0.0...v7.0.1) (2024-02-19)


### Bug Fixes

* fixing time packing returning negative values ([3ad84f2](https://github.com/James-Frowen/NetworkPositionSync/commit/3ad84f29d2cb2323bbe7a6bb0dd68db0c3a98b0f))

# [7.0.0](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.5...v7.0.0) (2024-02-11)


* fix!: changing time to be double ([e632c85](https://github.com/James-Frowen/NetworkPositionSync/commit/e632c85137af3374fe62987bf53ef3a171d34cb8))


### BREAKING CHANGES

* time fields are now double instead of float

## [6.0.5](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.4...v6.0.5) (2024-02-08)


### Bug Fixes

* fixing compile error ([ffb3838](https://github.com/James-Frowen/NetworkPositionSync/commit/ffb3838f62f9b98b573ae4007408f4994df2d580))

## [6.0.4](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.3...v6.0.4) (2024-02-08)


### Bug Fixes

* fixing compile error ([1d20015](https://github.com/James-Frowen/NetworkPositionSync/commit/1d2001579ff80407f48f0dd545c2540e57a7718b))

## [6.0.3](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.2...v6.0.3) (2024-02-08)


### Bug Fixes

* improving default timescale value and adding value to inspector ([35aae3e](https://github.com/James-Frowen/NetworkPositionSync/commit/35aae3e1e18fc4bab92068c5c5ae4d1bc059082a))

## [6.0.2](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.1...v6.0.2) (2024-02-08)


### Bug Fixes

* improving time scaling ([b073950](https://github.com/James-Frowen/NetworkPositionSync/commit/b0739501063c08288e9a8745df23d531a6271780))

## [6.0.1](https://github.com/James-Frowen/NetworkPositionSync/compare/v6.0.0...v6.0.1) (2024-01-14)


### Bug Fixes

* adding check for time out of order ([4f01483](https://github.com/James-Frowen/NetworkPositionSync/commit/4f01483b239cdee0c4f13678a2c0269382490c21))
* fixing mirage version in package ([17dcb0c](https://github.com/James-Frowen/NetworkPositionSync/commit/17dcb0cab63c5f72a847675addc06809eb87f59e))

# [6.0.0](https://github.com/James-Frowen/NetworkPositionSync/compare/v5.0.1...v6.0.0) (2023-06-13)


### Bug Fixes

* updating to mirage 141.0.2 ([48f50b9](https://github.com/James-Frowen/NetworkPositionSync/commit/48f50b9830d903ffea2dbe62ae1de7cc0ace8b92))


### BREAKING CHANGES

* now requires mirage v141.0.2

## [5.0.1](https://github.com/James-Frowen/NetworkPositionSync/compare/v5.0.0...v5.0.1) (2023-06-12)


### Bug Fixes

* fixing null ref in SyncPositionBehaviour ([69ba59d](https://github.com/James-Frowen/NetworkPositionSync/commit/69ba59db9f2659f26ea66bb1cb3fc722a5e98fae))

# [5.0.0](https://github.com/James-Frowen/NetworkPositionSync/compare/v4.1.0...v5.0.0) (2023-04-05)


### Bug Fixes

* updating to mirage 131.0.0 ([ad2a82b](https://github.com/James-Frowen/NetworkPositionSync/commit/ad2a82b1a8d058f73611e9629a19e0049e435b3e))


### BREAKING CHANGES

* now requires mirage 131.0.0

# [4.1.0](https://github.com/James-Frowen/NetworkPositionSync/compare/v4.0.0...v4.1.0) (2022-09-25)


### Bug Fixes

* changing time to double for remove and debug methods ([bd0c0f0](https://github.com/James-Frowen/NetworkPositionSync/commit/bd0c0f0b2b4dd5f792e98920d3341d35e2234270))


### Features

* adding Channel setting to unreliable can be used ([65cfdb8](https://github.com/James-Frowen/NetworkPositionSync/commit/65cfdb8aaa6c4c062d7bc4df3a724aa257cac017))
* adding support for multiple Behaviours per gameobject ([ce1fe4b](https://github.com/James-Frowen/NetworkPositionSync/commit/ce1fe4b7a6b00e332f7c15eed08797e9157eafa5))
