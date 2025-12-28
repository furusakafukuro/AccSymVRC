# AccSymVRC

VRChat向け対称加速器シミュレータ（教育・展示用途）。

## Core Design
- Integrator: Relativistic Boris (standard)
- Time:
  - tPhysics: real physical time (SI seconds, sub-ns), fixed ΔtPhysics (UI configurable)
  - tAnimation: visual time (Unity Time.deltaTime)
- Deterministic multiplayer via seed + configVersion + startTick

## Current Policy
- Global (x,y,z) coordinates only (no x/y scaling, no s-coordinate)
- CPU billboard point rendering
- Field vector visualizers supported

See Docs/ for detailed specs.
