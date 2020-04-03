﻿/*
MIT License

Copyright (c) 2020 Steffen Vetne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityExtensions;

namespace PrecisionSurfaceEffects
{
    public class SurfaceOutputs : List<SurfaceOutput>
    {
        //This (in theory) pushes the weights downward from anchor so that there is never any "popping". It should bias itself to the remaining highest weights
        //(So long as the weights given were sorted, and there aren't any outputs culled past the maxCount + 1, yet)
        public void Downshift(int maxCount, float minWeight, float mult = 1)
        {
            //This all relies on having the outputs be sorted by decreasing weight
            for (int i = 0; i < Count; i++)
            {
                var val = this[i];
                val.normalizedWeight *= mult;
                this[i] = val;

                if (this[i].normalizedWeight < minWeight)
                {
                    RemoveAt(i);
                    i--;
                }
            }

            //int downID = maxCount;
            //if (Count > downID)
            if(Count > 0)
            {
                //for (int i = downID; i >= 0; i--)
                //{
                //    if (this[i].normalizedWeight < minWeight)
                //        downID = i;
                //    else
                //        break;
                //}

                float anchor = 1; // this[0].normalizedWeight; //1


                float min = minWeight;
                if(Count > maxCount)
                    min = Mathf.Max(min, this[maxCount].normalizedWeight);
                float downMult = anchor / (anchor - min);

                //Clears any extras
                while (Count > maxCount)
                    RemoveAt(Count - 1);

                
                for (int i = 0; i < Count; i++)
                {
                    var o = this[i];
                    o.normalizedWeight = (o.normalizedWeight - anchor) * downMult + anchor;
                    this[i] = o;
                }
            }
        }

        public SurfaceOutputs(SurfaceOutputs so) : base(so) { }
        public SurfaceOutputs() : base() { }
    }
    public struct SurfaceOutput
    {
        public int surfaceTypeID;
        public float normalizedWeight;
    }

    public partial class SurfaceData : ScriptableObject
    {
        //Constants
        private static readonly Color gizmoColor = Color.white * 0.75f;


        //Fields
        private static readonly List<Material> materials = new List<Material>();
        internal static readonly SurfaceOutputs outputs = new SurfaceOutputs();


        //Spherecast
        public SurfaceOutputs GetSphereCastSurfaceTypes
        (
            Vector3 worldPosition, Vector3 downDirection, float radius,
            int maxOutputCount = 1, bool shareList = false,
            float maxDistance = Mathf.Infinity, int layerMask = -1
        )
        {
            outputs.Clear();

            if (Physics.SphereCast(worldPosition, radius, downDirection, out RaycastHit rh, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
            {
#if UNITY_EDITOR
                var bottomCenter = worldPosition + downDirection * rh.distance;
                Debug.DrawLine(worldPosition, bottomCenter, gizmoColor);
                Debug.DrawLine(bottomCenter, rh.point, gizmoColor);
#endif

                AddSurfaceTypes(outputs, rh.collider, rh.point, maxOutputCount, triangleIndex: rh.triangleIndex);
            }

            return GetList(shareList);
        }

        //Raycast
        public SurfaceOutputs GetRaycastSurfaceTypes
        (
            Vector3 worldPosition, Vector3 downDirection, 
            int maxOutputCount = 1, bool shareList = false,
            float maxDistance = Mathf.Infinity, int layerMask = -1
        )
        {
            outputs.Clear();

            if (Physics.Raycast(worldPosition, downDirection, out RaycastHit rh, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
            {
#if UNITY_EDITOR
                Debug.DrawLine(worldPosition, rh.point, gizmoColor);
#endif

                AddSurfaceTypes(outputs, rh.collider, rh.point, maxOutputCount, triangleIndex: rh.triangleIndex);
            }

            return GetList(shareList);
        }

        //Collision
        public SurfaceOutputs GetCollisionSurfaceTypes(Collision collision, int maxOutputCount = 1, bool shareList = false)
        {
            outputs.Clear();
            AddSurfaceTypes(outputs, collision.collider, collision.GetContact(0).point, maxOutputCount);
            return GetList(shareList);
        }


        internal void AddSurfaceTypes(SurfaceOutputs outputs, Collider collider, Vector3 worldPosition, int maxOutputCount, int triangleIndex = -1)
        {
            if (collider != null)
            {
                if (collider is TerrainCollider tc) //it is a terrain collider
                {
                    AddTerrainSurfaceTypes(outputs, tc.GetComponent<Terrain>(), worldPosition, maxOutputCount);
                }
                else
                {
                    AddNonTerrainSurfaceTypes(outputs, collider, worldPosition, maxOutputCount, triangleIndex: triangleIndex);
                }
            }
        }
        private void AddTerrainSurfaceTypes(SurfaceOutputs outputs, Terrain terrain, Vector3 worldPosition, int maxOutputCount)
        {
            var mix = Utility.GetTextureMix(terrain, worldPosition);

            float totalMax = Mathf.Infinity;

            Continue:
            while (outputs.Count < maxOutputCount + 1) //adds an additional one to calculate the smooth distribution
            {
                var terrainIndex = Utility.GetMainTexture(mix, out float maxMix, totalMax);
                totalMax = maxMix;

                if (terrainIndex == -1)
                    return;

                var terrainTextureName = terrain.terrainData.terrainLayers[terrainIndex].diffuseTexture.name; //This might be terrible performance??

                for (int i = 0; i < surfaceTypes.Length; i++)
                {
                    var st = surfaceTypes[i];

                    for (int ii = 0; ii < st.terrainAlbedos.Length; ii++)
                    {
                        if (terrainTextureName == st.terrainAlbedos[ii].name)
                        {
                            bool success = false;
                            for (int iii = 0; iii < outputs.Count; iii++)
                            {
                                var output = outputs[iii];
                                if (output.surfaceTypeID == i)
                                {
                                    output.normalizedWeight += maxMix;
                                    outputs[iii] = output;
                                    success = true;
                                    break;
                                }
                            }

                            if(!success)
                                outputs.Add(new SurfaceOutput() { surfaceTypeID = i, normalizedWeight = maxMix });

                            goto Continue;
                        }
                    }
                }
            }
        }
        private void AddNonTerrainSurfaceTypes(SurfaceOutputs outputs, Collider collider, Vector3 worldPosition, int maxOutputCount, int triangleIndex = -1)
        {
            //Markers
            var marker = collider.GetComponent<Marker>();
            SurfaceBlendOverridesMarker blendMarker = null;
            if (marker is SurfaceTypeMarker typeMarker)
            {
                //Type Marker
                if (TryGetStringSurfaceType(typeMarker.reference, out int stID))
                    AddSingleOutput(stID);
                return;
            }
            else if (marker is SurfaceBlendMarker sbm)
            {
                AddBlends(sbm.blends.result, maxOutputCount);
            }
            else
                blendMarker = marker as SurfaceBlendOverridesMarker;

            var mr = collider.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                //The collider is a non-convex meshCollider. We can find the triangle index.
                if (triangleIndex != -1 && collider is MeshCollider mc && !mc.convex)
                {
                    int subMeshID = Utility.GetSubmesh(collider.GetComponent<MeshFilter>().sharedMesh, triangleIndex);

                    List <BlendResult> blendResults = null;
                    if(blendMarker != null)
                    {
                        for (int i = 0; i < blendMarker.subMaterials.Length; i++)
                        {
                            var sm = blendMarker.subMaterials[i];

                            if (sm.materialID == subMeshID)
                            {
                                blendResults = sm.result;
                                break;
                            }
                        }
                    }

                    if (blendResults == null)
                    {
                        //Gets Materials
                        materials.Clear();
                        mr.GetSharedMaterials(materials);

                        if(materialBlendLookup.TryGetValue(materials[subMeshID], out List<BlendResult> value))
                            blendResults = value;
                    }

                    if (blendResults != null)
                    {
                        AddBlends(blendResults, maxOutputCount);
                    }
                    else
                    {
                        //Gets Materials
                        materials.Clear();
                        mr.GetSharedMaterials(materials);

                        //Adds based on keywords
                        if (TryGetStringSurfaceType(materials[subMeshID].name, out int stID))
                            AddSingleOutput(stID);
                        return;
                    }
                }
                else
                {
                    //Defaults to the first material. For most colliders it can't be discerned which specific material it is
                    if (TryGetStringSurfaceType(mr.sharedMaterial.name, out int stID))
                        AddSingleOutput(stID);
                    return;
                }
            }
        }
        private void AddBlends(List<BlendResult> blendResults, int maxOutputCount)
        {
            //Adds the blends. If a blend's reference doesn't match any ST, then it will give it the default ST
            int blendCount = Mathf.Min(blendResults.Count, maxOutputCount + 1);
            for (int i = 0; i < blendCount; i++)
            {
                var blend = blendResults[i];

                if (!TryGetStringSurfaceType(blend.reference, out int stID))
                    stID = defaultSurfaceType;

                outputs.Add(new SurfaceOutput() { surfaceTypeID = stID, normalizedWeight = blend.normalizedWeight });
            }
        }

        public bool TryGetStringSurfaceType(string checkName, out int stID)
        {
            if (!System.String.IsNullOrEmpty(checkName))
            {
                checkName = checkName.ToLowerInvariant();

                for (int i = 0; i < surfaceTypes.Length; i++)
                {
                    stID = i;
                    var st = surfaceTypes[i];

                    for (int ii = 0; ii < st.materialKeywords.Length; ii++)
                    {
                        if (checkName.Contains(st.materialKeywords[ii].ToLowerInvariant())) //check if the material name contains the keyword
                            return true;
                    }
                }
            }

            stID = -1;
            return false;
        }

        private void AddSingleOutput(int stID)
        {
            outputs.Add(new SurfaceOutput() { surfaceTypeID = stID, normalizedWeight = 1 });
        }
        private SurfaceOutputs GetList(bool share)
        {
            if (outputs.Count == 0)
                AddSingleOutput(defaultSurfaceType);
     
            if (share)
                return outputs;
            else
                return new SurfaceOutputs(outputs);
        }
    }
}

/*
 *         private int GetMainTexture(Terrain terrain, Vector3 WorldPos, out float mix, float totalMax)
        {
            // returns the zero-based index of the most dominant texture
            // on the main terrain at this world position.
            float[] mixes = GetTextureMix(terrain, WorldPos);

            return GetMainTexture(mixes, out float mix, totalMax);
        }

 * 
                for (int iii = 0; iii < ss.clipVariants.Length; iii++)
                {
                    var cv = ss.clipVariants[iii];

                    if (cv.probabilityWeight == 0)
                        cv.probabilityWeight = 1;
                }

private static readonly List<int> subMeshTriangles = new List<int>(); //to avoid some amount of constant reallocation

if (mesh.isReadable)
{
    //Much slower version. I don't know if the faster version will be consistent though, because I don't know how unity does things internally, so if there are problems then see if this fixes it. In my testing the faster version works fine though:
    int[] triangles = mesh.triangles;

    var triIndex = rh.triangleIndex * 3;
    int a = triangles[triIndex + 0], b = triangles[triIndex + 1], c = triangles[triIndex + 2];

    for (int submeshID = 0; submeshID < mesh.subMeshCount; submeshID++)
    {
        subMeshTriangles.Clear();
        mesh.GetTriangles(subMeshTriangles, submeshID);

        for (int i = 0; i < subMeshTriangles.Count; i += 3)
        {
            int aa = subMeshTriangles[i + 0], bb = subMeshTriangles[i + 1], cc = subMeshTriangles[i + 2];
            if (a == aa && b == bb && c == cc)
            {
                checkName = materials[submeshID].name; //the triangle hit is within this submesh

                goto Found; //This exits the nested loop, to avoid any more comparisons (for performance)
            }
        }
    }
}

                    //Found:





#if UNITY_EDITOR
            [UnityEditor.CustomPropertyDrawer(typeof(Clip))]
            public class ClipDrawer : UnityEditor.PropertyDrawer
            {
                public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
                {
                    return UnityEditor.EditorGUIUtility.singleLineHeight;
                }

                public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
                {
                    var pw = property.FindPropertyRelative("probabilityWeight");
                    var c = property.FindPropertyRelative("clip");

                    var r = rect;
                    r.width *= 0.5f;
                    UnityEditor.EditorGUI.PropertyField(r, property, false);

                    r = rect;
                    r.width *= 0.5f;
                    UnityEditor.EditorGUI.PropertyField(r, pw);

                    r.x += r.width;
                    UnityEditor.EditorGUI.PropertyField(r, c, null as GUIContent);
                }

                private void OnEnable()
                {

                }
            }

#endif
*/