using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
public class MapGenerator : MonoBehaviour
{
    public enum DrawMode {NoiseMap, ColorMap, MeshMap}
    public DrawMode drawMode;
    
    public int mapWidth;
    public int mapHeight;
    public float noiseScale;
    
    public int octaves;
    [Range(0 , 1)]
    public float persistance;
    public float lacunarity;

    #region 对于persistance、 lacurity的理解
    //persistance 影响的是递减层对整个像素的权重；persistance 越大，后面的贡献值越高，反之最小时只有第一层有意义。
    //persistance 影响的是 递减层对整个像素的权重，persistance 越大，后面的贡献值越高， 反之最小的时候，只有第一层有意义
    // persistance 控制每层 amplitude 的衰减速度
    // 值越大 → 高频细节权重越高，地形越粗糙
    // 值越小 → 细节快速衰减，地形越平滑（极端为0时只有第一层有效）
    //amplitude *= persistance;
    
    //lacunarity 影响的是每层频率的递增速度；lacunarity 越大，后面的层细节越密；越小（接近1）则各层差不多，叠加意义不大。
    // lacunarity 控制每层 frequency 的增长速度
    // 值越大 → 每层细节密度差异越大，层次越分明（典型为2，即音乐里的"八度翻倍"）
    // 值越小（接近1）→ 各层频率相近，叠加层次感弱，多层失去意义
    //frequency *= lacunarity;
    #endregion

    public int seed;
    public Vector2 offset;

    public TerrainType[] regions;
    
    public bool autoUpdate;
    public void GenerateMap()
    {
        float[,] noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, seed, noiseScale, octaves, persistance, lacunarity, offset);

        Color[] colourMap = new Color[mapWidth * mapHeight];
        //根据noiseMap中的值与regions中的值进行比较，决定属于哪个区域，赋予对应的颜色
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float currentHeight = noiseMap[x , y];
                for (int i = 0; i < regions.Length; i++)
                {
                    if (currentHeight <= regions[i].height)
                    {
                        colourMap[y * mapWidth + x] = regions[i].colour;//存储判断后的值
                        break;
                    }
                }
            }
        }
        MapDisplay mapDisplay = FindObjectOfType<MapDisplay>();
        
        //根据选择的枚举模式判断渲染哪个
        if (drawMode == DrawMode.NoiseMap)
        {
            mapDisplay.DrawTextureMap(TextureGenerator.TextureFromHeightMap(noiseMap));
        }

        if (drawMode == DrawMode.ColorMap)
        {
            mapDisplay.DrawTextureMap(TextureGenerator.TextureFromColorMap(colourMap, mapWidth, mapHeight));
        }

        if (drawMode == DrawMode.MeshMap)
        {
            mapDisplay.DrawMesh(MeshGenerator.GenerateTerrainMesh(noiseMap), TextureGenerator.TextureFromColorMap(colourMap, mapWidth, mapHeight));
            //注意Unity 单个 Mesh 最多只能有 65535 个顶点（旧的 16 位索引限制），否则Mesh 会出错、数据错乱
        }
    }

    private void OnValidate()
    {
        if (mapWidth < 1)
        {
            mapWidth = 1;
        }
        if (mapHeight < 1)
        {
            mapHeight = 1;
        }
        if (octaves < 0)
        {
            octaves = 0;
        }
        if (lacunarity < 1)
        {
            lacunarity = 1;
        }
    }

    [System.Serializable]//可以让自定义的类在细节面板中显示
    public struct TerrainType
    {
        public string name;
        public float height;
        public Color colour;
    }
}
