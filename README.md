# Microsplat Surface Sounds

Allows you to find audioClips in Unity depending on what a spherecast or raycast hits, aka footsteps. You could alternatively find it using `GetCollisionSurfaceType(Collision collision)`, but since Collisions don't know the triangleIndex it can't discern between submeshes.

Uses: https://github.com/garettbass/UnityExtensions.ArrayDrawer for some reorderability

The test scene requires MicroSplat's Examples

## Usage

### Steps
Create a SurfaceSounds asset in Unity's project window (by right clicking -> Create -> SurfaceSounds)

The component `SurfaceSoundTester.cs` can be used to test it out.

Delete the folder "Microsplat Example (Incomplete) Test - Delete this garbage" after you have tried it out (if you want to try it out). It is complete garbage apart from being a mediocre demonstration

The easiest thing is to reference the SurfaceSounds asset, and then do `surfaceSounds.GetRaycastSurfaceType(pos, Vector3.down).GetSoundSet().PlayOneShot(audioSource);`

You could use `GetRaycastSurfaceType`, but games often have entities magically floating with one or fewer feet on a ledge, and in that case the raycast would pass all the way to the bottom of the cliff. That's when it is good to use `GetSphereCastSurfaceType` with a larger radius.

### Terrain
If the spherecast hits a TerrainCollider, it tests against the indices (which were found using MicroSplat's Config file and Albedo (Diffuse) textures you specify).

### MeshRenderers
If the collider is not a TerrainCollider, it will test if the name of the MeshRenderer's material/s includes one of the keywords.

A collider has to be a non-convex MeshCollider to discern between the MeshRenderer's (submesh) materials. Otherwise it will default to the first material.
This can be a problem if the first material is not the one you want to test against.
That's when you can use the component `SurfaceTypeMarker.cs` to override the test string.

The collider has to be on the same GameObject as the MeshRenderer. If it is not, then use a `SurfaceTypeMarker`.

The material's name can include the keyword with any capitalization:
    Grass
    GRASS
    grass
    GrAsS
    
Keywords shouldn't contains another SurfaceType's keyword

### Config
If you want to change the MicroSplat config during runtime, you need to use `SurfaceSounds.InitConfig(JBooth.MicroSplat.TextureArrayConfig textureArrayConfig)`

### Performance
You can cache the `SurfaceType` from `GetSurfaceType`, to update it less frequently. However, the performance should be ok, and the player should definitely be up to date (found every time you want to play a footstep sound, not every frame).

### Default Surface Type
If a SurfaceType can't be found (if none of the SurfaceTypes include the Terrain index, or if none of the SurfaceTypes' keywords are included in the check string (the Material name or Marker reference)) the `defaultSurfaceType` is sent.

### Sound Sets
SoundSets are used for different sized creatures, although you don't need to use multiple. 

The `soundSetID` can be found with `FindSoundSetID(name)`, where the string will be searched for in `soundSetNames`.
The default for `GetSoundSet(soundSetID)` is the first one (0 id). 

You'll need to be diligent in reordering every `SoundSet` individually, as well as ensure that each `SoundSet` is included in each `SurfaceType`.

### Clip Variants
You can give the different clips different probabilityWeights. They are normalized, so if you give one clip 12, another 6, and another 2, since their sum is: 12 + 6 + 2 = 20, their actual probabilities are: (12 / 20), (6 / 20), and (2 / 20). 

### Randomized Volume/Pitch
You can get a randomized volume and pitch. `SurfaceType`'s `SoundSet`s have individual control over the amount, for example Concrete shouldn't be as randomized as e.g. Mud.

## Limitations

- Only works for MicroSplat. 
This can be changed:
    - remove `textureArrayConfig` 
    - remove `[HideInInspector]` from above `public int[] terrainIndices;`
    - remove the `InitConfig` function
    - remove `terrainAlbedos`, as that will no longer be used
    
    - Then you will need to specify the terrain indices manually
    
- Material names need to be proper (only include the relevant keyword, not another SurfaceType's keyword. Otherwise it might find the wrong SurfaceType)

## Follow me on Twitter

https://twitter.com/PrecisionCats
