This directory must contain the blade surface geometry before the pipeline
is run:

    constant/triSurface/fan.stl

Referenced by:
  - system/surfaceFeatureExtractDict  (feature edge extraction -> fan.eMesh)
  - system/snappyHexMeshDict          (castellatedMeshControls.geometry / refinementSurfaces)

Requirements on the STL itself:
  - Single solid, named "fan" in the STL header (solid fan / endsolid fan),
    matching the "fan" entry used in snappyHexMeshDict's geometry block.
  - Units: metres, consistent with convertToMeters 1 in blockMeshDict.
  - Watertight/manifold closed surface (snappyHexMesh will produce a bad
    mesh or hang on castellation if it isn't).
  - Positioned in the same coordinate frame as blockMeshDict: rotation
    axis = Z, centred on (0 0 z), sitting at approximately
    z = domainLength / 3 from the inlet (z=0) plane — see the
    domainLength/3-derived rotorZone bounds in topoSetDict for the
    exact axial window the orchestrator assumes the blade occupies.

Until this file is present, surfaceFeatureExtract and snappyHexMesh will
fail with a missing-file error, same as the original snappyHexMeshDict gap.
