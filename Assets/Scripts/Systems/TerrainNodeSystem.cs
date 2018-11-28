﻿using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Jobs;
using UnityEngine;
using Unity.Rendering;
using System.Collections.Generic;

public class TerrainNodeSystem : ComponentSystem
{

    private const float PERCENT_DIST_TO_SUBDIVIDE_AT = 100f;

    private struct MeshCreationSet
    {
        public Entity entity;
        public JobHandle jobHandle;
        public NativeArray<Vector3> verts;
        public NativeArray<int> tris;
    }
    private List<MeshCreationSet> meshCreationSets = new List<MeshCreationSet>();



    struct MeshBuildJob : IJob
    {
        public TerrainNode node;
        public float3 corner0;
        public float3 corner1;
        public float3 corner2;
        public int rez;
        public int nTris;
        public int nVerts;
        public NativeArray<Vector3> verts;
        public NativeArray<int> tris;

        public void Execute()
        {
            Vector3[] vertices = new Vector3[nVerts];
            //Vector3[] normals = new Vector3[nVerts];
            //Vector2[] uvs = new Vector2[nVerts];
            int[] indices = new int[nTris * 3];
            
            float dist01 = Vector3.Distance(corner0, corner1);
            float dist12 = Vector3.Distance(corner1, corner2);
            float dist20 = Vector3.Distance(corner2, corner0);
            
            float lenAxis01 = dist01 / (rez - 1);
            float lenAxis12 = dist12 / (rez - 1);
            float lenAxis20 = dist20 / (rez - 1);
            
            float3 add1 = math.normalize(corner1 - corner0) * lenAxis01;
            float3 add2 = math.normalize(corner2 - corner1) * lenAxis12;
            
            int vIdx = 0;

            for (int i = 0; i < rez; ++i)
            {
                for (int n = 0; n <= i; ++n)
                {
                    vertices[vIdx] = corner0 + add1 * i + add2 * n;
                    Vector3 normal = (vertices[vIdx]).normalized;
                    float noiseVal = GetValue(normal, node.noiseData, node.level);
                    vertices[vIdx] = normal * (node.planetData.radius + noiseVal * node.noiseData.finalValueMultiplier);
                    //vertices[vIdx] = normal * sphereRadius; //Use this line instead of above to gen a perfect sphere

                    //normals[vIdx] = normal;

                    ++vIdx;
                }
            }
            
            int indIdx = 0;
            int rowStartIdx = 1;
            int prevRowStartIdx = 0;

            for (int row = 0; row < rez - 1; ++row)
            {
                bool upright = true;
                int trisInRow = 1 + row * 2;
                int vertsInRowBottom = row + 2;

                int upTri = 0;
                int downTri = 0;

                for (int tri = 0; tri < trisInRow; ++tri)
                {
                    if (upright)
                    {
                        indices[indIdx] = rowStartIdx + upTri + 1;
                        indices[indIdx + 1] = rowStartIdx + upTri;
                        indices[indIdx + 2] = prevRowStartIdx + upTri;
                        ++upTri;
                    }
                    else
                    {
                        indices[indIdx] = prevRowStartIdx + downTri + 1;
                        indices[indIdx + 1] = rowStartIdx + downTri + 1;
                        indices[indIdx + 2] = prevRowStartIdx + downTri;
                        ++downTri;
                    }

                    indIdx += 3;
                    upright = !upright;
                }

                prevRowStartIdx = rowStartIdx;
                rowStartIdx += vertsInRowBottom;
            }
            
            verts.CopyFrom(vertices);
            tris.CopyFrom(indices);
        }
    }



