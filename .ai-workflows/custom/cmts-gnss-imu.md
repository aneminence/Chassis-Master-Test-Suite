# CMTS GNSS / IMU notes (placeholder)

Reserved for project-specific guidance on GPS/IMU units, VBO/GSpot column quirks,
TrackMap filters, and sensor calibration.

**Do not invent content here.** Fill this file when CMTS owners document real
conventions (Racelogic vs GSpot, altitude units, outlier policy, etc.).

Until then, prefer:

- Existing code comments in `Recorder/VboReader.cs`, `Communication/GSpot/`
- Tests under `ChassisMasterTestSuite.Tests` (Vbo / GSpot / TrackProjection)
- Custom skill `.ai-workflows/custom/verification-loop/SKILL.md` F5 checklist
