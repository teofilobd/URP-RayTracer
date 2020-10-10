# URP-RayTracer

This is just a port of the GPU Ray Tracer described in [@daerst](https://twitter.com/Daerst)'s [blog series](http://three-eyed-games.com/2018/05/03/gpu-ray-tracing-in-unity-part-1/) to Unity's URP with a few additions. 

**Disclaimer:** This is not a finished work, so you might expect bugs and instability. I was planning to commit it after having something more complete in terms of features, but I jumped into some other personal project and never returned to this :P. I decided to commit the way it is because it might help someone out there or serve as a starting point to people as well.

As far as I remember, the features I added were:
- Frustum culling in a separate compute buffer. 
- Materials with URP Lit shader can be used by Meshes (GameObjects must have a RayTracingMeshObject script attached). The properties used from material are: smoothness, spec color, base color, emission color and base texture.   
- Texture mapping (all textures must have the same size).