    protected override void OnUpdate()
    {
        for(int i = meshCreationSets.Count - 1; i >= 0; --i)
        {
            if(meshCreationSets[i].jobHandle.IsCompleted)
            {
                meshCreationSets[i].jobHandle.Complete();

                if(EntityManager.Exists(meshCreationSets[i].entity))
                {
                    Mesh mesh = new Mesh();

                    mesh.vertices = meshCreationSets[i].verts.ToArray();
                    mesh.triangles = meshCreationSets[i].tris.ToArray();
                    mesh.RecalculateNormals();

                    MeshInstanceRenderer r = EntityManager.GetSharedComponentData<MeshInstanceRenderer>(meshCreationSets[i].entity);

                    r.mesh = mesh;

                    EntityManager.SetSharedComponentData(meshCreationSets[i].entity, r);

                    TerrainNode node = EntityManager.GetComponentData<TerrainNode>(meshCreationSets[i].entity);

                    if(node.level != 0)
                    {
                        TerrainNode parentNode = EntityManager.GetComponentData<TerrainNode>(node.parentEntity);

                        if(parentNode.divided == 1)
                        {
                            ++parentNode.childrenBuilt;
                            if(parentNode.childrenBuilt == 4)
                            {
                                MeshInstanceRenderer parentR = EntityManager.GetSharedComponentData<MeshInstanceRenderer>(node.parentEntity);
                                parentR.mesh = null;
                                EntityManager.SetSharedComponentData(node.parentEntity, parentR);
                            }

                            EntityManager.SetComponentData(node.parentEntity, parentNode);
                        }
                    }
                }

                meshCreationSets[i].verts.Dispose();
                meshCreationSets[i].tris.Dispose();

                meshCreationSets.RemoveAt(i);
            }
        }



        ComponentGroup nodeGroup = GetComponentGroup(typeof(TerrainNode), typeof(MeshInstanceRenderer), typeof(Position));
        ComponentGroup camGroup = GetComponentGroup(typeof(Flycam), typeof(Position), typeof(Rotation));
        ComponentGroup dataGroup = GetComponentGroup(typeof(PlanetSharedData));

        SharedComponentDataArray<PlanetSharedData> planetDataArray = dataGroup.GetSharedComponentDataArray<PlanetSharedData>();
        PlanetSharedData[] dataArray = new PlanetSharedData[planetDataArray.Length];
        for (int i = 0; i < dataArray.Length; ++i)
            dataArray[i] = planetDataArray[i];

        EntityArray entityTempArray = nodeGroup.GetEntityArray();
        Entity[] entityArray = new Entity[entityTempArray.Length];
        for (int i = 0; i < entityArray.Length; ++i)
            entityArray[i] = entityTempArray[i];

        SharedComponentDataArray<MeshInstanceRenderer> meshCDArray = nodeGroup.GetSharedComponentDataArray<MeshInstanceRenderer>();
        MeshInstanceRenderer[] meshArray = new MeshInstanceRenderer[meshCDArray.Length];
        for (int i = 0; i < meshArray.Length; ++i)
            meshArray[i] = meshCDArray[i];

        ComponentDataArray<TerrainNode> nodeCDArray = nodeGroup.GetComponentDataArray<TerrainNode>();
        TerrainNode[] nodeArray = new TerrainNode[nodeCDArray.Length];
        for (int i = 0; i < nodeCDArray.Length; ++i)
            nodeArray[i] = nodeCDArray[i];

        ComponentDataArray<Position> nodePosArray = nodeGroup.GetComponentDataArray<Position>();
        Position[] posArray = new Position[nodePosArray.Length];
        for (int i = 0; i < nodePosArray.Length; ++i)
            posArray[i] = nodePosArray[i];

        ComponentDataArray<Position> camPosArray = camGroup.GetComponentDataArray<Position>();
        float3 camPos = camPosArray[0].Value;



        for (int i = 0; i < meshArray.Length; ++i)
        {
            if (nodeArray[i].built == 1 && nodeArray[i].divided == 0)
            {
                if(nodeArray[i].level < nodeArray[i].planetData.maxNodeLevels)
                {
                    float3 corner0 = nodeArray[i].corner1;
                    float3 corner1 = nodeArray[i].corner2;
                    float3 corner2 = nodeArray[i].corner3;
                    float sphereRadius = nodeArray[i].planetData.radius;

                    float3 corner0Pos = corner0 * sphereRadius;
                    float3 corner1Pos = corner1 * sphereRadius;

                    float distToSubdivide = math.distance(corner0Pos, corner1Pos) * (PERCENT_DIST_TO_SUBDIVIDE_AT / 100f);

                    float3 centerPoint = (math.normalize(corner0 + corner1 + corner2) * sphereRadius);
                    float dist = math.distance(camPos, centerPoint);

                    if (dist < distToSubdivide)
                        Subdivide(entityArray[i], nodeArray[i], posArray[i], meshArray[i], dataArray[0]);
                }
                if(nodeArray[i].level > 0)
                {
                    MeshInstanceRenderer parentR
                        = EntityManager.GetSharedComponentData<MeshInstanceRenderer>(nodeArray[i].parentEntity);
                    float dist = math.distance(camPos, nodeArray[i].parentCenter);

                    if (parentR.mesh != null && dist >= nodeArray[i].parnetSubdivideDist)
                        EntityManager.DestroyEntity(entityArray[i]);
                }
            }
            else if(nodeArray[i].built == 0 && nodeArray[i].divided == 1)
            {
                float3 corner0 = nodeArray[i].corner1;
                float3 corner1 = nodeArray[i].corner2;
                float3 corner2 = nodeArray[i].corner3;
                float sphereRadius = nodeArray[i].planetData.radius;

                float3 corner0Pos = corner0 * sphereRadius;
                float3 corner1Pos = corner1 * sphereRadius;

                float distToSubdivide = math.distance(corner0Pos, corner1Pos) * (PERCENT_DIST_TO_SUBDIVIDE_AT / 100f);

                float3 centerPoint = (math.normalize(corner0 + corner1 + corner2) * sphereRadius);
                float dist = math.distance(camPos, centerPoint);
                
                if (dist >= distToSubdivide)
                {
                    nodeArray[i].divided = 0;
                    nodeArray[i].childrenBuilt = 0;
                    EntityManager.SetComponentData(entityArray[i], nodeArray[i]);
                }
            }
            else if(nodeArray[i].built == 0 && nodeArray[i].divided == 0)
            {
                nodeArray[i].built = 1;

                Planet planetData = nodeArray[i].planetData;

                //float3 corner1 = nodeArray[i].corner1 * planetData.radius;
                //float3 corner2 = nodeArray[i].corner2 * planetData.radius;
                //float3 corner3 = nodeArray[i].corner3 * planetData.radius;

                // rez is the number of vertices on one side of the mesh/triangle
                // the part in parentheses is called the "Mersenne Number"
                int rez = 2 + ((int)Mathf.Pow(2, planetData.meshSubdivisions) - 1);
                // nTris is the number of tris in the mesh
                int t = rez - 2;
                int nTris = (t * (t + 1)) + (rez - 1);
                // nVerts is the number of vertices in the mesh
                // it is the formula for the "Triangle Sequence" of numbers
                int nVerts = (rez * (rez + 1)) / 2;

                NativeArray<Vector3> verts = new NativeArray<Vector3>(nVerts, Allocator.Persistent);
                NativeArray<int> tris = new NativeArray<int>(nTris * 3, Allocator.Persistent);

                MeshBuildJob job = new MeshBuildJob();
                job.node = nodeArray[i];
                job.corner0 = nodeArray[i].corner3;
                job.corner1 = nodeArray[i].corner2;
                job.corner2 = nodeArray[i].corner1;
                job.rez = rez;
                job.nTris = nTris;
                job.nVerts = nVerts;
                job.verts = verts;
                job.tris = tris;
                
                JobHandle handle = job.Schedule();
                JobHandle.ScheduleBatchedJobs();
                
                MeshCreationSet mcs = new MeshCreationSet();
                mcs.entity = entityArray[i];
                mcs.jobHandle = handle;
                mcs.verts = verts;
                mcs.tris = tris;
                meshCreationSets.Add(mcs);
                
                //Mesh mesh = BuildMesh(corners, planetData.meshSubdivisions, planetData.radius, nodeArray[i].noiseData, nodeArray[i].level);
                //
                //meshArray[i].mesh = mesh;
                //Entity e = entityArray[i];
                //EntityManager.SetSharedComponentData(e, meshArray[i]);
                EntityManager.SetComponentData(entityArray[i], nodeArray[i]);
            }
        }

    }
    
    protected override void OnStopRunning()
    {
        for(int i = 0; i < meshCreationSets.Count; ++i)
        {
            meshCreationSets[i].verts.Dispose();
            meshCreationSets[i].tris.Dispose();
        }
    }



    public static Mesh BuildMesh(Vector3[] corners, int divisions, float sphereRadius, PlanetNoise noiseData, int nodeLevel)
    {
        // rez is the number of vertices on one side of the mesh/triangle
        // the part in parentheses is called the "Mersenne Number"
        int rez = 2 + ((int)Mathf.Pow(2, divisions) - 1);
        // nTris is the number of tris in the mesh
        int t = rez - 2;
        int nTris = (t * (t + 1)) + (rez - 1);
        // nVerts is the number of vertices in the mesh
        // it is the formula for the "Triangle Sequence" of numbers
        int nVerts = (rez * (rez + 1)) / 2;
        
        Vector3[] vertices = new Vector3[nVerts];
        Vector3[] normals = new Vector3[nVerts];
        Vector2[] uvs = new Vector2[nVerts];
        Color[] vColors = new Color[nVerts];
        int[] indices = new int[nTris * 3];

        float dist01 = Vector3.Distance(corners[0], corners[1]);
        float dist12 = Vector3.Distance(corners[1], corners[2]);
        float dist20 = Vector3.Distance(corners[2], corners[0]);

        float lenAxis01 = dist01 / (rez - 1);
        float lenAxis12 = dist12 / (rez - 1);
        float lenAxis20 = dist20 / (rez - 1);

        Vector3 add1 = (corners[1] - corners[0]).normalized * lenAxis01;
        Vector3 add2 = (corners[2] - corners[1]).normalized * lenAxis12;

        int vIdx = 0;

        for (int i = 0; i < rez; ++i)
        {
            for (int n = 0; n <= i; ++n)
            {
                vertices[vIdx] = corners[0] + add1 * i + add2 * n;
                Vector3 normal = (vertices[vIdx]).normalized;
                float noiseVal = GetValue(normal, noiseData, nodeLevel);
                vertices[vIdx] = normal * (sphereRadius + noiseVal * noiseData.finalValueMultiplier);
                //vertices[vIdx] = normal * sphereRadius; //Use this line instead of above to gen a perfect sphere

                normals[vIdx] = normal;

                vColors[vIdx] = GetVertexColor(noiseData, noiseVal);

                ++vIdx;
            }
        }

        int indIdx = 0;
        int rowStartIdx = 1;
        int prevRowStartIdx = 0;

        for (int row = 0; row < rez - 1; ++row)
        {
            bool upright = true;
            int trisInRow = 1 + row * 2;
            int vertsInRowBottom = row + 2;

            int upTri = 0;
            int downTri = 0;

            for (int tri = 0; tri < trisInRow; ++tri)
            {
                if (upright)
                {
                    indices[indIdx] = rowStartIdx + upTri + 1;
                    indices[indIdx + 1] = rowStartIdx + upTri;
                    indices[indIdx + 2] = prevRowStartIdx + upTri;
                    ++upTri;
                }
                else
                {
                    indices[indIdx] = prevRowStartIdx + downTri + 1;
                    indices[indIdx + 1] = rowStartIdx + downTri + 1;
                    indices[indIdx + 2] = prevRowStartIdx + downTri;
                    ++downTri;
                }

                indIdx += 3;
                upright = !upright;
            }

            prevRowStartIdx = rowStartIdx;
            rowStartIdx += vertsInRowBottom;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.colors = vColors;
        //mesh.uv = uvs;
        //mesh.normals = normals;
        mesh.RecalculateNormals();
        return mesh;
    }



    private static Color GetVertexColor(PlanetNoise noiseData, float heightFromSeaLevel)
    {
        int layerIdx = 0;

        return Color.gray;

        //int len = noiseData.colorLayers.Length;
        //for (int i = 1; i < len; ++i)
        //{
        //    PlanetColorLayer layer = noiseData.colorLayers[i];
        //
        //    if (layer.heightThreshold < heightFromSeaLevel)
        //        layerIdx = i;
        //    else
        //        break;
        //}
        //
        //return new Color(noiseData.colorLayers[layerIdx].r,
        //                 noiseData.colorLayers[layerIdx].g,
        //                 noiseData.colorLayers[layerIdx].b);
    }



    private void Subdivide(Entity e, TerrainNode t, Position p, MeshInstanceRenderer r, PlanetSharedData d)
    {
        Entity[] entities = new Entity[4];
        TerrainNode[] nodes = new TerrainNode[4];

        float3 corner0 = t.corner1;
        float3 corner1 = t.corner2;
        float3 corner2 = t.corner3;

        for (int i = 0; i < 4; ++i)
        {
            entities[i] = EntityManager.Instantiate(d.nodePrefab);
            nodes[i] = EntityManager.GetComponentData<TerrainNode>(entities[i]);
            nodes[i].level = t.level + 1;
            nodes[i].planetData = t.planetData;
            nodes[i].noiseData = t.noiseData;
            nodes[i].built = 0;
            nodes[i].divided = 0;
            nodes[i].childrenBuilt = 0;
            nodes[i].parentEntity = e;
            
            float sphereRadius = t.planetData.radius;

            float3 corner0Pos = corner0 * sphereRadius;
            float3 corner1Pos = corner1 * sphereRadius;

            float distToSubdivide = math.distance(corner0Pos, corner1Pos) * (PERCENT_DIST_TO_SUBDIVIDE_AT / 100f);

            float3 centerPoint = (math.normalize(corner0 + corner1 + corner2) * sphereRadius);

            nodes[i].parentCenter = centerPoint;
            nodes[i].parnetSubdivideDist = distToSubdivide;

            //GameObject child = Instantiate(parentSphere.NodePrefab);
            //child.transform.position = transform.position;
            //child.transform.rotation = transform.rotation;
            //child.transform.parent = transform;
            //children[i] = child.GetComponent<PlatosphereNode>();
        }
        
        float3 mid01 = corner1 - corner0;
        mid01 = corner0 + math.normalize(mid01) * (math.length(mid01) / 2f);
        float3 mid02 = corner2 - corner0;
        mid02 = corner0 + math.normalize(mid02) * (math.length(mid02) / 2f);
        float3 mid12 = corner2 - corner1;
        mid12 = corner1 + math.normalize(mid12) * (math.length(mid12) / 2f);

        Vector3[] corners0 = new Vector3[] { corner0, mid01, mid02 }; //top
        Vector3[] corners1 = new Vector3[] { mid01, corner1, mid12 }; //left
        Vector3[] corners2 = new Vector3[] { mid02, mid12, corner2 }; //right
        Vector3[] corners3 = new Vector3[] { mid02, mid01, mid12 }; //center
        
        nodes[0].corner1 = corner0;
        nodes[0].corner2 = mid01;
        nodes[0].corner3 = mid02;

        nodes[1].corner1 = mid01;
        nodes[1].corner2 = corner1;
        nodes[1].corner3 = mid12;

        nodes[2].corner1 = mid02;
        nodes[2].corner2 = mid12;
        nodes[2].corner3 = corner2;

        nodes[3].corner1 = mid02;
        nodes[3].corner2 = mid01;
        nodes[3].corner3 = mid12;

        for (int i = 0; i < 4; ++i)
            EntityManager.SetComponentData(entities[i], nodes[i]);

        //r.mesh = null;

        t.divided = 1;
        t.built = 0;

        //EntityManager.SetSharedComponentData(e, r);
        EntityManager.SetComponentData(e, t);
    }



    public static float GetValue(float x, float y, float z, PlanetNoise noiseData, int level = 0)
    {
        float ret = GetNoiseValue(x, y, z, noiseData, level);
        //ret = settings.heightCurve.Evaluate(ret);
        
        return ret;
    }

    public static float GetNoiseValue(float x, float y, float z, PlanetNoise noiseData, int level = 0)
    {
        float localFreq = noiseData.frequency;
        float localAmp = noiseData.amplitude;
        
        float maxValue = 0f;
        float ret = 0f;

        FastNoise fastNoise = new FastNoise(noiseData.seed);

        for (int i = 0; i < noiseData.octaves + level * 1; ++i)
        {
            //ret += noiseClass.GetValue(x * localFreq, y * localFreq, z * localFreq) * localAmp;
            ret += fastNoise.GetSimplex(x * localFreq, y * localFreq, z * localFreq) * localAmp;

            maxValue += localAmp;

            localFreq *= noiseData.lacunarity;
            localAmp *= noiseData.persistence;
        }

        return ret / maxValue;
    }

    public static float GetValue(Vector3 pos, PlanetNoise noiseData, int level = 0)
        { return GetValue(pos.x, pos.y, pos.z, noiseData, level); }

}
